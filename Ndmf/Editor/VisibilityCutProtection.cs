#nullable enable
using System;
using System.Linq;
using nadena.dev.ndmf.preview;
using UnityEngine;

namespace Meshia.MeshSimplification.Ndmf.Editor
{
    internal static class VisibilityCutProtection
    {
        internal static MeshSimplifierOptions Resolve(Renderer renderer, MeshSimplifierOptions options,
            ComputeContext? context = null)
        {
            options.SkinningProtection.VisibilityBoneIndices.Clear();
            options.SkinningProtection.PreserveAllBoneMembership = false;
            if (renderer is not SkinnedMeshRenderer skinned) return options;
            var bones = skinned.bones;
            context?.Observe(skinned, r => r.bones, (a, b) => a.SequenceEqual(b));
            foreach (var bone in bones)
            {
                if (bone == null) continue;
                context?.Observe(bone);
                if (bone.parent != null) context?.Observe(bone.parent);
            }
            for (var i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                // MA 1.18's generated visibility bones use this pair of names.
                // Require both to avoid treating ordinary rig bones as cut controls.
                // No MA internals are called or modified; stock-version tests cover it.
                if (bone == null || bone.parent == null ||
                    !bone.name.StartsWith("NaNimatedBone for ", StringComparison.Ordinal) ||
                    !bone.parent.name.StartsWith("NaNimatedBuffer$", StringComparison.Ordinal)) continue;
                if (options.SkinningProtection.VisibilityBoneIndices.Length ==
                    options.SkinningProtection.VisibilityBoneIndices.Capacity)
                {
                    options.SkinningProtection.VisibilityBoneIndices.Clear();
                    options.SkinningProtection.PreserveAllBoneMembership = true;
                    break;
                }
                options.SkinningProtection.VisibilityBoneIndices.Add(i);
            }
            return options;
        }
    }
}
