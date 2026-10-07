#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using nadena.dev.ndmf.preview;

namespace Meshia.MeshSimplification.Ndmf.Editor.Preview
{
    internal abstract class MeshiaMeshSimplifierPreviewBase<TDerived> : IRenderFilter
        where TDerived : MeshiaMeshSimplifierPreviewBase<TDerived>
    {
        public static readonly Dictionary<Renderer, (int proxy, int simplified)> TriangleCountCache = new();
        public static TogglablePreviewNode PreviewControlNode { get; } = TogglablePreviewNode.Create(
            () => typeof(TDerived).Name, qualifiedName: typeof(TDerived).FullName);
        private static readonly TogglablePreviewNode[] PreviewControlNodes = { PreviewControlNode };
        public IEnumerable<TogglablePreviewNode> GetPreviewControlNodes() => PreviewControlNodes;
        public bool IsEnabled(ComputeContext context) => context.Observe(PreviewControlNode.IsEnabled);
        public static bool IsEnabled() => PreviewControlNode.IsEnabled.Value;
        public abstract ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context);

        Task<IRenderFilterNode> IRenderFilter.Instantiate(RenderGroup group,
            IEnumerable<(Renderer, Renderer)> proxyPairs, ComputeContext context)
            => Build(group, proxyPairs.ToArray(), context, null, RenderAspects.Everything);

