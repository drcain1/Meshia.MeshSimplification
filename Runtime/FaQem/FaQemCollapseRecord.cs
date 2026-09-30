using System;
using Unity.Mathematics;

namespace Meshia.MeshSimplification
{
    [Serializable]
    public struct FaQemCollapseRecord
    {
        public int RemovedVertex;
        public int SurvivorVertex;
        public float3 RemovedPosition;
        public float3 SurvivorPositionBefore;
        public float3 SurvivorPositionAfter;
        public int IncidentFaceStart;
        public int IncidentFaceCount;
    }

    [Serializable]
    public struct FaQemAffectedFace
    {
        public int OriginalTriangleIndex;
        public int SubMeshIndex;
        public int3 VerticesBefore;
    }
}
