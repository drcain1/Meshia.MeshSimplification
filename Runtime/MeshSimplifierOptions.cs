#nullable enable
using System;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.CompilerServices;
namespace Meshia.MeshSimplification
{
    [Serializable]
    public struct MeshSimplifierOptions : IEquatable<MeshSimplifierOptions>
    {
        public static MeshSimplifierOptions Default => new()
        {
            PreserveBorderEdges = true,
            PreserveSurfaceCurvature = false,
            UseBarycentricCoordinateInterpolation = false,
            MinNormalDot = 0.2f,
            EnableSmartLink = true,
            VertexLinkDistance = 0.0001f,
            VertexLinkMinNormalDot = 0.95f,
            VertexLinkColorDistance = 0.01f,
            VertexLinkUvDistance = 0.001f,
            SkinningProtection = SkinningProtectionOptions.Default,
            FaQem = FaQemOptions.Default,
        };

        /// <summary>Conservative avatar preset, applied explicitly. Saved options and core defaults are unchanged.</summary>
        public static MeshSimplifierOptions ConservativeAvatar
        {
            get
            {
                var options = Default;
                options.SkinningProtection.Policy = SkinningProtectionPolicy.AutoDeforming;
                options.SkinningProtection.PreserveJointTransitions = true;
                options.SkinningProtection.Strength = 2f;
                options.SkinningProtection.MaxWeightDistance = .1f;
                options.SkinningProtection.MaxDiscardedWeight = .02f;
                options.FaQem.MaxSurfaceDeviation = .0005f;
                return options;
            }
        }

        /// <summary>Explicit high-reduction preset with relaxed bone-weight and surface limits, retaining joint guards.</summary>
        public static MeshSimplifierOptions ExtremeAvatar
        {
            get
            {
                var options = ConservativeAvatar;
                options.SkinningProtection.Strength = .25f;
                options.SkinningProtection.MaxWeightDistance = .25f;
                options.SkinningProtection.MaxDiscardedWeight = .1f;
                options.FaQem.MaxSurfaceDeviation = .001f;
                // Allow more movement away from joints; keep their support vertices and seam guards.
                return options;
            }
        }

        /// <summary>New avatar settings: geometry guards on, deformation protection opt-in.</summary>
        public static MeshSimplifierOptions AvatarInitial
        {
            get
            {
                var options = ConservativeAvatar;
                options.SkinningProtection.Policy = SkinningProtectionPolicy.Off;
                options.SkinningProtection.Enabled = false;
                options.SkinningProtection.PreserveJointTransitions = false;
                return options;
            }
        }

        /// <summary>
        /// If you want to suppress hole generation during simplification, enable this option.
        /// </summary>
        [Tooltip("If you want to suppress hole generation during simplification, enable this option.")]
        public bool PreserveBorderEdges;

        /// <summary>Bypasses shape and attribute guards while retaining structural validity checks.</summary>
        [HideInInspector] public bool AllowUnsafeGeometry;

        /// <summary>Returns an unprotected copy without overwriting saved protection settings.</summary>
        public readonly MeshSimplifierOptions WithoutProtections()
        {
            var options = this;
            options.AllowUnsafeGeometry = true;
            options.PreserveBorderEdges = false;
            options.PreserveSurfaceCurvature = false;
            options.MinNormalDot = -1f;
            options.SkinningProtection.Policy = SkinningProtectionPolicy.Off;
            options.SkinningProtection.Enabled = false;
            options.SkinningProtection.PreserveJointTransitions = false;
            options.SkinningProtection.JointProtectionBoneIndices.Clear();
            options.FaQem = options.FaQem.Effective;
            options.FaQem.PreserveAttributeSeams = false;
            options.FaQem.MaxSurfaceDeviation = 0f;
            options.FaQem.ExperimentalUvEnabled = false;
            options.FaQem.MinNormalDot = 0f;
            return options;
        }

        public bool PreserveSurfaceCurvature;
        /// <summary>
        /// If you find that the texture is distorted, try toggling this option.
        /// </summary>
        [Tooltip("If you find that the texture is distorted, try toggling this option.")]
        public bool UseBarycentricCoordinateInterpolation;
        /// <summary>
        /// If this option is enabled, vertices that are not originally connected but are close to each other will be included in the first merge candidates. <br/>
        /// Increases the initialization cost.
        /// </summary>
        [Tooltip("If this option is enabled, vertices that are not originally connected but are close to each other will be included in the first merge candidates. \n" +
            "Increases the initialization cost.")]
        public bool EnableSmartLink;
        [Range(-1, 1)]
        public float MinNormalDot;
        /// <summary>
        /// When smart link is enabled, this is used to select candidates for merging vertices that are not originally connected to each other. <br/>
        /// Increasing this value also increases the initialization cost.
        /// </summary>
        [Tooltip("When smart link is enabled, this is used to select candidates for merging vertices that are not originally connected to each other. \n" +
            "Increasing this value also increases the initialization cost.")]
        public float VertexLinkDistance;
        [Range(-1, 1)]
        public float VertexLinkMinNormalDot;
        // This could be HDR color, so there is no Range.
        public float VertexLinkColorDistance;
        [Range(0, 1.41421356237f)]
        public float VertexLinkUvDistance;
        /// <summary>Optional experimental protection for skinned joint deformation.</summary>
        public SkinningProtectionOptions SkinningProtection;

        /// <summary>Settings used by the geometry-only FA-QEM target.</summary>
        public FaQemOptions FaQem;


        public readonly override bool Equals(object obj)
        {
            return obj is MeshSimplifierOptions options && Equals(options);
        }

        public readonly bool Equals(MeshSimplifierOptions other)
        {
            return AllowUnsafeGeometry == other.AllowUnsafeGeometry &&
                   PreserveBorderEdges == other.PreserveBorderEdges &&
                   PreserveSurfaceCurvature == other.PreserveSurfaceCurvature &&
                   UseBarycentricCoordinateInterpolation == other.UseBarycentricCoordinateInterpolation &&
                   EnableSmartLink == other.EnableSmartLink &&
                   MinNormalDot == other.MinNormalDot &&
                   VertexLinkDistance == other.VertexLinkDistance &&
                   VertexLinkMinNormalDot == other.VertexLinkMinNormalDot &&
                   VertexLinkColorDistance == other.VertexLinkColorDistance &&
                   VertexLinkUvDistance == other.VertexLinkUvDistance &&
                   SkinningProtection.Equals(other.SkinningProtection) &&
                   FaQem.Equals(other.FaQem);
        }

        public readonly override int GetHashCode()
        {
            return HashCode.Combine(AllowUnsafeGeometry, PreserveBorderEdges, PreserveSurfaceCurvature, MinNormalDot, SkinningProtection, FaQem);
        }

        public static bool operator ==(MeshSimplifierOptions left, MeshSimplifierOptions right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(MeshSimplifierOptions left, MeshSimplifierOptions right)
        {
            return !(left == right);
        }
    }
}
