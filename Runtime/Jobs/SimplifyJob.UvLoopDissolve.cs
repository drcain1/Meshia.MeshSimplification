using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Meshia.MeshSimplification
{
    [BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.FastCompilation)]
    struct UvLoopDissolveJob : IJob
    {
        const float UvLoopMinNormalDot = 0.95f;
        const float UvLoopMinContinuationDot = 0.65f;

        [ReadOnly] public Mesh.MeshData Mesh;
        [ReadOnly] public NativeArray<float3> VertexPositionBuffer;
        [ReadOnly] public NativeArray<float4> VertexNormalBuffer;
        [ReadOnly] public NativeArray<float4> VertexTexCoord0Buffer;
        [ReadOnly] public NativeArray<float> VertexBlendWeightBuffer;
        [ReadOnly] public NativeArray<uint> VertexBlendIndicesBuffer;
        [ReadOnly] public NativeArray<int3> Triangles;
        [ReadOnly] public NativeArray<float3> TriangleNormals;
        [ReadOnly] public NativeParallelMultiHashMap<int, int> VertexContainingTriangles;
        [ReadOnly] public NativeParallelMultiHashMap<int, int> VertexMergeOpponentVertices;
        [ReadOnly] public NativeBitArray DiscardedTriangle;
        [ReadOnly] public NativeBitArray DiscardedVertex;
        public NativeArray<int> SourceToTarget;
        public NativeArray<int> Diagnostics;
        public int TargetTriangleCount;

        int TriangleCount;
        int VertexCount;

        struct UvQuad
        {
            public int4 Vertices;
            public float Score;
        }

        struct UvLoopChain
        {
            public int EdgeStart;
            public int EdgeCount;
            public bool IsProtected;
        }

        public void Execute()
        {
            if (Diagnostics[UvLoopDissolveDiagnostics.LoopPhaseStopped] != 0)
            {
                return;
            }

            for (var vertex = 0; vertex < SourceToTarget.Length; vertex++)
            {
                SourceToTarget[vertex] = -1;
            }

            TriangleCount = DiscardedTriangle.Length - DiscardedTriangle.CountBits(0, DiscardedTriangle.Length);
            VertexCount = DiscardedVertex.Length - DiscardedVertex.CountBits(0, DiscardedVertex.Length);
            if (TargetTriangleCount < TriangleCount &&
                VertexTexCoord0Buffer.Length == VertexPositionBuffer.Length)
            {
                if (TryApplyUvLoopBatch(TargetTriangleCount))
                {
                    return;
                }
            }

            Diagnostics[UvLoopDissolveDiagnostics.LoopPhaseStopped] = 1;
        }

        bool TryApplyUvLoopBatch(int targetTriangleCount)
        {
            var temporaryCapacity = math.max(TriangleCount * 2, 1);
            var quads = new UnsafeList<UvQuad>(math.max(TriangleCount / 2, 1), Allocator.Temp);
            var triangleQuads = new UnsafeList<int>(math.max(Triangles.Length, 1), Allocator.Temp);
            var quadEdges = new UnsafeList<int2>(temporaryCapacity, Allocator.Temp);
            var quadEdgeSet = new NativeHashSet<int2>(temporaryCapacity, Allocator.Temp);
            var chainEdges = new UnsafeList<int2>(temporaryCapacity, Allocator.Temp);
            var chains = new UnsafeList<UvLoopChain>(temporaryCapacity, Allocator.Temp);
            var edgeToChain = new NativeHashMap<int2, int>(temporaryCapacity * 2, Allocator.Temp);
            var chainNeighbors = new NativeParallelMultiHashMap<int, int>(temporaryCapacity * 2, Allocator.Temp);
            var colors = new UnsafeList<int>(temporaryCapacity, Allocator.Temp);
            var components = new UnsafeList<int>(temporaryCapacity, Allocator.Temp);
            var componentValid = new UnsafeList<byte>(temporaryCapacity, Allocator.Temp);
            var sourceToTarget = new NativeHashMap<int, int>(math.max(VertexCount, 1), Allocator.Temp);
            try
            {
                triangleQuads.Resize(Triangles.Length, NativeArrayOptions.UninitializedMemory);
                for (var i = 0; i < triangleQuads.Length; i++)
                {
                    triangleQuads[i] = -1;
                }

                BuildUvQuads(ref quads, triangleQuads);
                if (quads.Length < 2)
                {
                    return false;
                }

                CollectUvQuadEdges(quads, ref quadEdges, quadEdgeSet);
                BuildUvLoopChains(quadEdges, ref chainEdges, ref chains, edgeToChain);
                if (chains.Length < 3)
                {
                    return false;
                }

                BuildUvLoopChainAdjacency(quads, chainEdges, chains, edgeToChain, chainNeighbors);
                colors.Resize(chains.Length, NativeArrayOptions.UninitializedMemory);
                components.Resize(chains.Length, NativeArrayOptions.UninitializedMemory);
                for (var i = 0; i < chains.Length; i++)
                {
                    colors[i] = -1;
                    components[i] = -1;
                }

                ColorUvLoopComponents(chains, chainNeighbors, colors, components, ref componentValid);

                var bestComponent = -1;
                var bestParity = -1;
                var bestCost = float.PositiveInfinity;
                for (var component = 0; component < componentValid.Length; component++)
                {
                    if (componentValid[component] == 0)
                    {
                        continue;
                    }

                    for (var parity = 0; parity < 2; parity++)
                    {
                        if (!TryEvaluateUvLoopBatch(
                                component,
                                parity,
                                targetTriangleCount,
                                quads,
                                chainEdges,
                                chains,
                                edgeToChain,
                                chainNeighbors,
                                colors,
                                components,
                                out var cost))
                        {
                            continue;
                        }

                        if (cost < bestCost || cost == bestCost &&
                            (component < bestComponent || component == bestComponent && parity < bestParity))
                        {
                            bestComponent = component;
                            bestParity = parity;
                            bestCost = cost;
                        }
                    }
                }

                var hasMapping = bestComponent >= 0 && TryBuildUvLoopBatchMapping(
                    bestComponent,
                    bestParity,
                    quads,
                    chainEdges,
                    chains,
                    edgeToChain,
                    chainNeighbors,
                    colors,
                    components,
                    sourceToTarget,
                    out _);
                if (hasMapping)
                {
                    ExpandUvSeamMappings(sourceToTarget);
                    hasMapping = IsUvLoopBatchValid(
                        sourceToTarget,
                        targetTriangleCount,
                        out var batchDiscardedTriangleCount) &&
                        batchDiscardedTriangleCount > 0;
                }

                if (!hasMapping)
                {
                    sourceToTarget.Clear();
                    if (!TryBuildBestSingleUvLoopMapping(
                            targetTriangleCount,
                            quads,
                            chainEdges,
                            chains,
                            edgeToChain,
                            chainNeighbors,
                            sourceToTarget))
                    {
                        return false;
                    }
                }

                if (!IsUvLoopBatchValid(sourceToTarget, targetTriangleCount, out var discardedTriangleCount) ||
                    discardedTriangleCount == 0)
                {
                    return false;
                }

                for (var source = 0; source < SourceToTarget.Length; source++)
                {
                    if (sourceToTarget.TryGetValue(source, out var target))
                    {
                        SourceToTarget[source] = target;
                    }
                }

                Diagnostics[UvLoopDissolveDiagnostics.PassCount]++;
                Diagnostics[UvLoopDissolveDiagnostics.DissolvedTriangleCount] += discardedTriangleCount;

                return true;
            }
            finally
            {
                sourceToTarget.Dispose();
                componentValid.Dispose();
                components.Dispose();
                colors.Dispose();
                chainNeighbors.Dispose();
                edgeToChain.Dispose();
                chains.Dispose();
                chainEdges.Dispose();
                quadEdgeSet.Dispose();
                quadEdges.Dispose();
                triangleQuads.Dispose();
                quads.Dispose();
            }
        }

        void BuildUvQuads(ref UnsafeList<UvQuad> quads, UnsafeList<int> triangleQuads)
        {
            for (var triangleIndex = 0; triangleIndex < Triangles.Length; triangleIndex++)
            {
                if (IsDiscardedTriangle(triangleIndex) || triangleQuads[triangleIndex] >= 0)
                {
                    continue;
                }

                if (!TryFindBestUvQuadNeighbor(triangleIndex, triangleQuads, out var neighbor, out var quad))
                {
                    continue;
                }

                if (!TryFindBestUvQuadNeighbor(neighbor, triangleQuads, out var reciprocal, out _) ||
                    reciprocal != triangleIndex)
                {
                    continue;
                }

                var quadIndex = quads.Length;
                quads.Add(quad);
                triangleQuads[triangleIndex] = quadIndex;
                triangleQuads[neighbor] = quadIndex;
            }
        }

        bool TryFindBestUvQuadNeighbor(
            int triangleIndex,
            UnsafeList<int> triangleQuads,
            out int bestNeighbor,
            out UvQuad bestQuad)
        {
            bestNeighbor = -1;
            bestQuad = default;
            var bestScore = float.PositiveInfinity;
            var triangle = Triangles[triangleIndex];
            var triangleSubMesh = GetTriangleSubMeshIndex(triangleIndex);
            int3 edgeA = new(triangle.x, triangle.y, triangle.z);
            int3 edgeB = new(triangle.y, triangle.z, triangle.x);

            for (var edgeIndex = 0; edgeIndex < 3; edgeIndex++)
            {
                var vertexA = edgeA[edgeIndex];
                var vertexB = edgeB[edgeIndex];
                var adjacentCount = 0;
                var adjacentTriangle = -1;
                foreach (var candidate in VertexContainingTriangles.GetValuesForKey(vertexA))
                {
                    if (candidate == triangleIndex || IsDiscardedTriangle(candidate) ||
                        !math.any(Triangles[candidate] == vertexB))
                    {
                        continue;
                    }

                    adjacentCount++;
                    adjacentTriangle = candidate;
                }

                if (adjacentCount != 1 || adjacentTriangle < 0 ||
                    triangleQuads[adjacentTriangle] >= 0 ||
                    GetTriangleSubMeshIndex(adjacentTriangle) != triangleSubMesh ||
                    !TryCreateUvQuad(triangleIndex, adjacentTriangle, out var candidateQuad))
                {
                    continue;
                }

                if (candidateQuad.Score < bestScore ||
                    candidateQuad.Score == bestScore && adjacentTriangle < bestNeighbor)
                {
                    bestNeighbor = adjacentTriangle;
                    bestQuad = candidateQuad;
                    bestScore = candidateQuad.Score;
                }
            }

            return bestNeighbor >= 0;
        }

        bool TryCreateUvQuad(int triangleAIndex, int triangleBIndex, out UvQuad quad)
        {
            quad = default;
            var triangleA = Triangles[triangleAIndex];
            var triangleB = Triangles[triangleBIndex];
            int3 edgeA = new(triangleA.x, triangleA.y, triangleA.z);
            int3 edgeB = new(triangleA.y, triangleA.z, triangleA.x);

            for (var edgeIndex = 0; edgeIndex < 3; edgeIndex++)
            {
                var u = edgeA[edgeIndex];
                var v = edgeB[edgeIndex];
                if (!TryGetReverseEdgeThirdVertex(triangleB, u, v, out var otherB))
                {
                    continue;
                }

                var otherA = triangleA[3 - edgeIndex - ((edgeIndex + 1) % 3)];
                if (otherA == otherB || otherA == u || otherA == v)
                {
                    return false;
                }

                var vertices = new int4(u, otherB, v, otherA);
                if (!TryScoreUvQuad(vertices, triangleAIndex, triangleBIndex, out var score))
                {
                    return false;
                }

                quad = new UvQuad { Vertices = vertices, Score = score };
                return true;
            }

            return false;
        }

        static bool TryGetReverseEdgeThirdVertex(int3 triangle, int u, int v, out int third)
        {
            if (triangle.x == v && triangle.y == u)
            {
                third = triangle.z;
                return true;
            }
            if (triangle.y == v && triangle.z == u)
            {
                third = triangle.x;
                return true;
            }
            if (triangle.z == v && triangle.x == u)
            {
                third = triangle.y;
                return true;
            }

            third = -1;
            return false;
        }

        bool TryScoreUvQuad(int4 vertices, int triangleAIndex, int triangleBIndex, out float score)
        {
            score = float.PositiveInfinity;
            var triangleNormalA = TriangleNormals[triangleAIndex];
            var triangleNormalB = TriangleNormals[triangleBIndex];
            if (math.dot(triangleNormalA, triangleNormalB) < UvLoopMinNormalDot)
            {
                return false;
            }

            float3x4 positions = new(
                VertexPositionBuffer[vertices.x],
                VertexPositionBuffer[vertices.y],
                VertexPositionBuffer[vertices.z],
                VertexPositionBuffer[vertices.w]);
            float2x4 uvs = new(
                VertexTexCoord0Buffer[vertices.x].xy,
                VertexTexCoord0Buffer[vertices.y].xy,
                VertexTexCoord0Buffer[vertices.z].xy,
                VertexTexCoord0Buffer[vertices.w].xy);

            var referenceNormal = math.normalizesafe(triangleNormalA + triangleNormalB, triangleNormalA);
            var geometricSign = 0f;
            var uvSign = 0f;
            var cornerPenalty = 0f;
            var edgeLengths = new float4();
            for (var i = 0; i < 4; i++)
            {
                var previous = (i + 3) & 3;
                var next = (i + 1) & 3;
                var edgeToPrevious = positions[previous] - positions[i];
                var edgeToNext = positions[next] - positions[i];
                var cross = math.dot(math.cross(edgeToNext, edgeToPrevious), referenceNormal);
                if (math.abs(cross) <= 1e-10f || geometricSign != 0f && cross * geometricSign < 0f)
                {
                    return false;
                }
                geometricSign = cross;

                var uvToPrevious = uvs[previous] - uvs[i];
                var uvToNext = uvs[next] - uvs[i];
                var uvCross = uvToNext.x * uvToPrevious.y - uvToNext.y * uvToPrevious.x;
                if (math.abs(uvCross) <= 1e-10f || uvSign != 0f && uvCross * uvSign < 0f)
                {
                    return false;
                }
                uvSign = uvCross;

                var previousLength = math.length(edgeToPrevious);
                var nextLength = math.length(edgeToNext);
                if (previousLength <= 1e-8f || nextLength <= 1e-8f)
                {
                    return false;
                }
                cornerPenalty += math.abs(math.dot(edgeToPrevious / previousLength, edgeToNext / nextLength));
                edgeLengths[i] = nextLength;
            }

            var oppositePenalty = math.abs(edgeLengths.x - edgeLengths.z) /
                math.max(edgeLengths.x + edgeLengths.z, 1e-8f) +
                math.abs(edgeLengths.y - edgeLengths.w) /
                math.max(edgeLengths.y + edgeLengths.w, 1e-8f);
            var diagonalA = math.distance(positions.c0, positions.c2);
            var diagonalB = math.distance(positions.c1, positions.c3);
            var diagonalPenalty = math.abs(diagonalA - diagonalB) / math.max(diagonalA + diagonalB, 1e-8f);
            score = cornerPenalty + oppositePenalty + diagonalPenalty;
            return true;
        }

        void CollectUvQuadEdges(
            UnsafeList<UvQuad> quads,
            ref UnsafeList<int2> quadEdges,
            NativeHashSet<int2> quadEdgeSet)
        {
            for (var quadIndex = 0; quadIndex < quads.Length; quadIndex++)
            {
                var vertices = quads[quadIndex].Vertices;
                AddUvQuadEdge(vertices.x, vertices.y, ref quadEdges, quadEdgeSet);
                AddUvQuadEdge(vertices.y, vertices.z, ref quadEdges, quadEdgeSet);
                AddUvQuadEdge(vertices.z, vertices.w, ref quadEdges, quadEdgeSet);
                AddUvQuadEdge(vertices.w, vertices.x, ref quadEdges, quadEdgeSet);
            }
        }

        static void AddUvQuadEdge(
            int vertexA,
            int vertexB,
            ref UnsafeList<int2> quadEdges,
            NativeHashSet<int2> quadEdgeSet)
        {
            var edge = NormalizeUvLoopEdge(vertexA, vertexB);
            if (quadEdgeSet.Add(edge))
            {
                quadEdges.Add(edge);
            }
        }

        void BuildUvLoopChains(
            UnsafeList<int2> quadEdges,
            ref UnsafeList<int2> chainEdges,
            ref UnsafeList<UvLoopChain> chains,
            NativeHashMap<int2, int> edgeToChain)
        {
            var vertexDegrees = new NativeArray<int>(VertexPositionBuffer.Length, Allocator.Temp);
            var vertexOffsets = new NativeArray<int>(VertexPositionBuffer.Length + 1, Allocator.Temp);
            var vertexCursors = new NativeArray<int>(VertexPositionBuffer.Length, Allocator.Temp);
            var incidentEdges = new NativeArray<int>(quadEdges.Length * 2, Allocator.Temp);
            var parents = new NativeArray<int>(quadEdges.Length, Allocator.Temp);
            var edgeChains = new NativeArray<int>(quadEdges.Length, Allocator.Temp);

            for (var edgeIndex = 0; edgeIndex < quadEdges.Length; edgeIndex++)
            {
                var edge = quadEdges[edgeIndex];
                vertexDegrees[edge.x]++;
                vertexDegrees[edge.y]++;
                parents[edgeIndex] = edgeIndex;
            }

            for (var vertex = 0; vertex < VertexPositionBuffer.Length; vertex++)
            {
                vertexOffsets[vertex + 1] = vertexOffsets[vertex] + vertexDegrees[vertex];
                vertexCursors[vertex] = vertexOffsets[vertex];
            }

            for (var edgeIndex = 0; edgeIndex < quadEdges.Length; edgeIndex++)
            {
                var edge = quadEdges[edgeIndex];
                incidentEdges[vertexCursors[edge.x]++] = edgeIndex;
                incidentEdges[vertexCursors[edge.y]++] = edgeIndex;
            }

            for (var vertex = 0; vertex < VertexPositionBuffer.Length; vertex++)
            {
                var start = vertexOffsets[vertex];
                var end = vertexOffsets[vertex + 1];
                for (var a = start; a < end; a++)
                {
                    for (var b = a + 1; b < end; b++)
                    {
                        var edgeAIndex = incidentEdges[a];
                        var edgeBIndex = incidentEdges[b];
                        if (AreUvLoopEdgesContinuous(quadEdges[edgeAIndex], quadEdges[edgeBIndex], vertex))
                        {
                            UnionUvLoopEdges(parents, edgeAIndex, edgeBIndex);
                        }
                    }
                }
            }

            var rootToChain = new UnsafeHashMap<int, int>(math.max(quadEdges.Length, 1), Allocator.Temp);
            var chainCounts = new UnsafeList<int>(math.max(quadEdges.Length, 1), Allocator.Temp);
            for (var edgeIndex = 0; edgeIndex < quadEdges.Length; edgeIndex++)
            {
                var root = FindUvLoopEdgeRoot(parents, edgeIndex);
                if (!rootToChain.TryGetValue(root, out var chainIndex))
                {
                    chainIndex = chainCounts.Length;
                    rootToChain.TryAdd(root, chainIndex);
                    chainCounts.Add(0);
                }
                edgeChains[edgeIndex] = chainIndex;
                chainCounts[chainIndex]++;
            }

            var chainOffsets = new NativeArray<int>(chainCounts.Length + 1, Allocator.Temp);
            var chainCursors = new NativeArray<int>(chainCounts.Length, Allocator.Temp);
            for (var chainIndex = 0; chainIndex < chainCounts.Length; chainIndex++)
            {
                chainOffsets[chainIndex + 1] = chainOffsets[chainIndex] + chainCounts[chainIndex];
                chainCursors[chainIndex] = chainOffsets[chainIndex];
            }

            chainEdges.Resize(quadEdges.Length, NativeArrayOptions.UninitializedMemory);
            for (var edgeIndex = 0; edgeIndex < quadEdges.Length; edgeIndex++)
            {
                chainEdges[chainCursors[edgeChains[edgeIndex]]++] = quadEdges[edgeIndex];
            }

            for (var chainIndex = 0; chainIndex < chainCounts.Length; chainIndex++)
            {
                var edgeStart = chainOffsets[chainIndex];
                var edgeCount = chainCounts[chainIndex];
                var isProtected = false;
                for (var edgeOffset = 0; edgeOffset < edgeCount; edgeOffset++)
                {
                    var edge = chainEdges[edgeStart + edgeOffset];
                    edgeToChain.TryAdd(edge, chainIndex);
                    isProtected |= IsUvLoopProtectedEdge(edge);
                }
                chains.Add(new UvLoopChain
                {
                    EdgeStart = edgeStart,
                    EdgeCount = edgeCount,
                    IsProtected = isProtected,
                });
            }

            chainCursors.Dispose();
            chainOffsets.Dispose();
            chainCounts.Dispose();
            rootToChain.Dispose();
            edgeChains.Dispose();
            parents.Dispose();
            incidentEdges.Dispose();
            vertexCursors.Dispose();
            vertexOffsets.Dispose();
            vertexDegrees.Dispose();
        }

        bool AreUvLoopEdgesContinuous(int2 edgeA, int2 edgeB, int sharedVertex)
        {
            var otherA = edgeA.x == sharedVertex ? edgeA.y : edgeA.x;
            var otherB = edgeB.x == sharedVertex ? edgeB.y : edgeB.x;
            var uvA = math.normalizesafe(VertexTexCoord0Buffer[otherA].xy - VertexTexCoord0Buffer[sharedVertex].xy);
            var uvB = math.normalizesafe(VertexTexCoord0Buffer[otherB].xy - VertexTexCoord0Buffer[sharedVertex].xy);
            var positionA = math.normalizesafe(VertexPositionBuffer[otherA] - VertexPositionBuffer[sharedVertex]);
            var positionB = math.normalizesafe(VertexPositionBuffer[otherB] - VertexPositionBuffer[sharedVertex]);
            return math.dot(uvA, uvB) <= -UvLoopMinContinuationDot &&
                math.dot(positionA, positionB) <= -0.25f;
        }

        static int FindUvLoopEdgeRoot(NativeArray<int> parents, int edge)
        {
            while (parents[edge] != edge)
            {
                parents[edge] = parents[parents[edge]];
                edge = parents[edge];
            }
            return edge;
        }

        static void UnionUvLoopEdges(NativeArray<int> parents, int edgeA, int edgeB)
        {
            var rootA = FindUvLoopEdgeRoot(parents, edgeA);
            var rootB = FindUvLoopEdgeRoot(parents, edgeB);
            if (rootA == rootB)
            {
                return;
            }

            if (rootA < rootB)
            {
                parents[rootB] = rootA;
            }
            else
            {
                parents[rootA] = rootB;
            }
        }

        bool IsUvLoopProtectedEdge(int2 edge)
        {
            var commonTriangleCount = 0;
            var subMesh = -1;
            foreach (var triangleIndex in VertexContainingTriangles.GetValuesForKey(edge.x))
            {
                if (IsDiscardedTriangle(triangleIndex) || !math.any(Triangles[triangleIndex] == edge.y))
                {
                    continue;
                }

                var triangleSubMesh = GetTriangleSubMeshIndex(triangleIndex);
                if (subMesh >= 0 && subMesh != triangleSubMesh)
                {
                    return true;
                }

                subMesh = triangleSubMesh;
                commonTriangleCount++;
            }

            return commonTriangleCount != 2;
        }

        void BuildUvLoopChainAdjacency(
            UnsafeList<UvQuad> quads,
            UnsafeList<int2> chainEdges,
            UnsafeList<UvLoopChain> chains,
            NativeHashMap<int2, int> edgeToChain,
            NativeParallelMultiHashMap<int, int> chainNeighbors)
        {
            using var uniqueAdjacency = new NativeHashSet<int2>(math.max(quads.Length * 2, 1), Allocator.Temp);
            for (var quadIndex = 0; quadIndex < quads.Length; quadIndex++)
            {
                var vertices = quads[quadIndex].Vertices;
                var chain0 = GetUvLoopChainIndex(vertices.x, vertices.y, chainEdges, chains, edgeToChain);
                var chain1 = GetUvLoopChainIndex(vertices.y, vertices.z, chainEdges, chains, edgeToChain);
                var chain2 = GetUvLoopChainIndex(vertices.z, vertices.w, chainEdges, chains, edgeToChain);
                var chain3 = GetUvLoopChainIndex(vertices.w, vertices.x, chainEdges, chains, edgeToChain);
                if (chain0 < 0 || chain1 < 0 || chain2 < 0 || chain3 < 0)
                {
                    continue;
                }
                AddUvLoopChainAdjacency(chain0, chain2, chainNeighbors, uniqueAdjacency);
                AddUvLoopChainAdjacency(chain1, chain3, chainNeighbors, uniqueAdjacency);
            }
        }

        static void AddUvLoopChainAdjacency(
            int chainA,
            int chainB,
            NativeParallelMultiHashMap<int, int> chainNeighbors,
            NativeHashSet<int2> uniqueAdjacency)
        {
            if (chainA == chainB)
            {
                return;
            }

            var adjacency = NormalizeUvLoopEdge(chainA, chainB);
            if (!uniqueAdjacency.Add(adjacency))
            {
                return;
            }

            chainNeighbors.Add(chainA, chainB);
            chainNeighbors.Add(chainB, chainA);
        }

        static void ColorUvLoopComponents(
            UnsafeList<UvLoopChain> chains,
            NativeParallelMultiHashMap<int, int> chainNeighbors,
            UnsafeList<int> colors,
            UnsafeList<int> components,
            ref UnsafeList<byte> componentValid)
        {
            using var queue = new UnsafeList<int>(math.max(chains.Length, 1), Allocator.Temp);
            for (var initialChain = 0; initialChain < chains.Length; initialChain++)
            {
                if (colors[initialChain] >= 0)
                {
                    continue;
                }

                var component = componentValid.Length;
                componentValid.Add(1);
                queue.Clear();
                queue.Add(initialChain);
                colors[initialChain] = 0;
                components[initialChain] = component;

                for (var queueIndex = 0; queueIndex < queue.Length; queueIndex++)
                {
                    var chain = queue[queueIndex];
                    foreach (var neighbor in chainNeighbors.GetValuesForKey(chain))
                    {
                        if (colors[neighbor] < 0)
                        {
                            colors[neighbor] = 1 - colors[chain];
                            components[neighbor] = component;
                            queue.Add(neighbor);
                        }
                        else if (colors[neighbor] == colors[chain])
                        {
                            componentValid[component] = 0;
                        }
                    }
                }
            }
        }

        bool TryEvaluateUvLoopBatch(
            int component,
            int parity,
            int targetTriangleCount,
            UnsafeList<UvQuad> quads,
            UnsafeList<int2> chainEdges,
            UnsafeList<UvLoopChain> chains,
            NativeHashMap<int2, int> edgeToChain,
            NativeParallelMultiHashMap<int, int> chainNeighbors,
            UnsafeList<int> colors,
            UnsafeList<int> components,
            out float cost)
        {
            using var mapping = new NativeHashMap<int, int>(math.max(VertexCount, 1), Allocator.Temp);
            if (!TryBuildUvLoopBatchMapping(
                    component,
                    parity,
                    quads,
                    chainEdges,
                    chains,
                    edgeToChain,
                    chainNeighbors,
                    colors,
                    components,
                    mapping,
                    out cost))
            {
                return false;
            }

            return IsUvLoopBatchValid(mapping, targetTriangleCount, out _);
        }

        bool TryBuildBestSingleUvLoopMapping(
            int targetTriangleCount,
            UnsafeList<UvQuad> quads,
            UnsafeList<int2> chainEdges,
            UnsafeList<UvLoopChain> chains,
            NativeHashMap<int2, int> edgeToChain,
            NativeParallelMultiHashMap<int, int> chainNeighbors,
            NativeHashMap<int, int> sourceToTarget)
        {
            var bestSourceChain = -1;
            var bestTargetChain = -1;
            var bestIsPartialSegment = false;
            var bestCost = float.PositiveInfinity;
            using var candidateMapping = new NativeHashMap<int, int>(math.max(VertexCount, 1), Allocator.Temp);
            for (var sourceChain = 0; sourceChain < chains.Length; sourceChain++)
            {
                if (chains[sourceChain].IsProtected)
                {
                    continue;
                }

                foreach (var targetChain in chainNeighbors.GetValuesForKey(sourceChain))
                {
                    candidateMapping.Clear();
                    var isPartialSegment = false;
                    var hasCandidate = TryEvaluateUvLoopChainTarget(
                            sourceChain,
                            targetChain,
                            quads,
                            chainEdges,
                            chains,
                            edgeToChain,
                            out var cost) &&
                        TryAddUvLoopChainMapping(
                            sourceChain,
                            targetChain,
                            quads,
                            chainEdges,
                            chains,
                            edgeToChain,
                            candidateMapping);
                    if (hasCandidate)
                    {
                        ExpandUvSeamMappings(candidateMapping);
                        hasCandidate = IsUvLoopBatchValid(
                            candidateMapping,
                            targetTriangleCount,
                            out var discardedTriangleCount) &&
                            discardedTriangleCount > 0;
                    }

                    if (!hasCandidate)
                    {
                        candidateMapping.Clear();
                        hasCandidate = TryBuildBestPartialUvLoopSegmentMapping(
                            sourceChain,
                            targetChain,
                            targetTriangleCount,
                            quads,
                            chainEdges,
                            chains,
                            edgeToChain,
                            candidateMapping,
                            out cost);
                        isPartialSegment = hasCandidate;
                    }

                    if (!hasCandidate)
                    {
                        continue;
                    }

                    if (cost < bestCost || cost == bestCost &&
                        (sourceChain < bestSourceChain ||
                            sourceChain == bestSourceChain && targetChain < bestTargetChain))
                    {
                        bestSourceChain = sourceChain;
                        bestTargetChain = targetChain;
                        bestIsPartialSegment = isPartialSegment;
                        bestCost = cost;
                    }
                }
            }

            if (bestSourceChain < 0)
            {
                return false;
            }

            if (bestIsPartialSegment)
            {
                return TryBuildBestPartialUvLoopSegmentMapping(
                    bestSourceChain,
                    bestTargetChain,
                    targetTriangleCount,
                    quads,
                    chainEdges,
                    chains,
                    edgeToChain,
                    sourceToTarget,
                    out _);
            }

            if (!TryAddUvLoopChainMapping(
                    bestSourceChain,
                    bestTargetChain,
                    quads,
                    chainEdges,
                    chains,
                    edgeToChain,
                    sourceToTarget))
            {
                return false;
            }

            ExpandUvSeamMappings(sourceToTarget);
            return true;
        }

        bool TryBuildBestPartialUvLoopSegmentMapping(
            int sourceChain,
            int targetChain,
            int targetTriangleCount,
            UnsafeList<UvQuad> quads,
            UnsafeList<int2> chainEdges,
            UnsafeList<UvLoopChain> chains,
            NativeHashMap<int2, int> edgeToChain,
            NativeHashMap<int, int> sourceToTarget,
            out float cost)
        {
            cost = float.PositiveInfinity;
            using var localMapping = new NativeHashMap<int, int>(
                math.max(chains[sourceChain].EdgeCount * 2, 1),
                Allocator.Temp);
            if (!TryBuildUvLoopLocalMapping(
                    sourceChain,
                    targetChain,
                    quads,
                    chainEdges,
                    chains,
                    edgeToChain,
                    localMapping))
            {
                return false;
            }

            var sourceData = chains[sourceChain];
            var bestEdgeAOffset = -1;
            var bestEdgeBOffset = -1;
            using var candidateMapping = new NativeHashMap<int, int>(math.max(VertexCount, 1), Allocator.Temp);
            for (var edgeAOffset = 0; edgeAOffset < sourceData.EdgeCount; edgeAOffset++)
            {
                var edgeA = chainEdges[sourceData.EdgeStart + edgeAOffset];
                if (!localMapping.ContainsKey(edgeA.x) || !localMapping.ContainsKey(edgeA.y))
                {
                    continue;
                }

                for (var edgeBOffset = edgeAOffset + 1; edgeBOffset < sourceData.EdgeCount; edgeBOffset++)
                {
                    var edgeB = chainEdges[sourceData.EdgeStart + edgeBOffset];
                    if (!SharesUvLoopVertex(edgeA, edgeB) ||
                        !localMapping.ContainsKey(edgeB.x) || !localMapping.ContainsKey(edgeB.y))
                    {
                        continue;
                    }

                    candidateMapping.Clear();
                    if (!TryAddPartialUvLoopEdgeMapping(edgeA, localMapping, candidateMapping) ||
                        !TryAddPartialUvLoopEdgeMapping(edgeB, localMapping, candidateMapping))
                    {
                        continue;
                    }

                    ExpandUvSeamMappings(candidateMapping);
                    if (!IsUvLoopBatchValid(candidateMapping, targetTriangleCount, out var discardedTriangleCount) ||
                        discardedTriangleCount == 0)
                    {
                        continue;
                    }

                    var candidateCost = GetUvLoopMappingCost(candidateMapping);
                    if (candidateCost < cost || candidateCost == cost &&
                        (edgeAOffset < bestEdgeAOffset ||
                            edgeAOffset == bestEdgeAOffset && edgeBOffset < bestEdgeBOffset))
                    {
                        bestEdgeAOffset = edgeAOffset;
                        bestEdgeBOffset = edgeBOffset;
                        cost = candidateCost;
                    }
                }
            }

            if (bestEdgeAOffset < 0)
            {
                for (var edgeOffset = 0; edgeOffset < sourceData.EdgeCount; edgeOffset++)
                {
                    var edge = chainEdges[sourceData.EdgeStart + edgeOffset];
                    candidateMapping.Clear();
                    if (!TryAddPartialUvLoopEdgeMapping(edge, localMapping, candidateMapping))
                    {
                        continue;
                    }

                    ExpandUvSeamMappings(candidateMapping);
                    if (!IsUvLoopBatchValid(candidateMapping, targetTriangleCount, out var discardedTriangleCount) ||
                        discardedTriangleCount == 0)
                    {
                        continue;
                    }

                    var candidateCost = GetUvLoopMappingCost(candidateMapping);
                    if (candidateCost < cost || candidateCost == cost && edgeOffset < bestEdgeAOffset)
                    {
                        bestEdgeAOffset = edgeOffset;
                        bestEdgeBOffset = -1;
                        cost = candidateCost;
                    }
                }
                if (bestEdgeAOffset < 0)
                {
                    return false;
                }
            }

            var bestEdgeA = chainEdges[sourceData.EdgeStart + bestEdgeAOffset];
            if (!TryAddPartialUvLoopEdgeMapping(bestEdgeA, localMapping, sourceToTarget))
            {
                return false;
            }
            if (bestEdgeBOffset >= 0)
            {
                var bestEdgeB = chainEdges[sourceData.EdgeStart + bestEdgeBOffset];
                if (!TryAddPartialUvLoopEdgeMapping(bestEdgeB, localMapping, sourceToTarget))
                {
                    return false;
                }
            }

            ExpandUvSeamMappings(sourceToTarget);
            return true;
        }

        static bool SharesUvLoopVertex(int2 edgeA, int2 edgeB)
        {
            return edgeA.x == edgeB.x || edgeA.x == edgeB.y ||
                edgeA.y == edgeB.x || edgeA.y == edgeB.y;
        }

        static bool TryAddPartialUvLoopEdgeMapping(
            int2 edge,
            NativeHashMap<int, int> localMapping,
            NativeHashMap<int, int> sourceToTarget)
        {
            return localMapping.TryGetValue(edge.x, out var targetA) &&
                localMapping.TryGetValue(edge.y, out var targetB) &&
                TryAddUvLoopVertexMapping(edge.x, targetA, sourceToTarget) &&
                TryAddUvLoopVertexMapping(edge.y, targetB, sourceToTarget);
        }

        float GetUvLoopMappingCost(NativeHashMap<int, int> mapping)
        {
            var cost = 0f;
            var count = 0;
            foreach (var pair in mapping)
            {
                var positionDistance = math.lengthsq(
                    VertexPositionBuffer[pair.Key] - VertexPositionBuffer[pair.Value]);
                var uvDistance = math.lengthsq(
                    VertexTexCoord0Buffer[pair.Key].xy - VertexTexCoord0Buffer[pair.Value].xy);
                cost += positionDistance + uvDistance * 0.01f;
                count++;
            }
            return count > 0 ? cost / count : float.PositiveInfinity;
        }

        bool TryBuildUvLoopBatchMapping(
            int component,
            int parity,
            UnsafeList<UvQuad> quads,
            UnsafeList<int2> chainEdges,
            UnsafeList<UvLoopChain> chains,
            NativeHashMap<int2, int> edgeToChain,
            NativeParallelMultiHashMap<int, int> chainNeighbors,
            UnsafeList<int> colors,
            UnsafeList<int> components,
            NativeHashMap<int, int> sourceToTarget,
            out float cost)
        {
            cost = 0f;
            var selectedChainCount = 0;
            for (var chain = 0; chain < chains.Length; chain++)
            {
                if (components[chain] != component || colors[chain] != parity || chains[chain].IsProtected)
                {
                    continue;
                }

                var bestTargetChain = -1;
                var bestTargetCost = float.PositiveInfinity;
                foreach (var neighbor in chainNeighbors.GetValuesForKey(chain))
                {
                    if (components[neighbor] != component || colors[neighbor] == parity)
                    {
                        continue;
                    }

                    if (TryEvaluateUvLoopChainTarget(
                            chain,
                            neighbor,
                            quads,
                            chainEdges,
                            chains,
                            edgeToChain,
                            out var targetCost) &&
                        (targetCost < bestTargetCost || targetCost == bestTargetCost && neighbor < bestTargetChain))
                    {
                        bestTargetChain = neighbor;
                        bestTargetCost = targetCost;
                    }
                }

                if (bestTargetChain < 0 || !TryAddUvLoopChainMapping(
                        chain,
                        bestTargetChain,
                        quads,
                        chainEdges,
                        chains,
                        edgeToChain,
                        sourceToTarget))
                {
                    return false;
                }

                selectedChainCount++;
                cost += bestTargetCost;
            }

            return selectedChainCount > 0;
        }

        bool TryEvaluateUvLoopChainTarget(
            int sourceChain,
            int targetChain,
            UnsafeList<UvQuad> quads,
            UnsafeList<int2> chainEdges,
            UnsafeList<UvLoopChain> chains,
            NativeHashMap<int2, int> edgeToChain,
            out float cost)
        {
            using var mapping = new NativeHashMap<int, int>(math.max(chains[sourceChain].EdgeCount * 2, 1), Allocator.Temp);
            if (!TryAddUvLoopChainMapping(
                    sourceChain,
                    targetChain,
                    quads,
                    chainEdges,
                    chains,
                    edgeToChain,
                    mapping))
            {
                cost = float.PositiveInfinity;
                return false;
            }

            cost = 0f;
            var mappedVertexCount = 0;
            var chainData = chains[sourceChain];
            using var visitedVertices = new UnsafeHashSet<int>(math.max(chainData.EdgeCount * 2, 1), Allocator.Temp);
            for (var edgeOffset = 0; edgeOffset < chainData.EdgeCount; edgeOffset++)
            {
                var edge = chainEdges[chainData.EdgeStart + edgeOffset];
                AddUvLoopMappingCost(edge.x, mapping, visitedVertices, ref mappedVertexCount, ref cost);
                AddUvLoopMappingCost(edge.y, mapping, visitedVertices, ref mappedVertexCount, ref cost);
            }

            if (mappedVertexCount == 0)
            {
                return false;
            }

            cost /= mappedVertexCount;
            return true;
        }

        void AddUvLoopMappingCost(
            int source,
            NativeHashMap<int, int> mapping,
            UnsafeHashSet<int> visitedVertices,
            ref int mappedVertexCount,
            ref float cost)
        {
            if (!visitedVertices.Add(source) || !mapping.TryGetValue(source, out var target))
            {
                return;
            }

            var positionDistance = math.lengthsq(VertexPositionBuffer[source] - VertexPositionBuffer[target]);
            var uvDistance = math.lengthsq(
                VertexTexCoord0Buffer[source].xy - VertexTexCoord0Buffer[target].xy);
            cost += positionDistance + uvDistance * 0.01f;
            mappedVertexCount++;
        }

        bool TryAddUvLoopChainMapping(
            int sourceChain,
            int targetChain,
            UnsafeList<UvQuad> quads,
            UnsafeList<int2> chainEdges,
            UnsafeList<UvLoopChain> chains,
            NativeHashMap<int2, int> edgeToChain,
            NativeHashMap<int, int> sourceToTarget)
        {
            var sourceData = chains[sourceChain];
            using var sourceVertices = new UnsafeHashSet<int>(math.max(sourceData.EdgeCount * 2, 1), Allocator.Temp);
            for (var edgeOffset = 0; edgeOffset < sourceData.EdgeCount; edgeOffset++)
            {
                var edge = chainEdges[sourceData.EdgeStart + edgeOffset];
                sourceVertices.Add(edge.x);
                sourceVertices.Add(edge.y);
            }

            using var localMapping = new NativeHashMap<int, int>(math.max(sourceData.EdgeCount * 2, 1), Allocator.Temp);
            if (!TryBuildUvLoopLocalMapping(
                    sourceChain,
                    targetChain,
                    quads,
                    chainEdges,
                    chains,
                    edgeToChain,
                    localMapping))
            {
                return false;
            }

            var mappedVertexCount = 0;
            foreach (var source in sourceVertices)
            {
                if (!localMapping.TryGetValue(source, out var target))
                {
                    return false;
                }
                if (source == target || sourceToTarget.ContainsKey(target) ||
                    sourceToTarget.TryGetValue(source, out var existingTarget) && existingTarget != target)
                {
                    return false;
                }

                sourceToTarget.TryAdd(source, target);
                mappedVertexCount++;
            }

            return mappedVertexCount == sourceVertices.Count;
        }

        bool TryBuildUvLoopLocalMapping(
            int sourceChain,
            int targetChain,
            UnsafeList<UvQuad> quads,
            UnsafeList<int2> chainEdges,
            UnsafeList<UvLoopChain> chains,
            NativeHashMap<int2, int> edgeToChain,
            NativeHashMap<int, int> localMapping)
        {
            for (var quadIndex = 0; quadIndex < quads.Length; quadIndex++)
            {
                var vertices = quads[quadIndex].Vertices;
                var chainsByEdge = new int4(
                    GetUvLoopChainIndex(vertices.x, vertices.y, chainEdges, chains, edgeToChain),
                    GetUvLoopChainIndex(vertices.y, vertices.z, chainEdges, chains, edgeToChain),
                    GetUvLoopChainIndex(vertices.z, vertices.w, chainEdges, chains, edgeToChain),
                    GetUvLoopChainIndex(vertices.w, vertices.x, chainEdges, chains, edgeToChain));

                if (chainsByEdge.x == sourceChain && chainsByEdge.z == targetChain)
                {
                    if (!TryAddUvLoopVertexMapping(vertices.x, vertices.w, localMapping) ||
                        !TryAddUvLoopVertexMapping(vertices.y, vertices.z, localMapping)) return false;
                }
                else if (chainsByEdge.z == sourceChain && chainsByEdge.x == targetChain)
                {
                    if (!TryAddUvLoopVertexMapping(vertices.z, vertices.y, localMapping) ||
                        !TryAddUvLoopVertexMapping(vertices.w, vertices.x, localMapping)) return false;
                }
                else if (chainsByEdge.y == sourceChain && chainsByEdge.w == targetChain)
                {
                    if (!TryAddUvLoopVertexMapping(vertices.y, vertices.x, localMapping) ||
                        !TryAddUvLoopVertexMapping(vertices.z, vertices.w, localMapping)) return false;
                }
                else if (chainsByEdge.w == sourceChain && chainsByEdge.y == targetChain)
                {
                    if (!TryAddUvLoopVertexMapping(vertices.w, vertices.z, localMapping) ||
                        !TryAddUvLoopVertexMapping(vertices.x, vertices.y, localMapping)) return false;
                }
            }

            return localMapping.Count >= 2;
        }

        static int GetUvLoopChainIndex(
            int vertexA,
            int vertexB,
            UnsafeList<int2> chainEdges,
            UnsafeList<UvLoopChain> chains,
            NativeHashMap<int2, int> edgeToChain)
        {
            var edge = NormalizeUvLoopEdge(vertexA, vertexB);
            if (edgeToChain.TryGetValue(edge, out var chainIndex))
            {
                return chainIndex;
            }

            for (var chain = 0; chain < chains.Length; chain++)
            {
                var chainData = chains[chain];
                for (var edgeOffset = 0; edgeOffset < chainData.EdgeCount; edgeOffset++)
                {
                    if (math.all(chainEdges[chainData.EdgeStart + edgeOffset] == edge))
                    {
                        return chain;
                    }
                }
            }

            return -1;
        }

        static bool TryAddUvLoopVertexMapping(int source, int target, NativeHashMap<int, int> mapping)
        {
            if (mapping.TryGetValue(source, out var existingTarget))
            {
                return existingTarget == target;
            }

            return mapping.TryAdd(source, target);
        }

        void ExpandUvSeamMappings(NativeHashMap<int, int> sourceToTarget)
        {
            var boundsMin = new float3(float.PositiveInfinity);
            var boundsMax = new float3(float.NegativeInfinity);
            for (var vertex = 0; vertex < VertexPositionBuffer.Length; vertex++)
            {
                if (!IsDiscardedVertex(vertex))
                {
                    boundsMin = math.min(boundsMin, VertexPositionBuffer[vertex]);
                    boundsMax = math.max(boundsMax, VertexPositionBuffer[vertex]);
                }
            }

            var positionTolerance = math.max(math.length(boundsMax - boundsMin) * 1e-6f, 1e-7f);
            var inverseTolerance = 1f / positionTolerance;
            using var positionBuckets = new UnsafeParallelMultiHashMap<int, int>(
                math.max(VertexPositionBuffer.Length, 1),
                Allocator.Temp);
            for (var vertex = 0; vertex < VertexPositionBuffer.Length; vertex++)
            {
                if (!IsDiscardedVertex(vertex))
                {
                    positionBuckets.Add(GetUvLoopPositionHash(VertexPositionBuffer[vertex], inverseTolerance), vertex);
                }
            }

            using var additionalMappings = new NativeHashMap<int, int>(math.max(VertexCount, 1), Allocator.Temp);
            var toleranceSquared = positionTolerance * positionTolerance;
            for (var source = 0; source < VertexPositionBuffer.Length; source++)
            {
                if (!sourceToTarget.TryGetValue(source, out var target))
                {
                    continue;
                }

                var sourceHash = GetUvLoopPositionHash(VertexPositionBuffer[source], inverseTolerance);
                foreach (var duplicateSource in positionBuckets.GetValuesForKey(sourceHash))
                {
                    if (duplicateSource == source || sourceToTarget.ContainsKey(duplicateSource) ||
                        math.distancesq(VertexPositionBuffer[source], VertexPositionBuffer[duplicateSource]) > toleranceSquared ||
                        !AreUvLoopVerticesCompatible(source, duplicateSource))
                    {
                        continue;
                    }

                    var duplicateTarget = -1;
                    foreach (var candidateTarget in VertexMergeOpponentVertices.GetValuesForKey(duplicateSource))
                    {
                        if (IsDiscardedVertex(candidateTarget) || sourceToTarget.ContainsKey(candidateTarget) ||
                            math.distancesq(VertexPositionBuffer[target], VertexPositionBuffer[candidateTarget]) > toleranceSquared ||
                            !AreUvLoopVerticesCompatible(target, candidateTarget) ||
                            !IsUvLoopTopologicalEdge(duplicateSource, candidateTarget))
                        {
                            continue;
                        }

                        if (duplicateTarget < 0 || candidateTarget < duplicateTarget)
                        {
                            duplicateTarget = candidateTarget;
                        }
                    }

                    if (duplicateTarget >= 0)
                    {
                        additionalMappings.TryAdd(duplicateSource, duplicateTarget);
                    }
                }
            }

            for (var source = 0; source < VertexPositionBuffer.Length; source++)
            {
                if (additionalMappings.TryGetValue(source, out var target))
                {
                    sourceToTarget.TryAdd(source, target);
                }
            }
        }

        bool AreUvLoopVerticesCompatible(int vertexA, int vertexB)
        {
            if (VertexNormalBuffer.Length == VertexPositionBuffer.Length)
            {
                var normalA = math.normalizesafe(VertexNormalBuffer[vertexA].xyz);
                var normalB = math.normalizesafe(VertexNormalBuffer[vertexB].xyz);
                if (math.dot(normalA, normalB) < UvLoopMinNormalDot)
                {
                    return false;
                }
            }

            if (VertexBlendIndicesBuffer.Length == 0)
            {
                return true;
            }

            var influencesPerVertex = VertexBlendIndicesBuffer.Length / VertexPositionBuffer.Length;
            for (var influence = 0; influence < influencesPerVertex; influence++)
            {
                var indexA = vertexA * influencesPerVertex + influence;
                var indexB = vertexB * influencesPerVertex + influence;
                if (VertexBlendIndicesBuffer[indexA] != VertexBlendIndicesBuffer[indexB] ||
                    math.abs(VertexBlendWeightBuffer[indexA] - VertexBlendWeightBuffer[indexB]) > 0.001f)
                {
                    return false;
                }
            }

            return true;
        }

        bool IsUvLoopTopologicalEdge(int vertexA, int vertexB)
        {
            foreach (var triangleIndex in VertexContainingTriangles.GetValuesForKey(vertexA))
            {
                if (!IsDiscardedTriangle(triangleIndex) && math.any(Triangles[triangleIndex] == vertexB))
                {
                    return true;
                }
            }

            return false;
        }

        static int GetUvLoopPositionHash(float3 position, float inverseTolerance)
        {
            var quantized = (int3)math.round(position * inverseTolerance);
            return (int)math.hash(quantized);
        }

        bool IsUvLoopBatchValid(
            NativeHashMap<int, int> sourceToTarget,
            int targetTriangleCount,
            out int discardedTriangleCount)
        {
            discardedTriangleCount = 0;
            for (var triangleIndex = 0; triangleIndex < Triangles.Length; triangleIndex++)
            {
                if (IsDiscardedTriangle(triangleIndex))
                {
                    continue;
                }

                var originalTriangle = Triangles[triangleIndex];
                var remappedTriangle = new int3(
                    ResolveUvLoopTarget(originalTriangle.x, sourceToTarget),
                    ResolveUvLoopTarget(originalTriangle.y, sourceToTarget),
                    ResolveUvLoopTarget(originalTriangle.z, sourceToTarget));
                if (remappedTriangle.x == remappedTriangle.y ||
                    remappedTriangle.y == remappedTriangle.z ||
                    remappedTriangle.z == remappedTriangle.x)
                {
                    discardedTriangleCount++;
                    continue;
                }

                if (math.all(remappedTriangle == originalTriangle))
                {
                    continue;
                }

                var positionA = VertexPositionBuffer[remappedTriangle.x];
                var positionB = VertexPositionBuffer[remappedTriangle.y];
                var positionC = VertexPositionBuffer[remappedTriangle.z];
                var newCross = math.cross(positionB - positionA, positionC - positionA);
                if (math.lengthsq(newCross) <= 1e-12f)
                {
                    return false;
                }

                var newNormal = math.normalize(newCross);
                if (math.dot(newNormal, TriangleNormals[triangleIndex]) < 0.2f)
                {
                    return false;
                }
            }

            return TriangleCount - discardedTriangleCount >= targetTriangleCount;
        }

        static int ResolveUvLoopTarget(int vertex, NativeHashMap<int, int> sourceToTarget)
        {
            return sourceToTarget.TryGetValue(vertex, out var target) ? target : vertex;
        }

        bool IsDiscardedVertex(int vertexIndex) => DiscardedVertex.IsSet(vertexIndex);

        bool IsDiscardedTriangle(int triangleIndex) => DiscardedTriangle.IsSet(triangleIndex);

        int GetTriangleSubMeshIndex(int triangleIndex)
        {
            var currentTriangle = 0;
            for (var subMeshIndex = 0; subMeshIndex < Mesh.subMeshCount; subMeshIndex++)
            {
                var subMesh = Mesh.GetSubMesh(subMeshIndex);
                if (subMesh.topology != UnityEngine.MeshTopology.Triangles)
                {
                    continue;
                }

                var nextTriangle = currentTriangle + subMesh.indexCount / 3;
                if (triangleIndex < nextTriangle)
                {
                    return subMeshIndex;
                }

                currentTriangle = nextTriangle;
            }

            return -1;
        }

        static int2 NormalizeUvLoopEdge(int vertexA, int vertexB)
        {
            return new int2(math.min(vertexA, vertexB), math.max(vertexA, vertexB));
        }
    }
}
