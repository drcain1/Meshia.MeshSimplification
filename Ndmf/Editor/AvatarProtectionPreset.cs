#nullable enable
#if ENABLE_MODULAR_AVATAR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Meshia.MeshSimplification.Ndmf.Editor
{
    internal enum AvatarProtectionLevel { Conservative, Aggressive, Extreme }

    /// <summary>One-time preset selection. It does not change build-time policy resolution.</summary>
    internal static class AvatarProtectionPreset
    {
        internal static void Apply(MeshiaCascadingAvatarMeshSimplifier component, AvatarProtectionLevel level)
        {
            var root = component.transform.parent != null ? component.transform.parent.gameObject : component.gameObject;
            var animator = root.GetComponent<Animator>();
            var handBones = new HashSet<Transform>();
            var handNames = new HashSet<string>();
            if (animator != null && animator.isHuman)
            {
                for (var i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    if ((MeshiaCascadingAvatarMeshSimplifierRendererEntry.DefaultHandBones & (1ul << i)) == 0) continue;
                    var bone = animator.GetBoneTransform((HumanBodyBones)i);
                    if (bone == null) continue;
                    handBones.Add(bone);
                    // Clothing can still reference its own copy before Modular Avatar merges it.
                    handNames.Add(Normalize(bone.name));
                }
            }
            foreach (var entry in component.Entries)
            {
                var keepProtection = level == AvatarProtectionLevel.Conservative ||
                    NeedsProtection(entry.GetTargetRenderer(component), handBones, handNames);
                var options = level == AvatarProtectionLevel.Extreme
                    ? MeshSimplifierOptions.ExtremeAvatar : MeshSimplifierOptions.ConservativeAvatar;
                if (!keepProtection)
                {
                    options.SkinningProtection.Policy = SkinningProtectionPolicy.Off;
                    options.SkinningProtection.Enabled = false;
                    options.SkinningProtection.PreserveJointTransitions = false;
                }
                entry.DisableProtections = false;
                entry.Options = options;
                entry.PreserveJointTransitionsBones = level == AvatarProtectionLevel.Extreme
                    ? MeshiaCascadingAvatarMeshSimplifierRendererEntry.DefaultHandBones
                    : MeshiaCascadingAvatarMeshSimplifierRendererEntry.DefaultJointBones;
            }
        }

        internal static bool NeedsProtection(Renderer? renderer, HashSet<Transform> handBones, HashSet<string> handNames)
        {
            if (renderer == null) return true;
            var mesh = RendererUtility.GetMesh(renderer);
            if (mesh == null || !mesh.isReadable) return true;
            // Be conservative about faces even if the user explicitly enabled their simplification.
            if (string.Equals(renderer.name, "Body", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(renderer.name, "Face", StringComparison.OrdinalIgnoreCase) ||
                IsHair(renderer.name) || IsHair(mesh.name)) return true;
            if (renderer is not SkinnedMeshRenderer skin) return false;
            var bones = skin.bones;
            using var weights = mesh.GetAllBoneWeights();
            var used = new HashSet<int>();
            foreach (var weight in weights)
            {
                if (float.IsNaN(weight.weight) || float.IsInfinity(weight.weight)) return true;
                if (weight.weight <= 0) continue;
                if (weight.boneIndex < 0 || weight.boneIndex >= bones.Length || bones[weight.boneIndex] == null) return true;
                used.Add(weight.boneIndex);
            }
            foreach (var index in used)
            {
                var bone = bones[index];
                var name = Normalize(bone.name);
                if (handBones.Contains(bone) || handNames.Contains(name) || IsHand(name) || IsHair(name)) return true;
            }
            return false;
        }

        private static string Normalize(string name) => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        private static bool IsHair(string name)
        {
            name = Normalize(name);
            return name.Contains("hair") || name.Contains("bang") || name.Contains("ponytail") ||
                name.Contains("braid") || name.Contains("髪") || name.Contains("ヘア");
        }

        private static bool IsHand(string name)
            => name.Contains("hand") || name.Contains("wrist") || name.Contains("finger") ||
                name.Contains("thumb") || name.Contains("index") || name.Contains("middle") ||
                name.Contains("ring") || name.Contains("little") || name.Contains("pinky") ||
                name.Contains("手首") || name.Contains("指");
    }
}
#endif
