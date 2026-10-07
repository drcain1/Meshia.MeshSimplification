#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using nadena.dev.ndmf.preview;
using Unity.Mathematics;
using UnityEngine;

namespace Meshia.MeshSimplification.Ndmf.Editor
{
    // Resolve only the palette here. Connectivity and dominant weights are checked
    // once by FA-QEM when preparing a reduction sequence, not on every slider edit.
    internal static class AutomaticJointProtection
    {
#if ENABLE_MODULAR_AVATAR
        private sealed class Mapping
        {
            internal readonly ComputeContext Context = new("Meshia clothing joint mapping");
            internal readonly Dictionary<Transform, Transform> Bones = new();
        }
        private static readonly ConditionalWeakTable<GameObject, Mapping> Mappings = new();

        private static Transform[] MapClothingBones(GameObject root, Transform[] bones, ComputeContext? context)
        {
            // A build uses its current transient hierarchy directly. Only preview/UI
            // calls cache mappings, with dependencies on MA settings and both rigs.
            Mapping mapping;
            if (context != null && Mappings.TryGetValue(root, out var cached) && !cached.Context.IsInvalidated)
                mapping = cached;
            else
            {
                if (context != null) Mappings.Remove(root);
                mapping = new Mapping();
                var observed = new HashSet<Transform>();
                var merges = mapping.Context.GetComponentsInChildren<nadena.dev.modular_avatar.core.ModularAvatarMergeArmature>(root, true);
                foreach (var merge in merges)
                {
                    mapping.Context.Observe(merge);
                    if (!merge.enabled) continue;
                    var target = merge.mergeTargetObject;
                    if (target == null || !target.transform.IsChildOf(root.transform)) continue;
                    foreach (var rig in new[] { merge.gameObject, target })
                        foreach (var bone in mapping.Context.GetComponentsInChildren<Transform>(rig, true))
                            if (observed.Add(bone)) mapping.Context.Observe(bone, t => t.name);
                    mapping.Bones[merge.transform] = target.transform;
                    var pairs = merge.GetBonesMapping();
                    if (pairs == null) continue;
                    foreach (var pair in pairs) mapping.Bones[pair.Item2] = pair.Item1;
                }
                if (context != null) Mappings.Add(root, mapping);
            }
            if (context != null) mapping.Context.Invalidates(context);
            var mapped = (Transform[])bones.Clone();
            for (var i = 0; i < mapped.Length; i++)
            {
                var remaining = mapping.Bones.Count + 1;
                while (mapped[i] != null && mapping.Bones.TryGetValue(mapped[i], out var next))
                {
                    if (--remaining == 0) { mapped[i] = null!; break; }
                    mapped[i] = next;
                }
            }
            if (context == null) mapping.Context.Invalidate();
            return mapped;
        }
#endif
        private static readonly (HumanBodyBones upper, HumanBodyBones lower)[] Joints =
        {
            (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg),
            (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg),
            (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm),
            (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm),
        };

        internal static MeshSimplifierOptions Resolve(GameObject root, Renderer renderer,
            MeshSimplifierOptions options, ComputeContext? context = null)
        {
            options.SkinningProtection.AutomaticJointBonePairs.Clear();
            if (options.AllowUnsafeGeometry || options.SkinningProtection.Policy != SkinningProtectionPolicy.AutoDeforming ||
                renderer is not SkinnedMeshRenderer skin || root == null) return options;
            var animator = context != null ? context.GetComponent<Animator>(root) : root.GetComponent<Animator>();
            if (animator == null) return options;
            context?.Observe(animator);
            if (animator.avatar != null) context?.Observe(animator.avatar);
            if (!animator.isHuman) return options;
            var bones = skin.bones;
#if ENABLE_MODULAR_AVATAR
            bones = MapClothingBones(root, bones, context);
#endif
            foreach (var joint in Joints)
            {
                var upper = animator.GetBoneTransform(joint.upper);
                var lower = animator.GetBoneTransform(joint.lower);
                if (upper == null || lower == null || upper == lower) continue;
                AddPair(ref options.SkinningProtection, bones, upper, lower);
            }
            return options;
        }

        internal static void AddPair(ref SkinningProtectionOptions protection, Transform[] bones, Transform upper, Transform lower)
        {
            // Keep duplicate palette slots: the mesh may use any of them.
            for (var a = 0; a < bones.Length; a++)
                if (bones[a] == upper)
                    for (var b = 0; b < bones.Length; b++)
                        if (bones[b] == lower)
                        {
                            if (protection.AutomaticJointBonePairs.Length == protection.AutomaticJointBonePairs.Capacity)
                                throw new InvalidOperationException("Too many duplicate bone slots for automatic joint protection.");
                            protection.AutomaticJointBonePairs.Add(new int2(a, b));
                        }
        }
    }
}
