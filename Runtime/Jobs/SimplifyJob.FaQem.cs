using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Meshia.MeshSimplification
{
    partial struct SimplifyJob
    {
        public NativeList<FaQemCollapseRecord> FaQemCollapseHistory;
        public NativeList<FaQemAffectedFace> FaQemAffectedFaces;
        public bool RecordFaQemHistory;
        public NativeList<int> FaQemTriangleCounts;
        public bool RecordFaQemCounts;

        const byte FaQemDeformableDegenerateVertex = 2;
        const byte FaQemJointTransitionVertex = 4;

        struct FaQemCandidate : IComparable<FaQemCandidate>
        {
            public int A, B;
            public int RevisionA, RevisionB;
            public float3 Position;
            public double Cost;

            public readonly int CompareTo(FaQemCandidate other)
            {
                var byCost = Cost.CompareTo(other.Cost);
                if (byCost != 0) return byCost;
                var byA = A.CompareTo(other.A);
                return byA != 0 ? byA : B.CompareTo(other.B);
            }
        }

        internal void RunFaQem(int targetTriangleCount)
        {
            targetTriangleCount = math.max(0, targetTriangleCount);
            if (RecordFaQemCounts) FaQemTriangleCounts.Add(TriangleCount);
            if (TriangleCount <= targetTriangleCount || VertexCount == 0) return;

            var settings = Options.FaQem.Effective;
            using var sourceQuadrics = new NativeArray<FaQemQuadric>(VertexPositionBuffer.Length, Allocator.Temp);
            if (!TryGetFaQemNormalization(out var center, out var scale)) return;
            using var seamFlags = new NativeArray<byte>(VertexPositionBuffer.Length, Allocator.Temp, NativeArrayOptions.ClearMemory);
            var hasDeformation = BlendShapes.Length > 0 || VertexBlendWeightBuffer.Length > 0;
            for (var ti = 0; ti < Triangles.Length; ti++)
                if (!IsDiscardedTriangle(ti) && !FaQemTopology.IsGeometricallyValid(Triangles[ti], VertexPositionBuffer))
                {
                    var degenerate = Triangles[ti];
                    // A flat rest-pose face can open under a blend shape or skinning.
                    // Keep its vertices fixed even when optional seam/border guards are off.
                    if (hasDeformation && degenerate.x != degenerate.y &&
                        degenerate.y != degenerate.z && degenerate.z != degenerate.x)
                    {
                        seamFlags.ElementAt(degenerate.x) |= FaQemDeformableDegenerateVertex;
                        seamFlags.ElementAt(degenerate.y) |= FaQemDeformableDegenerateVertex;
                        seamFlags.ElementAt(degenerate.z) |= FaQemDeformableDegenerateVertex;
                        continue;
                    }
                    VertexContainingTriangles.Remove(degenerate.x, ti);
                    VertexContainingTriangles.Remove(degenerate.y, ti);
                    VertexContainingTriangles.Remove(degenerate.z, ti);
                    if (!VertexContainingTriangles.ContainsKey(degenerate.x)) DiscardVertex(degenerate.x);
                    if (!VertexContainingTriangles.ContainsKey(degenerate.y)) DiscardVertex(degenerate.y);
                    if (!VertexContainingTriangles.ContainsKey(degenerate.z)) DiscardVertex(degenerate.z);
                    DiscardTriangle(ti);
                }
            if (RecordFaQemCounts) FaQemTriangleCounts.Add(TriangleCount);
            InitializeFaQemJointTransitions(seamFlags);
            InitializeFaQemSourceQuadrics(sourceQuadrics, center, scale, settings);
            using var envelope = new FaQemSurfaceEnvelope(VertexPositionBuffer, Triangles, DiscardedTriangle,
                center, scale, settings.MaxSurfaceDeviation);
            InitializeFaQemSeamFlags(seamFlags, settings.PreserveAttributeSeams, scale);
            UseFaQem = true;
            using var revisions = new NativeArray<int>(VertexPositionBuffer.Length, Allocator.Temp, NativeArrayOptions.ClearMemory);
            // The queue can grow as stale candidates await removal. Reclaim
            // replaced backing buffers immediately rather than at job end.
            using var queue = new NativeMinPriorityQueue<FaQemCandidate>(math.max(1, TriangleCount * 3), Allocator.Persistent);
            using var topologyWorkspace = new FaQemTopology.Workspace(Allocator.Temp);
            using var affectedVertices = new NativeHashSet<int>(64, Allocator.Temp);
            using var affectedEdges = new NativeHashSet<int2>(384, Allocator.Temp);
            BuildFaQemCandidates(queue, sourceQuadrics, revisions, seamFlags, center, scale, settings, envelope);
            while (TriangleCount > targetTriangleCount && queue.TryDequeue(out var candidate))
            {
                    if (IsDiscardedVertex(candidate.A) || IsDiscardedVertex(candidate.B) ||
                        candidate.RevisionA != revisions[candidate.A] || candidate.RevisionB != revisions[candidate.B] ||
                        !FaQemTopology.LinkConditionLocal(new int2(candidate.A, candidate.B), Triangles, DiscardedTriangle, VertexContainingTriangles, topologyWorkspace) ||
                        IsFaQemProtected(candidate.A, candidate.B, seamFlags, settings) ||
                        !IsSkinningCollapseValid(candidate.A, candidate.B, candidate.Position, out _, (float)scale) ||
                        !IsFaQemPlacementValid(candidate, settings, center, scale, envelope)) continue;

                    RecordFaQemCollapse(candidate);
                    var survivor = candidate.A;
                    var removed = candidate.B;
                    affectedVertices.Clear();
                    CollectFaQemStarVertices(survivor, affectedVertices);
                    CollectFaQemStarVertices(removed, affectedVertices);
                    ApplyMerge(new VertexMerge
                    {
                        VertexAIndex = survivor,
                        VertexBIndex = removed,
                        VertexAVersion = VertexVersions[survivor],
                        VertexBVersion = VertexVersions[removed],
                        Position = candidate.Position,
                        Cost = (float)math.min(candidate.Cost, float.MaxValue),
                     });
                     if (RecordFaQemCounts) FaQemTriangleCounts.Add(TriangleCount);
                     sourceQuadrics.ElementAt(survivor) = sourceQuadrics[survivor] + sourceQuadrics[removed];
                     CollectFaQemStarVertices(survivor, affectedVertices);
                     // Revisions must be advanced for the complete change set
                     // before any replacement candidates are queued. Queueing
                     // during the first pass creates entries that become
                     // immediately stale when a later affected vertex is
                     // advanced, and repeats the same edge many times.
                     foreach (var affectedVertex in affectedVertices)
                     {
                         if (IsDiscardedVertex(affectedVertex)) continue;
                         revisions.ElementAt(affectedVertex)++;
                     }
                     affectedEdges.Clear();
                     foreach (var affectedVertex in affectedVertices)
                         CollectFaQemOneRingEdges(affectedEdges, affectedVertex);
                     foreach (var edge in affectedEdges)
                         EnqueueFaQemCandidate(queue, sourceQuadrics, revisions, seamFlags, edge, center, scale, settings, envelope);
            }
            UseFaQem = false;
        }

        readonly bool TryGetFaQemNormalization(out double3 center, out double scale)
        {
            var min = new double3(double.PositiveInfinity);
            var max = new double3(double.NegativeInfinity);
            for (var i = 0; i < VertexPositionBuffer.Length; i++)
            {
                if (IsDiscardedVertex(i)) continue;
                var p = (double3)VertexPositionBuffer[i];
                if (!math.all(math.isfinite(p))) { center = default; scale = 0d; return false; }
                min = math.min(min, p); max = math.max(max, p);
            }
            center = (min + max) * 0.5d;
            scale = math.length(max - min);
            if (!math.isfinite(scale) || scale <= 1e-15) scale = 1d;
            return math.all(math.isfinite(center));
        }

        void InitializeFaQemSourceQuadrics(NativeArray<FaQemQuadric> quadrics, double3 center, double scale, FaQemOptions settings)
        {
            using var fallbackNormals = new NativeArray<double3>(quadrics.Length, Allocator.Temp, NativeArrayOptions.ClearMemory);
            for (var triangleIndex = 0; triangleIndex < Triangles.Length; triangleIndex++)
            {
                if (IsDiscardedTriangle(triangleIndex) || !FaQemTopology.IsGeometricallyValid(Triangles[triangleIndex], VertexPositionBuffer)) continue;
                var t = Triangles[triangleIndex];
                var a = ((double3)VertexPositionBuffer[t.x] - center) / scale;
                var b = ((double3)VertexPositionBuffer[t.y] - center) / scale;
                var c = ((double3)VertexPositionBuffer[t.z] - center) / scale;
                var cross = math.cross(b - a, c - a);
                var twiceArea = math.length(cross);
                if (!math.isfinite(twiceArea) || twiceArea <= FaQemMath.AreaEpsilon) continue;
                var normal = cross / twiceArea;
                var area = 0.5d * twiceArea;
                var planeWeight = settings.UseInverseAreaWeighting
                    ? 1d / (settings.PlaneAreaWeight * math.max(area, FaQemMath.AreaEpsilon))
                    : settings.PlaneAreaWeight;
                var q = FaQemQuadric.Plane(normal, a, planeWeight);
                quadrics[t.x] += q; quadrics[t.y] += q; quadrics[t.z] += q;
                fallbackNormals.ElementAt(t.x) += cross;
                fallbackNormals.ElementAt(t.y) += cross;
                fallbackNormals.ElementAt(t.z) += cross;
            }
            if (settings.NormalWeight > 0d)
            {
                // Eq. 10 is a source-vertex term, so add one tangent-plane
                // quadric per vertex rather than multiplying it by valence.
                for (var vertex = 0; vertex < quadrics.Length; vertex++)
                {
                    if (IsDiscardedVertex(vertex)) continue;
                    var normal = fallbackNormals[vertex];
                    if (VertexNormalBuffer.Length == quadrics.Length)
                    {
                        var sourceNormal = (double3)VertexNormalBuffer[vertex].xyz;
                        if (math.all(math.isfinite(sourceNormal)) && math.lengthsq(sourceNormal) > FaQemMath.AreaEpsilon)
                            normal = sourceNormal;
                    }
                    normal = math.normalizesafe(normal);
                    if (math.lengthsq(normal) > FaQemMath.AreaEpsilon)
                    {
                        var point = ((double3)VertexPositionBuffer[vertex] - center) / scale;
                        quadrics[vertex] += FaQemQuadric.Plane(normal, point, settings.NormalWeight);
                    }
                }
            }
            AddFaQemSourceBoundaryQuadrics(quadrics, center, scale, settings.BoundaryWeight);
        }

        void AddFaQemSourceBoundaryQuadrics(NativeArray<FaQemQuadric> quadrics, double3 center, double scale, double weight)
        {
            if (weight <= 0d) return;
            using var incidence = new NativeHashMap<int2, int>(math.max(1, TriangleCount * 3), Allocator.Temp);
            for (var ti = 0; ti < Triangles.Length; ti++)
            {
                if (IsDiscardedTriangle(ti)) continue;
                var t = Triangles[ti];
                IncrementFaQemIncidence(incidence, FaQemTopology.CanonicalEdge(t.x, t.y));
                IncrementFaQemIncidence(incidence, FaQemTopology.CanonicalEdge(t.y, t.z));
                IncrementFaQemIncidence(incidence, FaQemTopology.CanonicalEdge(t.z, t.x));
            }
            using var boundaryNeighbors = new NativeParallelMultiHashMap<int, int>(math.max(1, incidence.Count * 2), Allocator.Temp);
            foreach (var pair in incidence)
            {
                if (pair.Value != 1) continue;
                boundaryNeighbors.Add(pair.Key.x, pair.Key.y);
                boundaryNeighbors.Add(pair.Key.y, pair.Key.x);
            }
            using var neighbors = new NativeHashSet<int>(8, Allocator.Temp);
            for (var v = 0; v < quadrics.Length; v++)
            {
                if (IsDiscardedVertex(v)) continue;
                var first = int.MaxValue;
                var second = int.MaxValue;
                neighbors.Clear();
                foreach (var neighbor in boundaryNeighbors.GetValuesForKey(v))
                {
                    if (!neighbors.Add(neighbor)) continue;
                    if (neighbor < first) { second = first; first = neighbor; }
                    else if (neighbor < second) second = neighbor;
                }
                // Equation 6-9 is defined for the two ordered boundary
                // neighbors at a vertex. Non-manifold boundary fans with
                // three or more neighbors have no unambiguous stencil.
                if (neighbors.Count != 2 || first == int.MaxValue || second == int.MaxValue) continue;
                var v1 = ((double3)VertexPositionBuffer[v] - center) / scale;
                var v2 = ((double3)VertexPositionBuffer[first] - center) / scale;
                var v3 = ((double3)VertexPositionBuffer[second] - center) / scale;
                var delta1 = v3 - v2;
                var length = math.length(delta1);
                if (!math.isfinite(length) || length <= 1e-12) continue;
                var delta2 = v3 - 2d * v1 + v2;
                var curvature = math.length(math.cross(delta1, delta2)) / (length * length * length);
                if (!math.isfinite(curvature) || curvature <= 0d) continue;
                var n1 = math.cross(v1 - v2, v3 - v1); // Equation 7, deliberately unnormalized.
                var d = v1 - v2;                       // Equation 8, deliberately unnormalized.
                var scaledWeight = weight * curvature;
                quadrics[v] += FaQemQuadric.Plane(n1, v1, scaledWeight) + FaQemQuadric.Plane(d, v1, scaledWeight);
            }
        }

        static void IncrementFaQemIncidence(NativeHashMap<int2, int> incidence, int2 edge)
        {
            if (incidence.TryGetValue(edge, out var count)) incidence[edge] = count + 1;
            else incidence.Add(edge, 1);
        }

        void AddFaQemNormalQuadric(NativeArray<FaQemQuadric> quadrics, int vertex, double3 point,
            double3 geometricFallback, double weight)
        {
            double3 normal = VertexNormalBuffer.Length == quadrics.Length ? VertexNormalBuffer[vertex].xyz : geometricFallback;
            if (!math.all(math.isfinite(normal)) || math.lengthsq(normal) <= FaQemMath.AreaEpsilon) normal = geometricFallback;
            normal = math.normalizesafe(normal);
            if (math.lengthsq(normal) > FaQemMath.AreaEpsilon)
                quadrics[vertex] += FaQemQuadric.Plane(normal, point, weight);
        }

        void BuildFaQemCandidates(NativeMinPriorityQueue<FaQemCandidate> queue, NativeArray<FaQemQuadric> sourceQuadrics,
            NativeArray<int> revisions, NativeArray<byte> seamFlags,
            double3 center, double scale, FaQemOptions settings, FaQemSurfaceEnvelope envelope)
        {
            using var edges = new NativeHashSet<int2>(math.max(1, TriangleCount * 3), Allocator.Temp);
            for (var i = 0; i < Triangles.Length; i++)
            {
                if (IsDiscardedTriangle(i)) continue;
                var t = Triangles[i];
                edges.Add(FaQemTopology.CanonicalEdge(t.x, t.y));
                edges.Add(FaQemTopology.CanonicalEdge(t.y, t.z));
                edges.Add(FaQemTopology.CanonicalEdge(t.z, t.x));
            }
            foreach (var edge in edges)
            {
                EnqueueFaQemCandidate(queue, sourceQuadrics, revisions, seamFlags, edge, center, scale, settings, envelope);
            }
        }

        void CollectFaQemOneRingEdges(NativeHashSet<int2> edges, int vertex)
        {
            foreach (var ti in VertexContainingTriangles.GetValuesForKey(vertex))
            {
                if (IsDiscardedTriangle(ti)) continue;
                var t = Triangles[ti];
                edges.Add(FaQemTopology.CanonicalEdge(t.x, t.y));
                edges.Add(FaQemTopology.CanonicalEdge(t.y, t.z));
                edges.Add(FaQemTopology.CanonicalEdge(t.z, t.x));
            }
        }

        void CollectFaQemStarVertices(int vertex, NativeHashSet<int> result)
        {
            foreach (var ti in VertexContainingTriangles.GetValuesForKey(vertex))
            {
                if (IsDiscardedTriangle(ti)) continue;
                var triangle = Triangles[ti];
                result.Add(triangle.x);
                result.Add(triangle.y);
                result.Add(triangle.z);
            }
        }

        void EnqueueFaQemCandidate(NativeMinPriorityQueue<FaQemCandidate> queue, NativeArray<FaQemQuadric> sourceQuadrics,
            NativeArray<int> revisions, NativeArray<byte> seamFlags, int2 edge, double3 center, double scale, FaQemOptions settings,
            FaQemSurfaceEnvelope envelope)
        {
            if (edge.x == edge.y || IsDiscardedVertex(edge.x) || IsDiscardedVertex(edge.y) ||
                IsFaQemProtected(edge.x, edge.y, seamFlags, settings)) return;
            // Topology is validated when a current candidate is popped.
            var q = sourceQuadrics[edge.x] + sourceQuadrics[edge.y];
            var a = ((double3)VertexPositionBuffer[edge.x] - center) / scale;
            var b = ((double3)VertexPositionBuffer[edge.y] - center) / scale;
            if (!FaQemMath.IsCollapsibleEdge(a, b)) return;
            var optimal = q.TrySolve(out var solved) ? solved : FaQemMath.SelectFiniteMinimum(q, a, b);
            var best = new FaQemCandidate { Cost = double.PositiveInfinity };
            // Keep the paper's optimal placement when valid. If the optional
            // envelope rejects it, try endpoints and midpoint before giving up.
            // Queue the alternative at its actual cost, not the rejected cost.
            for (var attempt = 0; attempt < (settings.MaxSurfaceDeviation > 0f ? 4 : 1); attempt++)
            {
                var position = attempt == 0 ? optimal : attempt == 1 ? a : attempt == 2 ? b : (a + b) * 0.5d;
                var cost = q.Evaluate(position);
                if (settings.AreaWeight > 0d && !Options.PreserveBorderEdges)
                    cost += settings.AreaWeight * ComputeFaQemAreaCost(edge, position, center, scale);
                var world = (float3)(position * scale + center);
                if (!IsSkinningCollapseValid(edge.x, edge.y, world, out var skinningPenalty, (float)scale)) continue;
                if (Options.SkinningProtection.Enabled)
                    cost += Options.SkinningProtection.Strength * skinningPenalty * skinningPenalty;
                if (!math.isfinite(cost) || !math.all(math.isfinite(world))) continue;
                var candidate = new FaQemCandidate { A = edge.x, B = edge.y, RevisionA = revisions[edge.x], RevisionB = revisions[edge.y], Position = world, Cost = math.max(0d, cost) };
                if (settings.MaxSurfaceDeviation > 0f && !IsFaQemPlacementValid(candidate, settings, center, scale, envelope)) continue;
                if (candidate.Cost < best.Cost) best = candidate;
                if (attempt == 0) break;
            }
            if (math.isfinite(best.Cost)) queue.Enqueue(best);
        }

        readonly double ComputeFaQemAreaCost(int2 candidate, double3 position, double3 center, double scale)
        {
            return ComputeFaQemStarAreaCost(candidate, candidate.x, -1, position, center, scale) +
                   ComputeFaQemStarAreaCost(candidate, candidate.y, candidate.x, position, center, scale);
        }

        readonly double ComputeFaQemStarAreaCost(int2 candidate, int vertex, int skipVertex,
            double3 position, double3 center, double scale)
        {
            var result = 0d;
            // Visit each incident face once. An open boundary edge belongs to
            // only one face, so it needs no additional edge deduplication set.
            foreach (var ti in VertexContainingTriangles.GetValuesForKey(vertex))
            {
                if (IsDiscardedTriangle(ti)) continue;
                var t = Triangles[ti];
                if (skipVertex >= 0 && math.any(t == skipVertex)) continue;
                for (var j = 0; j < 3; j++)
                {
                    var edge = FaQemTopology.CanonicalEdge(t[j], t[(j + 1) % 3]);
                    if (edge.x != candidate.x && edge.x != candidate.y && edge.y != candidate.x && edge.y != candidate.y) continue;
                    if (FaQemTopology.CountIncidentFacesLocal(edge, Triangles, DiscardedTriangle, VertexContainingTriangles) != 1) continue;
                    var a = ((double3)VertexPositionBuffer[edge.x] - center) / scale;
                    var b = ((double3)VertexPositionBuffer[edge.y] - center) / scale;
                    result += FaQemMath.AreaEdgeCost(a, b, position);
                }
            }
            return result;
        }

        void InitializeFaQemJointTransitions(NativeArray<byte> flags)
        {
            if (!Options.SkinningProtection.PreserveJointTransitions || VertexBlendIndicesBuffer.Length == 0) return;
            var dimension = VertexBlendIndicesBuffer.Length / VertexPositionBuffer.Length;
            if (dimension == 0 || VertexBlendWeightBuffer.Length != VertexBlendIndicesBuffer.Length) return;
            using var dominant = new NativeArray<int>(flags.Length, Allocator.Temp);
            for (var vertex = 0; vertex < flags.Length; vertex++)
            {
                dominant.ElementAt(vertex) = SkinningCollapseMetrics.DominantBone(
                    VertexBlendIndicesBuffer.AsSpan().Slice(vertex * dimension, dimension),
                    VertexBlendWeightBuffer.AsSpan().Slice(vertex * dimension, dimension));
            }
            for (var ti = 0; ti < Triangles.Length; ti++)
            {
                if (IsDiscardedTriangle(ti)) continue;
                var triangle = Triangles[ti];
                for (var edge = 0; edge < 3; edge++)
                {
                    var a = triangle[edge];
                    var b = triangle[(edge + 1) % 3];
                    if (dominant[a] < 0 || dominant[b] < 0 || dominant[a] == dominant[b]) continue;
                    var selected = Options.SkinningProtection.JointProtectionBoneIndices;
                    if (selected.Length > 0 && !selected.Contains(dominant[a]) && !selected.Contains(dominant[b])) continue;
                    flags.ElementAt(a) |= FaQemJointTransitionVertex;
                    flags.ElementAt(b) |= FaQemJointTransitionVertex;
                }
            }
            // Freeze only one source support ring: do not grow this region as collapses
            // proceed, or the whole rigid segment would eventually become protected.
            using var transitionFlags = new NativeArray<byte>(flags, Allocator.Temp);
            for (var ti = 0; ti < Triangles.Length; ti++)
            {
                if (IsDiscardedTriangle(ti)) continue;
                var t = Triangles[ti];
                if (((transitionFlags[t.x] | transitionFlags[t.y] | transitionFlags[t.z]) & FaQemJointTransitionVertex) == 0) continue;
                flags.ElementAt(t.x) |= FaQemJointTransitionVertex;
                flags.ElementAt(t.y) |= FaQemJointTransitionVertex;
                flags.ElementAt(t.z) |= FaQemJointTransitionVertex;
            }
        }

        readonly bool IsFaQemProtected(int a, int b, NativeArray<byte> seamFlags, FaQemOptions settings)
        {
            if (((seamFlags[a] | seamFlags[b]) & (FaQemDeformableDegenerateVertex | FaQemJointTransitionVertex)) != 0) return true;
            if (IsFaQemPreservedBoundary(a) || IsFaQemPreservedBoundary(b)) return true;
            if (VertexContainingSubMeshIndices.Length == VertexPositionBuffer.Length && VertexContainingSubMeshIndices[a] != VertexContainingSubMeshIndices[b]) return true;
            return settings.PreserveAttributeSeams && (IsFaQemSeamVertex(a, seamFlags) || IsFaQemSeamVertex(b, seamFlags));
        }

        readonly bool IsFaQemPreservedBoundary(int vertex)
        {
            if (!Options.PreserveBorderEdges &&
                (VertexBlendIndicesBuffer.Length == 0 || PreserveBorderEdgesBoneIndices.Length == 0)) return false;
            var boundary = false;
            foreach (var ti in VertexContainingTriangles.GetValuesForKey(vertex))
            {
                if (IsDiscardedTriangle(ti)) continue;
                var t = Triangles[ti];
                for (var j = 0; j < 3; j++)
                {
                    if (t[j] != vertex) continue;
                    var edge = FaQemTopology.CanonicalEdge(vertex, t[(j + 1) % 3]);
                    if (FaQemTopology.CountIncidentFacesLocal(edge, Triangles, DiscardedTriangle, VertexContainingTriangles) == 1) boundary = true;
                    edge = FaQemTopology.CanonicalEdge(vertex, t[(j + 2) % 3]);
                    if (FaQemTopology.CountIncidentFacesLocal(edge, Triangles, DiscardedTriangle, VertexContainingTriangles) == 1) boundary = true;
                }
            }
            if (!boundary) return false;
            if (Options.PreserveBorderEdges) return true;
            if (VertexBlendIndicesBuffer.Length == 0 || PreserveBorderEdgesBoneIndices.Length == 0) return false;
            var boneCount = VertexBlendIndicesBuffer.Length / VertexPositionBuffer.Length;
            for (var i = 0; i < boneCount; i++)
                if (PreserveBorderEdgesBoneIndices.IsSet((int)VertexBlendIndicesBuffer[vertex * boneCount + i])) return true;
            return false;
        }

        readonly bool IsFaQemSeamVertex(int vertex, NativeArray<byte> seamFlags)
        {
            return seamFlags.Length == VertexPositionBuffer.Length && seamFlags[vertex] != 0;
        }

        void InitializeFaQemSeamFlags(NativeArray<byte> flags, bool enabled, double scale)
        {
            if (!enabled) return;
            var tolerance = math.max(1e-7d, scale * 1e-6d);
            using var buckets = new NativeParallelMultiHashMap<int3, int>(math.max(1, VertexPositionBuffer.Length), Allocator.Temp);
            for (var vertex = 0; vertex < VertexPositionBuffer.Length; vertex++)
                buckets.Add((int3)math.floor((double3)VertexPositionBuffer[vertex] / tolerance), vertex);
            for (var vertex = 0; vertex < VertexPositionBuffer.Length; vertex++)
            {
                var p = (double3)VertexPositionBuffer[vertex];
                var key = (int3)math.floor(p / tolerance);
                for (var dx = -1; dx <= 1; dx++) for (var dy = -1; dy <= 1; dy++) for (var dz = -1; dz <= 1; dz++)
                    foreach (var other in buckets.GetValuesForKey(key + new int3(dx, dy, dz)))
                    {
                        if (other <= vertex || IsDiscardedVertex(other) || math.lengthsq((double3)VertexPositionBuffer[other] - p) > tolerance * tolerance) continue;
                        // Separate indices can stitch disconnected face fans even
                        // when every appearance attribute matches. FA-QEM does
                        // not collapse those fans together, so moving either
                        // copy independently opens a crack. Keep both fixed.
                        flags.ElementAt(vertex) |= 1;
                        flags.ElementAt(other) |= 1;
                    }
            }
        }

        readonly bool IsFaQemPlacementValid(FaQemCandidate candidate, FaQemOptions settings, double3 center, double scale,
            FaQemSurfaceEnvelope envelope)
        {
            if (!math.all(math.isfinite(candidate.Position))) return false;
            if (!envelope.Contains(((double3)candidate.Position - center) / scale)) return false;
            var merge = new VertexMerge { VertexAIndex = candidate.A, VertexBIndex = candidate.B, Position = candidate.Position };
            return !WillFaQemFlip(merge, candidate.A, candidate.B, settings.MinNormalDot, center, scale, envelope) &&
                   !WillFaQemFlip(merge, candidate.B, candidate.A, settings.MinNormalDot, center, scale, envelope);
        }

        readonly bool WillFaQemFlip(VertexMerge merge, int vertex, int opponent, float minNormalDot, double3 center, double scale,
            FaQemSurfaceEnvelope envelope)
        {
            foreach (var ti in VertexContainingTriangles.GetValuesForKey(vertex))
            {
                if (IsDiscardedTriangle(ti)) continue;
                var t = Triangles[ti];
                if (math.any(t == opponent)) continue;
                var i1 = t.x == vertex ? t.y : t.y == vertex ? t.z : t.x;
                var i2 = t.x == vertex ? t.z : t.y == vertex ? t.x : t.y;
                var origin = ((double3)VertexPositionBuffer[vertex] - center) / scale;
                var p1 = ((double3)VertexPositionBuffer[i1] - center) / scale;
                var p2 = ((double3)VertexPositionBuffer[i2] - center) / scale;
                var merged = ((double3)merge.Position - center) / scale;
                var oldCross = math.cross(p1 - origin, p2 - origin);
                var newCross = math.cross(p1 - merged, p2 - merged);
                if (!math.all(math.isfinite(newCross)) || math.lengthsq(newCross) <= 1e-24d) return true;
                if (math.dot(math.normalizesafe(oldCross), math.normalizesafe(newCross)) < minNormalDot) return true;
                // Checking only the replacement vertex misses new triangles
                // that cut across a curved source surface. This is a sampled
                // one-sided envelope, not a continuous collision guarantee.
                if (!envelope.Contains((merged + p1) * 0.5d) ||
                    !envelope.Contains((merged + p2) * 0.5d) ||
                    !envelope.Contains((p1 + p2) * 0.5d) ||
                    !envelope.Contains((merged + p1 + p2) / 3d)) return true;
            }
            return false;
        }

        void RecordFaQemCollapse(FaQemCandidate candidate)
        {
            if (!RecordFaQemHistory) return;
            var start = FaQemAffectedFaces.Length;
            using var faceSet = new NativeHashSet<int>(32, Allocator.TempJob);
            foreach (var ti in VertexContainingTriangles.GetValuesForKey(candidate.A)) faceSet.Add(ti);
            foreach (var ti in VertexContainingTriangles.GetValuesForKey(candidate.B)) faceSet.Add(ti);
            using var faces = new NativeList<int>(faceSet.Count, Allocator.TempJob);
            foreach (var ti in faceSet) if (!IsDiscardedTriangle(ti)) faces.Add(ti);
            for (var i = 1; i < faces.Length; i++)
            {
                var value = faces[i];
                var j = i - 1;
                while (j >= 0 && faces[j] > value) { faces.ElementAt(j + 1) = faces[j]; j--; }
                faces.ElementAt(j + 1) = value;
            }
            foreach (var ti in faces)
            {
                var t = Triangles[ti];
                FaQemAffectedFaces.Add(new FaQemAffectedFace { OriginalTriangleIndex = ti, SubMeshIndex = FindFaQemSubMesh(t), VerticesBefore = t });
            }
            FaQemCollapseHistory.Add(new FaQemCollapseRecord
            {
                RemovedVertex = candidate.B, SurvivorVertex = candidate.A,
                RemovedPosition = VertexPositionBuffer[candidate.B], SurvivorPositionBefore = VertexPositionBuffer[candidate.A],
                SurvivorPositionAfter = candidate.Position, IncidentFaceStart = start,
                IncidentFaceCount = FaQemAffectedFaces.Length - start,
            });
        }

        readonly int FindFaQemSubMesh(int3 triangle)
        {
            if (VertexContainingSubMeshIndices.Length != VertexPositionBuffer.Length) return -1;
            var mask = VertexContainingSubMeshIndices[triangle.x] | VertexContainingSubMeshIndices[triangle.y] | VertexContainingSubMeshIndices[triangle.z];
            if (mask != 0 && (mask & (mask - 1)) == 0)
                for (var i = 0; i < 32; i++) if ((mask & (1u << i)) != 0) return i;
            // A vertex mask cannot identify a triangle when records overlap
            // multiple submeshes. Preserve the ambiguity instead of assigning
            // a deterministic but incorrect slot.
            return -1;
        }
    }
}