        private async Task<IRenderFilterNode> Build(RenderGroup group, (Renderer, Renderer)[] pairs,
            ComputeContext context, Node? previous, RenderAspects changes)
        {
            var renderers = pairs.Select(p => p.Item1).ToArray();
            var request = PreviewActivity.Begin(renderers);
            Prepared? prepared = null;
            var outputs = new Dictionary<Renderer, Output>();
            using var cancellation = new CancellationTokenSource();
            using var subscription = context.InvokeOnInvalidate(cancellation, c => c.Cancel());
            try
            {
                var originals = pairs.Select(p => RendererUtility.GetRequiredMesh(p.Item2)).ToArray();
                var settings = new Settings[pairs.Length];
                var publishers = new SliderMeasurementPublisher?[pairs.Length];
                var supportsCuts = new bool[pairs.Length];
                for (var i = 0; i < pairs.Length; i++)
                {
                    var (original, proxy) = pairs[i];
                    var selected = IncludesRenderer(context, group, original);
                    var (target, options, bones) = selected ? QueryTarget(context, group, original, proxy)
                        : (new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount,
                            Value = originals[i].GetTriangleCount() }, MeshSimplifierOptions.Default, (BitArray?)null);
                    options = NdmfPlugin.ResolvePreviewOptions(context, context.GetAvatarRoot(original.gameObject), original, options);
                    options = VisibilityCutProtection.Resolve(proxy, options, context);
                    settings[i] = new Settings(selected, target, options, bones);
                    if (selected) publishers[i] = CaptureMeasurementPublisher(context, group, original);
                    supportsCuts[i] = CutMeshPreparation.Supports(target, options);
                }

                // Targets only change the stopping count. Cuts and overlap regions
                // remain valid until their observations, upstream geometry or pose change.
                var poses = pairs.Select(p => new Pose(p.Item2)).ToArray();
                if (previous != null && changes == 0 && previous.Inputs.CanReuse(renderers, originals, supportsCuts, poses))
                    prepared = previous.Inputs.Retain();
                else prepared = new Prepared(pairs, originals, supportsCuts, settings, poses);
                prepared.Context.Invalidates(context);

                var work = new List<int>();
                for (var i = 0; i < pairs.Length; i++)
                {
                    if (!settings[i].Selected) continue;
                    var setting = settings[i];
                    if (setting.Target.Kind == MeshSimplificationTargetKind.FaQemTriangleCount)
                        setting.Options = CutOverlapProtection.ApplyResolved(setting.Options, prepared.Protections[i]);
                    settings[i] = setting;
                    if (previous != null && prepared == previous.Inputs && previous.Settings[i].Same(setting) &&
                        previous.Outputs.TryGetValue(renderers[i], out var cached))
                        outputs.Add(renderers[i], cached.Retain());
                    else work.Add(i);
                }

                // Limit peak native memory on cold previews. Outdated requests stop
                // scheduling further meshes and never publish stale triangle counts.
                var cursor = 0;
                async Task Worker()
                {
                    while (cursor < work.Count)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        var i = work[cursor++];
                        var output = new Output(new Mesh());
                        outputs.Add(renderers[i], output);
                        var setting = settings[i];
                        if (setting.Target.Kind == MeshSimplificationTargetKind.FaQemTriangleCount &&
                            setting.Target.Value < prepared.Inputs[i].Mesh.GetTriangleCount())
                        {
                            var replay = await prepared.GetReplay(i, setting);
                            cancellation.Token.ThrowIfCancellationRequested();
                            await replay.WriteAsync((int)setting.Target.Value, output.Mesh, cancellation.Token);
                        }
                        else await MeshSimplifier.SimplifyAsync(prepared.Inputs[i].Mesh, setting.Target,
                            setting.Options, setting.Bones, output.Mesh, cancellation.Token);
                    }
                }
                await Task.WhenAll(Worker(), Worker());
                cancellation.Token.ThrowIfCancellationRequested();
                return new Node(this, group, renderers, prepared, settings, outputs, request, work.Count, publishers);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                foreach (var output in outputs.Values) output.Dispose();
                prepared?.Dispose();
                PreviewActivity.Finish(renderers, request);
                // NDMF treats faulted tasks as errors, and older versions do not
                // retire cancelled pipeline tasks. Complete the invalidated node
                // normally so it can advance to the newest request. Keep the last
                // displayed mesh where available instead of publishing partial work.
                return previous ?? (IRenderFilterNode)new RetryNode(this, group);
            }
            catch
            {
                foreach (var output in outputs.Values) output.Dispose();
                prepared?.Dispose();
                PreviewActivity.Finish(renderers, request);
                throw;
            }
        }

        protected virtual bool IncludesRenderer(ComputeContext context, RenderGroup group, Renderer original) => true;
        protected delegate IDisposable? SliderMeasurementPublisher(FaQemCountProfile? profile,
            Func<Task<FaQemCountProfile>> measureAsync, int sourceCount, int requested, int produced);
        protected virtual SliderMeasurementPublisher? CaptureMeasurementPublisher(
            ComputeContext context, RenderGroup group, Renderer original) => null;
        protected abstract (MeshSimplificationTarget, MeshSimplifierOptions, BitArray?) QueryTarget(
            ComputeContext context, RenderGroup group, Renderer original, Renderer proxy);

        private sealed class RetryNode : IRenderFilterNode
        {
            private readonly MeshiaMeshSimplifierPreviewBase<TDerived> owner;
            private readonly RenderGroup group;
            internal RetryNode(MeshiaMeshSimplifierPreviewBase<TDerived> owner, RenderGroup group)
            { this.owner = owner; this.group = group; }
            public RenderAspects WhatChanged => RenderAspects.Mesh;
            public Task<IRenderFilterNode> Refresh(IEnumerable<(Renderer, Renderer)> pairs, ComputeContext context, RenderAspects changes)
                => owner.Build(group, pairs.ToArray(), context, null, RenderAspects.Everything);
        }

        private struct Settings
        {
            internal readonly bool Selected;
            internal readonly MeshSimplificationTarget Target;
            internal MeshSimplifierOptions Options;
            internal readonly BitArray? Bones;
            internal Settings(bool selected, MeshSimplificationTarget target, MeshSimplifierOptions options, BitArray? bones)
            {
                Selected = selected; Target = target; Options = options;
                Bones = bones == null ? null : (BitArray)bones.Clone();
            }
            internal bool Same(Settings other) => Selected == other.Selected && Target == other.Target && SameOptions(other);
            internal bool SameOptions(Settings other) => Options == other.Options && (Bones == null ? other.Bones == null : other.Bones != null &&
                    Bones.Cast<bool>().SequenceEqual(other.Bones.Cast<bool>()));
        }

        private sealed class Pose
        {
            private readonly Matrix4x4 world;
            private readonly Matrix4x4[] bones;
            private readonly float[] weights;
            internal Pose(Renderer renderer)
            {
                world = renderer.localToWorldMatrix;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    bones = skin.bones.Select(b => b == null ? default : b.localToWorldMatrix).ToArray();
                    weights = Enumerable.Range(0, skin.sharedMesh.blendShapeCount).Select(skin.GetBlendShapeWeight).ToArray();
                }
                else { bones = Array.Empty<Matrix4x4>(); weights = Array.Empty<float>(); }
            }
            internal bool Same(Pose other) => world.Equals(other.world) && bones.SequenceEqual(other.bones) && weights.SequenceEqual(other.weights);
        }

        private sealed class Prepared : IDisposable
        {
            internal readonly ComputeContext Context = new("Meshia prepared cut geometry");
            internal readonly List<CutMeshPreparation> Inputs = new();
            internal readonly CutOverlapProtection.Protection[] Protections;
            internal readonly Mesh[] Originals;
            private readonly Renderer[] renderers;
            private readonly bool[] supportsCuts;
            private readonly Pose[] poses;
            private readonly Dictionary<int, (Settings settings, Task<MeshSimplifier.FaQemReplay> task)> replays = new();
            private int references = 1;
            internal Prepared((Renderer, Renderer)[] pairs, Mesh[] originals, bool[] supports,
                Settings[] settings, Pose[] poses)
            {
                Originals = originals; supportsCuts = supports; this.poses = poses;
                renderers = pairs.Select(p => p.Item1).ToArray();
                try
                {
                    var observedTransforms = new HashSet<Transform>();
                    void ObserveParents(Transform transform)
                    {
                        for (var t = transform; t != null && observedTransforms.Add(t); t = t.parent) Context.Observe(t);
                    }
                    for (var i = 0; i < pairs.Length; i++)
                    {
                        var original = pairs[i].Item1;
                        Context.Observe(original); Context.Observe(originals[i]);
                        ObserveParents(original.transform);
                        if (original is SkinnedMeshRenderer skin)
                            foreach (var bone in skin.bones) if (bone != null) ObserveParents(bone);
                        Inputs.Add(CutMeshPreparation.Prepare(original, originals[i], settings[i].Target, settings[i].Options, Context));
                    }
                    Protections = CutOverlapProtection.CalculateResolved(pairs.Select(p => p.Item2).ToArray(),
                        Inputs.Select(p => p.Mesh).ToArray(), originals);
                }
                catch { Dispose(); throw; }
            }
            internal bool CanReuse(Renderer[] currentRenderers, Mesh[] originals, bool[] supports, Pose[] currentPoses) =>
                !Context.IsInvalidated && renderers.SequenceEqual(currentRenderers) &&
                Originals.SequenceEqual(originals) && supportsCuts.SequenceEqual(supports) &&
                poses.Length == currentPoses.Length && poses.Select((p, i) => p.Same(currentPoses[i])).All(same => same);
            internal Task<MeshSimplifier.FaQemReplay> GetReplay(int index, Settings settings)
            {
                if (replays.TryGetValue(index, out var entry) && entry.settings.SameOptions(settings) && !entry.task.IsFaulted)
                    return entry.task;
                var task = MeshSimplifier.PrepareFaQemReplayAsync(Inputs[index].Mesh, settings.Options, settings.Bones);
                replays[index] = (settings, task);
                return task;
            }
            internal Prepared Retain() { references++; return this; }
            internal FaQemCountProfile? CompletedProfile(int index, Settings settings) =>
                replays.TryGetValue(index, out var entry) && entry.settings.SameOptions(settings) &&
                entry.task.Status == TaskStatus.RanToCompletion ? entry.task.Result.Counts : null;
            public void Dispose()
            {
                if (--references != 0) return;
                Context.Invalidate();
                foreach (var input in Inputs) input.Dispose();
            }
        }

        private sealed class Output : IDisposable
        {
            internal readonly Mesh Mesh;
            private int references = 1;
            internal Output(Mesh mesh) => Mesh = mesh;
            internal Output Retain() { references++; return this; }
            public void Dispose() { if (--references == 0) UnityEngine.Object.DestroyImmediate(Mesh); }
        }

        private sealed class Node : IRenderFilterNode
        {
            private readonly MeshiaMeshSimplifierPreviewBase<TDerived> owner;
            private readonly RenderGroup group;
            private readonly Renderer[] renderers;
            internal readonly Prepared Inputs;
            internal readonly Settings[] Settings;
            internal readonly Dictionary<Renderer, Output> Outputs;
            private readonly long request;
            internal readonly int RecomputedMeshes;
            private bool disposed;
            private bool displayed;
            private readonly SliderMeasurementPublisher?[] publishers;
            private readonly List<IDisposable> measurements = new();
            internal Node(MeshiaMeshSimplifierPreviewBase<TDerived> owner, RenderGroup group, Renderer[] renderers,
                Prepared inputs, Settings[] settings, Dictionary<Renderer, Output> outputs, long request, int recomputed,
                SliderMeasurementPublisher?[] publishers)
            {
                this.owner = owner; this.group = group; this.renderers = renderers;
                Inputs = inputs; Settings = settings; Outputs = outputs; this.request = request; RecomputedMeshes = recomputed;
                this.publishers = publishers;
            }
            public RenderAspects WhatChanged => RenderAspects.Mesh;
            public Task<IRenderFilterNode> Refresh(IEnumerable<(Renderer, Renderer)> pairs, ComputeContext context, RenderAspects changes)
                => owner.Build(group, pairs.ToArray(), context, this, changes);
            public void OnFrame(Renderer original, Renderer proxy)
            {
                if (Outputs.TryGetValue(original, out var output)) RendererUtility.SetMesh(proxy, output.Mesh);
            }
            public void OnFrameGroup()
            {
                if (displayed) return;
                displayed = true;
                for (var i = 0; i < renderers.Length; i++)
                    if (Outputs.TryGetValue(renderers[i], out var output))
                    {
                        TriangleCountCache[renderers[i]] = (Inputs.Originals[i].GetTriangleCount(), output.Mesh.GetTriangleCount());
                        var index = i;
                        if (publishers[i]?.Invoke(Inputs.CompletedProfile(i, Settings[i]), () => MeasureProfileAsync(index),
                            Inputs.Inputs[i].Mesh.GetTriangleCount(), (int)Settings[i].Target.Value, output.Mesh.GetTriangleCount()) is { } measurement)
                            measurements.Add(measurement);
                    }
                PreviewActivity.Finish(renderers, request);
            }
            private async Task<FaQemCountProfile> MeasureProfileAsync(int index)
            {
                if (disposed) throw new ObjectDisposedException(nameof(Node));
                // A full-detail mesh does not need a reduction sequence until its
                // slider is lowered. Retain the prepared source while that job runs,
                // even if its preview node is replaced or the inspector is closed.
                var inputs = Inputs.Retain();
                try { return (await inputs.GetReplay(index, Settings[index])).Counts; }
                finally { inputs.Dispose(); }
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                foreach (var measurement in measurements) measurement.Dispose();
                foreach (var output in Outputs.Values) output.Dispose();
                Inputs.Dispose();
                PreviewActivity.Finish(renderers, request);
            }
        }
    }
}
