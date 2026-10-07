#nullable enable
#if ENABLE_MODULAR_AVATAR

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.preview;
using UnityEngine;

namespace Meshia.MeshSimplification.Ndmf.Editor.Preview
{
    internal class MeshiaCascadingAvatarMeshSimplifierPreview : MeshiaMeshSimplifierPreviewBase<MeshiaCascadingAvatarMeshSimplifierPreview>
    {
        private static readonly Dictionary<(MeshiaCascadingAvatarMeshSimplifier, int), SliderMeasurement> SliderMeasurements = new();

        // Managed count curves only: no preview mesh or native allocation escapes its node.
        private sealed class SliderMeasurement : IDisposable
        {
            internal readonly MeasuredMeshResponse Response;
            internal readonly Func<bool> IsCurrent;
            private readonly (MeshiaCascadingAvatarMeshSimplifier, int) key;
            internal SliderMeasurement((MeshiaCascadingAvatarMeshSimplifier, int) key, MeasuredMeshResponse response, Func<bool> isCurrent)
            { this.key = key; Response = response; IsCurrent = isCurrent; }
            public void Dispose()
            {
                if (SliderMeasurements.TryGetValue(key, out var current) && current == this) SliderMeasurements.Remove(key);
                Response.Dispose();
            }
        }

        internal static bool TryGetSliderMeasurement(MeshiaCascadingAvatarMeshSimplifier component, int index,
            out MeasuredMeshResponse response)
        {
            response = null!;
            if (!IsEnabled() || !SliderMeasurements.TryGetValue((component, index), out var measurement) || !measurement.IsCurrent()) return false;
            response = measurement.Response;
            return true;
        }

        protected override SliderMeasurementPublisher? CaptureMeasurementPublisher(
            ComputeContext context, RenderGroup group, Renderer original)
        {
            var (component, index) = group.GetData<(MeshiaCascadingAvatarMeshSimplifier, int)>();
            if (index < 0) (component, index) = FindGroupedTarget(context, original);
            if (component == null || index < 0 || component.Entries[index].Algorithm != MeshiaCascadingSimplificationAlgorithm.FaQem) return null;
            var snapshot = component.Entries[index] with { TargetTriangleCount = 0, CutBudgetVersion = 0, Fixed = false };
            bool Current() => component != null && original != null && !context.IsInvalidated &&
                component.gameObject.activeInHierarchy && index < component.Entries.Count &&
                component.Entries[index].GetTargetRenderer(component) == original &&
                snapshot.Equals(component.Entries[index] with { TargetTriangleCount = 0, CutBudgetVersion = 0, Fixed = false });
            return (profile, measureAsync, source, requested, produced) =>
            {
                if (!Current()) return null;
                int Evaluate(int count) => profile != null && profile.TryGetOutput(count, out var output) ? output
                    : throw new InvalidOperationException("Preview counts must be measured asynchronously.");
                var response = new MeasuredMeshResponse(index, source, requested, produced, Evaluate,
                    countProfile: profile, measureProfileAsync: measureAsync);
                var measurement = new SliderMeasurement((component, index), response, Current);
                SliderMeasurements[(component, index)] = measurement;
                return measurement;
            };
        }

        internal static RenderGroup CreateRenderGroup(Renderer renderer,
            MeshiaCascadingAvatarMeshSimplifier component, int index)
        {
#if ENABLE_NDMF_EXPLICIT_RENDER_GROUP_EQUALITY
            // Match the legacy tuple equality: component identity plus entry index.
            return RenderGroup.For(renderer).WithData((component, index),
                EqualityComparer<(MeshiaCascadingAvatarMeshSimplifier, int)>.Default);
#else
            // NDMF before 1.13 does not expose the explicit-comparer overload.
            return RenderGroup.For(renderer).WithData((component, index));
#endif
        }

