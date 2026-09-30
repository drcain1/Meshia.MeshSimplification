#nullable enable
using System;
using Unity.Collections;

namespace Meshia.MeshSimplification
{
    /// <summary>Optional constraints for preserving skinned joint deformation.</summary>
    [Serializable]
    public struct SkinningProtectionOptions : IEquatable<SkinningProtectionOptions>
    {
        public bool Enabled;
        /// <summary>Serialization-compatible policy. Zero is the pre-policy Legacy mode.</summary>
        public SkinningProtectionPolicy Policy;
        /// <summary>FA-QEM: retains source joint-transition rings and their immediate support vertices.</summary>
        public bool PreserveJointTransitions;
        /// <summary>Optional mesh bone indices for joint-transition protection. Empty means all bones. Resolved by NDMF from its humanoid selection; not serialized.</summary>
        [NonSerialized] public FixedList512Bytes<int> JointProtectionBoneIndices;
        public float Strength;
        public float MaxWeightDistance;
        public float MaxDiscardedWeight;

        public static SkinningProtectionOptions Default => new()
        {
            Enabled = false,
            Policy = SkinningProtectionPolicy.Legacy,
            Strength = 1f,
            MaxWeightDistance = .25f,
            MaxDiscardedWeight = .1f,
        };

        public readonly void Validate()
        {
            if (!Finite(Strength) || Strength < 0f || !Finite(MaxWeightDistance) || MaxWeightDistance < 0f || MaxWeightDistance > 1f ||
                !Finite(MaxDiscardedWeight) || MaxDiscardedWeight < 0f || MaxDiscardedWeight > 1f)
                throw new ArgumentOutOfRangeException(nameof(SkinningProtectionOptions), "Skinning protection values must be finite and within their supported ranges.");
        }

        /// <summary>Returns the build-resolved copy without changing serialized component settings.</summary>
        public readonly SkinningProtectionOptions Resolve(bool autoSelected)
        {
            var resolved = this;
            resolved.Enabled = Policy switch
            {
                SkinningProtectionPolicy.Legacy => Enabled,
                SkinningProtectionPolicy.Auto => autoSelected,
                SkinningProtectionPolicy.AutoDeforming => autoSelected,
                SkinningProtectionPolicy.On => true,
                SkinningProtectionPolicy.Off => false,
                _ => throw new ArgumentOutOfRangeException(nameof(Policy), Policy, "Unknown skinning protection policy."),
            };
            return resolved;
        }

        public readonly bool Equals(SkinningProtectionOptions other)
            => Enabled == other.Enabled && Policy == other.Policy && PreserveJointTransitions == other.PreserveJointTransitions &&
               JointProtectionBoneIndices.Equals(other.JointProtectionBoneIndices) && Strength == other.Strength &&
               MaxWeightDistance == other.MaxWeightDistance && MaxDiscardedWeight == other.MaxDiscardedWeight;
        public override readonly bool Equals(object? obj) => obj is SkinningProtectionOptions other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(Enabled, Policy, PreserveJointTransitions, JointProtectionBoneIndices, Strength, MaxWeightDistance, MaxDiscardedWeight);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
