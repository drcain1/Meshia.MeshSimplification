#nullable enable
using System;
using UnityEngine;

namespace Meshia.MeshSimplification
{
    /// <summary>Controls the feature-aware quadric metric and its attribute constraints.</summary>
    [Serializable]
    public struct FaQemOptions : IEquatable<FaQemOptions>
    {
        [SerializeField, HideInInspector] int version;

        /// <summary>Returns the initial feature-aware settings.</summary>
        public static FaQemOptions Default => new()
        {
            version = 1,
            PlaneAreaWeight = 1f,
            BoundaryWeight = 500f,
            NormalWeight = 0.01f,
            AreaWeight = 100f,
            UseInverseAreaWeighting = true,
            PreserveAttributeSeams = true,
            MinNormalDot = 0.2f,
        };

        /// <summary>Positive divisor applied to source triangle areas.</summary>
        [Min(0.000001f)] public float PlaneAreaWeight;
        /// <summary>Weight of the boundary curvature constraints.</summary>
        [Min(0)] public float BoundaryWeight;
        /// <summary>Weight of original-normal tangent planes.</summary>
        [Min(0)] public float NormalWeight;
        /// <summary>Weight of the boundary swept-area ranking penalty.</summary>
        [Min(0)] public float AreaWeight;
        /// <summary>Enables inverse triangle-area weighting of source planes.</summary>
        public bool UseInverseAreaWeighting;
        /// <summary>Protects coincident vertex records from separating, including records with matching attributes.</summary>
        public bool PreserveAttributeSeams;
        /// <summary>Minimum dot product of a surviving face's normals before and after a collapse.</summary>
        [Range(0, 1)] public float MinNormalDot;
        /// <summary>Maximum sampled distance from the original surface, as a fraction of the mesh bounds diagonal. Zero disables this guard.</summary>
        [Range(0, 0.1f)] public float MaxSurfaceDeviation;

        /// <summary>Resolves settings absent in older serialized components to the defaults.</summary>
        public readonly FaQemOptions Effective => version == 0 ? Default : this;

        /// <summary>Validates the resolved settings without altering intentional zero weights.</summary>
        public readonly void Validate()
        {
            var value = Effective;
            if (!Finite(value.PlaneAreaWeight) || value.PlaneAreaWeight <= 0 ||
                !Nonnegative(value.BoundaryWeight) || !Nonnegative(value.NormalWeight) ||
                !Nonnegative(value.AreaWeight) || !Finite(value.MinNormalDot) ||
                value.MinNormalDot < 0 || value.MinNormalDot > 1 ||
                !Nonnegative(value.MaxSurfaceDeviation) || value.MaxSurfaceDeviation > 0.1f)
            {
                throw new ArgumentOutOfRangeException(nameof(FaQemOptions), "FA-QEM requires finite nonnegative weights, a positive area divisor, a normal threshold between zero and one, and surface deviation between zero and 0.1.");
            }
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Nonnegative(float value) => Finite(value) && value >= 0;

        /// <inheritdoc/>
        public readonly bool Equals(FaQemOptions other)
        {
            var a = Effective;
            var b = other.Effective;
            return a.PlaneAreaWeight == b.PlaneAreaWeight && a.BoundaryWeight == b.BoundaryWeight &&
                   a.NormalWeight == b.NormalWeight && a.AreaWeight == b.AreaWeight &&
                   a.UseInverseAreaWeighting == b.UseInverseAreaWeighting &&
                   a.PreserveAttributeSeams == b.PreserveAttributeSeams && a.MinNormalDot == b.MinNormalDot &&
                   a.MaxSurfaceDeviation == b.MaxSurfaceDeviation;
        }

        /// <inheritdoc/>
        public override readonly bool Equals(object? obj) => obj is FaQemOptions other && Equals(other);
        /// <inheritdoc/>
        public override readonly int GetHashCode()
        {
            var a = Effective;
            return HashCode.Combine(HashCode.Combine(a.PlaneAreaWeight, a.BoundaryWeight, a.NormalWeight, a.AreaWeight),
                a.UseInverseAreaWeighting, a.PreserveAttributeSeams, a.MinNormalDot, a.MaxSurfaceDeviation);
        }
    }
}