        public override ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            var groups = new List<RenderGroup>();
            foreach (var root in context.GetAvatarRoots())
            {
                if (context.ActiveInHierarchy(root) is false) continue;
                var components = context.GetComponentsInChildren<MeshiaCascadingAvatarMeshSimplifier>(root, true)
                    .Where(c => context.Observe(c.gameObject, g => g.activeInHierarchy)).ToArray();
                var renderers = context.GetComponentsInChildren<Renderer>(root, true)
                    .Where(r => r is MeshRenderer or SkinnedMeshRenderer && RendererUtility.GetMesh(r) != null).ToArray();
                var hasCuts = false;
                foreach (var renderer in renderers)
                {
                    var mesh = RendererUtility.GetRequiredMesh(renderer);
                    context.Observe(renderer); context.Observe(mesh);
                    using var prepared = CutMeshPreparation.Prepare(renderer, mesh,
                        new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = mesh.GetTriangleCount() },
                        MeshSimplifierOptions.Default, context);
                    hasCuts |= prepared.Changed;
                }
                if (hasCuts && components.Length > 0)
                {
#if ENABLE_NDMF_EXPLICIT_RENDER_GROUP_EQUALITY
                    groups.Add(RenderGroup.For(renderers).WithData((components[0], -1),
                        EqualityComparer<(MeshiaCascadingAvatarMeshSimplifier, int)>.Default));
#else
                    groups.Add(RenderGroup.For(renderers).WithData((components[0], -1)));
#endif
                    continue;
                }
                foreach (var component in context.GetComponentsInChildren<MeshiaCascadingAvatarMeshSimplifier>(root, true))
                {
                    var componentEnabled = context.Observe(component.gameObject, g => g.activeInHierarchy);
                    if (!componentEnabled) continue;

                    var targetCount = context.Observe(component, c => c.Entries.Count());
                    for (int i = 0; i < targetCount; i++)
                    {
                        var index = i;
                        var targetEnabled = context.Observe(component, c => c.Entries[index].IsValid(c) && c.Entries[index].Enabled);
                        if (!targetEnabled) continue;

                        var renderer = component.Entries[index].GetTargetRenderer(component)!;
                        groups.Add(CreateRenderGroup(renderer, component, index));
                    }
                }
            }
            return groups.ToImmutableList();
        }

        private static (MeshiaCascadingAvatarMeshSimplifier?, int) FindGroupedTarget(ComputeContext context, Renderer renderer)
        {
            var root = context.GetAvatarRoot(renderer.gameObject);
            foreach (var component in context.GetComponentsInChildren<MeshiaCascadingAvatarMeshSimplifier>(root, true))
            {
                if (!context.Observe(component.gameObject, g => g.activeInHierarchy)) continue;
                context.Observe(component);
                var index = component.Entries.FindIndex(e => e.Enabled && e.IsValid(component) && e.GetTargetRenderer(component) == renderer);
                if (index >= 0) return (component, index);
            }
            return (null, -1);
        }

        protected override bool IncludesRenderer(ComputeContext context, RenderGroup group, Renderer original)
            => group.GetData<(MeshiaCascadingAvatarMeshSimplifier, int)>().Item2 >= 0 || FindGroupedTarget(context, original).Item2 >= 0;
        
        protected override (MeshSimplificationTarget, MeshSimplifierOptions, BitArray?) QueryTarget(ComputeContext context, RenderGroup group, Renderer original, Renderer proxy)
        {
            var data = group.GetData<(MeshiaCascadingAvatarMeshSimplifier, int)>();
            var component = data.Item1;
            var index = data.Item2;
            if (index < 0)
            {
                var resolved = FindGroupedTarget(context, original);
                component = resolved.Item1!; index = resolved.Item2;
            }

            var cascadingTarget = context.Observe(component, c => c.Entries[index] with { }, (a, b) => a.Equals(b));
            var proxyMesh = RendererUtility.GetRequiredMesh(proxy);
            var target = cascadingTarget.CreateTarget(proxyMesh.GetTriangleCount());

            var avatarRoot = context.GetAvatarRoot(original.gameObject);
            var preserveBorderEdgeBoneIndices = MeshiaCascadingAvatarMeshSimplifier.GetPreserveBorderEdgesBoneIndices(avatarRoot, component, cascadingTarget);
            if (original is SkinnedMeshRenderer skinned)
                context.Observe(skinned, r => r.bones, (a, b) => a.SequenceEqual(b));
            var animator = context.GetComponent<Animator>(avatarRoot);
            if (animator != null)
            {
                context.Observe(animator);
                if (animator.avatar != null) context.Observe(animator.avatar);
            }
            return (target, MeshiaCascadingAvatarMeshSimplifier.GetJointProtectionOptions(avatarRoot, component, cascadingTarget), preserveBorderEdgeBoneIndices);
        }

        
    }
}

#endif
