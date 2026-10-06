#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.preview;
using UnityEngine;

namespace Meshia.MeshSimplification.Ndmf.Editor.Preview
{
    internal class MeshiaMeshSimplifierPreview : MeshiaMeshSimplifierPreviewBase<MeshiaMeshSimplifierPreview>
    {
        public override ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            var targets = context.GetComponentsByType<MeshiaMeshSimplifier>()
            .Where(meshiaMeshSimplifier => context.ActiveAndEnabled(meshiaMeshSimplifier))
            .Select(meshiaMeshSimplifier => context.GetComponent<Renderer>(meshiaMeshSimplifier.gameObject))
            .Where(renderer => renderer is MeshRenderer or SkinnedMeshRenderer)
            .ToArray();
            var groups = new List<RenderGroup>();
            foreach (var byRoot in targets.GroupBy(r => context.GetAvatarRoot(r.gameObject)))
            {
                if (byRoot.Key == null) { groups.AddRange(byRoot.Select(RenderGroup.For)); continue; }
                var renderers = context.GetComponentsInChildren<Renderer>(byRoot.Key, true)
                    .Where(r => r is MeshRenderer or SkinnedMeshRenderer && RendererUtility.GetMesh(r) != null).ToArray();
                var hasCuts = false;
                foreach (var renderer in renderers)
                {
                    var mesh = RendererUtility.GetRequiredMesh(renderer);
                    context.Observe(renderer); context.Observe(mesh);
                    using var prepared = CutMeshPreparation.Prepare(renderer, mesh,
                        new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = mesh.GetTriangleCount() }, MeshSimplifierOptions.Default, context);
                    hasCuts |= prepared.Changed;
                }
                if (hasCuts) groups.Add(RenderGroup.For(renderers));
                else groups.AddRange(byRoot.Select(RenderGroup.For));
            }
            return groups.ToImmutableList();
        }
        protected override bool IncludesRenderer(ComputeContext context, RenderGroup group, Renderer original)
        {
            var component = context.GetComponent<MeshiaMeshSimplifier>(original.gameObject);
            return component != null && context.ActiveAndEnabled(component);
        }
        protected override (MeshSimplificationTarget, MeshSimplifierOptions, BitArray?) QueryTarget(ComputeContext context, RenderGroup group, Renderer original, Renderer proxy)
        {
            var ndmfMeshSimplifier = original.GetComponent<MeshiaMeshSimplifier>();
            var target = context.Observe(ndmfMeshSimplifier, ndmfMeshSimplifier => ndmfMeshSimplifier.target, (x, y) => x == y);
            var options = context.Observe(ndmfMeshSimplifier, ndmfMeshSimplifier => ndmfMeshSimplifier.options, (x, y) => x == y);
            return (target, options, null);
        }
    }
}
