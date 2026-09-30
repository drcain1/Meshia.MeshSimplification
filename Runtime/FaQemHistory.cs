#nullable enable
using System;

namespace Meshia.MeshSimplification
{
    /// <summary>Accepted collapses and stable output identifiers for successive surface correspondence.</summary>
    public sealed class FaQemHistory
    {
        /// <summary>The accepted collapses in chronological order.</summary>
        public FaQemCollapseRecord[] Records { get; }
        /// <summary>Incident faces immediately before each recorded collapse.</summary>
        public FaQemAffectedFace[] AffectedFaces { get; }
        /// <summary>Maps compact output vertices to stable input buffer identifiers.</summary>
        public int[] OutputVertexToSourceVertex { get; }
        /// <summary>Maps compact output triangles to their stable source triangle slots.</summary>
        public int[] OutputTriangleToSourceTriangle { get; }

        internal FaQemHistory(FaQemCollapseRecord[] records, FaQemAffectedFace[] affectedFaces,
            int[] outputVertexToSourceVertex, int[] outputTriangleToSourceTriangle)
        {
            Records = records ?? throw new ArgumentNullException(nameof(records));
            AffectedFaces = affectedFaces ?? throw new ArgumentNullException(nameof(affectedFaces));
            OutputVertexToSourceVertex = outputVertexToSourceVertex ?? throw new ArgumentNullException(nameof(outputVertexToSourceVertex));
            OutputTriangleToSourceTriangle = outputTriangleToSourceTriangle ?? throw new ArgumentNullException(nameof(outputTriangleToSourceTriangle));
        }
    }
}
