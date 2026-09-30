#nullable enable
using System;
using UnityEngine;

namespace Meshia.MeshSimplification
{
    /// <summary>Diagnostic record for an accepted Blender collapse whose lineage intersects selected input vertices.</summary>
    [Serializable]
    public struct BlenderCollapseTraceRecord
    {
        public int acceptedSequence;
        public int vertexA;
        public int vertexB;
        public ulong vertexALineage;
        public ulong vertexBLineage;
        public Vector3 position;
        public float lerpFactor;
        public float queuedTotalCost;
        public float acceptanceGeometricCost;
        /// <summary>Whether the acceptance-time geometric recomputation used the topology fallback.</summary>
        public bool usedTopologyFallback;
        public float weightDistance;
        public float discardedWeight;
        public bool skinningMetricsAvailable;
        public float skinningPenalty;
        public float skinningCost;
        public float acceptanceTotalCost;
        public float queuedMinusAcceptanceCost;
    }

    /// <summary>
    /// Accepted-collapse diagnostics. Costs other than <see cref="BlenderCollapseTraceRecord.queuedTotalCost"/>
    /// are recomputed immediately before acceptance. The queued-position float precision and intervening
    /// topology updates can contribute to their difference. This trace does not contain rejected candidates.
    /// </summary>
    [Serializable]
    public sealed class BlenderCollapseTrace
    {
        public int[] sourceVertexIds = Array.Empty<int>();
        public BlenderCollapseTraceRecord[] records = Array.Empty<BlenderCollapseTraceRecord>();
        public bool truncated;
    }
}
