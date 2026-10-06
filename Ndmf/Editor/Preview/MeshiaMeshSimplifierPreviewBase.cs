#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using nadena.dev.ndmf.preview;
using System.Collections;

namespace Meshia.MeshSimplification.Ndmf.Editor.Preview
{
    internal abstract class MeshiaMeshSimplifierPreviewBase<TDerived> : IRenderFilter
        where TDerived : MeshiaMeshSimplifierPreviewBase<TDerived>
    {
        public static readonly Dictionary<Renderer, (int proxy, int simplified)> TriangleCountCache = new();

        public static TogglablePreviewNode PreviewControlNode { get; } = TogglablePreviewNode.Create(
                () => typeof(TDerived).Name,
                qualifiedName: typeof(TDerived).FullName
            );

        static TogglablePreviewNode[] PreviewControlNodes { get; } = { PreviewControlNode };

        public IEnumerable<TogglablePreviewNode> GetPreviewControlNodes() => PreviewControlNodes;

        public bool IsEnabled(ComputeContext context)
        {
            return context.Observe(PreviewControlNode.IsEnabled);
        }

        public static bool IsEnabled() => PreviewControlNode.IsEnabled.Value;

        public abstract ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context);

        async Task<IRenderFilterNode> IRenderFilter.Instantiate(RenderGroup group, IEnumerable<(Renderer, Renderer)> proxyPairs, ComputeContext context)
        {
            var pairs = proxyPairs.ToArray();
            var inputs = new List<CutMeshPreparation>();
            var originals = new List<Mesh>();
            var targets = new List<(MeshSimplificationTarget target, MeshSimplifierOptions options, BitArray? bones, bool selected)>();
            var outputs = new Dictionary<Renderer, Mesh>();
            try
            {
                foreach (var (original, proxy) in pairs)
                {
                    var mesh = RendererUtility.GetRequiredMesh(proxy); originals.Add(mesh);
                    var selected = IncludesRenderer(context, group, original);
                    var (target, options, bones) = selected ? QueryTarget(context, group, original, proxy)
                        : (new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = mesh.GetTriangleCount() }, MeshSimplifierOptions.Default, (BitArray?)null);
                    options = NdmfPlugin.ResolvePreviewOptions(context, context.GetAvatarRoot(original.gameObject), original, options);
                    options = VisibilityCutProtection.Resolve(proxy, options, context);
                    targets.Add((target, options, bones, selected));
                    inputs.Add(CutMeshPreparation.Prepare(original, mesh, target, options, context));
                    context.Observe(original);
                    context.Observe(original.transform);
                    // BakeMesh depends on the proxy's current rig and blendshapes.
                    if (proxy is SkinnedMeshRenderer skin)
                        foreach (var bone in skin.bones) if (bone != null) context.Observe(bone);
                }
                var masks = CutOverlapProtection.Calculate(pairs.Select(p => p.Item2).ToArray(),
                    inputs.Select(p => p.Mesh).ToArray(), originals);
                for (var i = 0; i < pairs.Length; i++)
                {
                    var (target, options, bones, selected) = targets[i];
                    if (!selected) continue;
                    if (target.Kind == MeshSimplificationTargetKind.FaQemTriangleCount)
                        options = CutOverlapProtection.Apply(options, masks[i]);
                    var output = new Mesh(); outputs.Add(pairs[i].Item1, output);
                    await MeshSimplifier.SimplifyAsync(inputs[i].Mesh, target, options, bones, output);
                    TriangleCountCache[pairs[i].Item1] = (originals[i].GetTriangleCount(), output.GetTriangleCount());
                }
                return new NdmfMeshSimplifierPreviewGroupNode(outputs);
            }
            catch (Exception)
            {
                foreach (var mesh in outputs.Values) UnityEngine.Object.DestroyImmediate(mesh);
                throw;
            }
            finally { foreach (var input in inputs) input.Dispose(); }
        }

        protected virtual bool IncludesRenderer(ComputeContext context, RenderGroup group, Renderer original) => true;
        protected abstract (MeshSimplificationTarget, MeshSimplifierOptions, BitArray?) QueryTarget(ComputeContext context, RenderGroup group, Renderer original, Renderer proxy);
    }

    internal class NdmfMeshSimplifierPreviewNode : IRenderFilterNode
    {
        public RenderAspects WhatChanged => RenderAspects.Mesh;
        private readonly Mesh _simplifiedMesh;

        public NdmfMeshSimplifierPreviewNode(Mesh mesh)
        {
            _simplifiedMesh = mesh;
        }

        public void OnFrame(Renderer original, Renderer proxy)
        {
            RendererUtility.SetMesh(proxy, _simplifiedMesh);
        }

        void IDisposable.Dispose() => UnityEngine.Object.DestroyImmediate(_simplifiedMesh);
    }

    internal sealed class NdmfMeshSimplifierPreviewGroupNode : IRenderFilterNode
    {
        private readonly Dictionary<Renderer, Mesh> meshes;
        internal NdmfMeshSimplifierPreviewGroupNode(Dictionary<Renderer, Mesh> meshes) => this.meshes = meshes;
        public RenderAspects WhatChanged => RenderAspects.Mesh;
        public void OnFrame(Renderer original, Renderer proxy)
        {
            if (meshes.TryGetValue(original, out var mesh)) RendererUtility.SetMesh(proxy, mesh);
        }
        public void Dispose() { foreach (var mesh in meshes.Values) UnityEngine.Object.DestroyImmediate(mesh); }
    }
}
