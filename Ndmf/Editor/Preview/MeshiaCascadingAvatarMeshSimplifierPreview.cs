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
        
        protected override (MeshSimplificationTarget, MeshSimplifierOptions, BitArray?) QueryTarget(ComputeContext context, RenderGroup group, Renderer original, Renderer proxy)
        {
            var data = group.GetData<(MeshiaCascadingAvatarMeshSimplifier, int)>();
            var component = data.Item1;
            var index = data.Item2;

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
