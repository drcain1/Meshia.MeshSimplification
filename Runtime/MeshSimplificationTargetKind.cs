#nullable enable
using System;

namespace Meshia.MeshSimplification
{
    /// <summary>
    /// Represents what actually <see cref="MeshSimplificationTarget.Value"/> means.
    /// </summary>
    [Serializable]
    public enum MeshSimplificationTargetKind
    {
        /// <summary>
        /// The value is the target vertex count relative to the original mesh.
        /// </summary>
        RelativeVertexCount,
        /// <summary>
        /// The value is the target vertex count as an absolute number.
        /// </summary>
        AbsoluteVertexCount,
        /// <summary>
        /// The value is the max total error scaled by the original mesh's bounding box size and vertex count.
        /// </summary>
        ScaledTotalError,
        /// <summary>
        /// The value is the max total error as an absolute number.
        /// </summary>
        AbsoluteTotalError,
        /// <summary>
        /// The value is the target triangle count relative to the original mesh.
        /// </summary>
        RelativeTriangleCount,
        /// <summary>
        /// The value is the target triangle count as an absolute number.
        /// </summary>
        AbsoluteTriangleCount,
        /// <summary>
        /// The value is the target triangle-count ratio, using Blender 5.2's
        /// Mesh &gt; Clean Up &gt; Decimate Geometry edge-collapse policy.
        /// A value of 1 keeps the current triangle count and 0 decimates as far as topology allows.
        /// </summary>
        BlenderDecimateRatio,
        /// <summary>
        /// The value is an absolute target triangle count. Before falling back to the
        /// Blender-compatible collapse policy, the simplifier removes conservative,
        /// alternating edge-loop batches reconstructed from the triangulated mesh.
        /// </summary>
        UvLoopDissolveTriangleCount,
    }
}


