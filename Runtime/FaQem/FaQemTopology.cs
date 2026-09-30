using Unity.Collections;
using Unity.Mathematics;

namespace Meshia.MeshSimplification
{
    internal static class FaQemTopology
    {
        // Reused for every candidate in a job. Disposing Allocator.Temp
        // collections does not reclaim their storage until the job ends.
        internal struct Workspace : System.IDisposable
        {
            internal NativeHashSet<int> Neighbors, Expected, Incident;
            internal NativeHashSet<int3> Faces;

            internal Workspace(Allocator allocator)
            {
                Neighbors = new NativeHashSet<int>(32, allocator);
                Expected = new NativeHashSet<int>(4, allocator);
                Incident = new NativeHashSet<int>(64, allocator);
                Faces = new NativeHashSet<int3>(64, allocator);
            }

            public void Dispose()
            {
                Neighbors.Dispose(); Expected.Dispose(); Incident.Dispose(); Faces.Dispose();
            }
        }

        internal static int2 CanonicalEdge(int a, int b) => new(math.min(a, b), math.max(a, b));

        internal static bool IsGeometricallyValid(int3 triangle, NativeArray<float3> positions)
        {
            if (triangle.x == triangle.y || triangle.y == triangle.z || triangle.z == triangle.x)
                return false;
            // Widen before the cross product. Computing this in float first can
            // overflow on otherwise finite world-space coordinates and falsely
            // classify a face as invalid.
            var origin = (double3)positions[triangle.x];
            var edge1 = (double3)positions[triangle.y] - origin;
            var edge2 = (double3)positions[triangle.z] - origin;
            var cross = math.cross(edge1, edge2);
            return math.all(math.isfinite(cross)) && math.lengthsq(cross) > 0d;
        }

        internal static int CountIncidentFaces(int2 edge, NativeArray<int3> triangles, NativeBitArray discarded)
        {
            var count = 0;
            for (var i = 0; i < triangles.Length; i++)
            {
                if (discarded.IsSet(i)) continue;
                var t = triangles[i];
                if (math.any(t == edge.x) && math.any(t == edge.y)) count++;
            }
            return count;
        }

        // Local variants are used by the production FA-QEM queue. The
        // containing-triangle map is maintained by ApplyMerge, so these do
        // not turn a candidate pop into a whole-mesh traversal.
        internal static int CountIncidentFacesLocal(int2 edge, NativeArray<int3> triangles, NativeBitArray discarded,
            NativeParallelMultiHashMap<int, int> containing)
        {
            var count = 0;
            // Initialization and ApplyMerge maintain one entry per vertex /
            // triangle pair. Degenerate faces are removed before this path.
            foreach (var ti in containing.GetValuesForKey(edge.x))
            {
                if (discarded.IsSet(ti)) continue;
                var t = triangles[ti];
                if (math.any(t == edge.x) && math.any(t == edge.y)) count++;
            }
            return count;
        }

        internal static bool LinkConditionLocal(int2 edge, NativeArray<int3> triangles, NativeBitArray discarded,
            NativeParallelMultiHashMap<int, int> containing)
        {
            using var workspace = new Workspace(Allocator.TempJob);
            return LinkConditionLocal(edge, triangles, discarded, containing, workspace);
        }

        internal static bool LinkConditionLocal(int2 edge, NativeArray<int3> triangles, NativeBitArray discarded,
            NativeParallelMultiHashMap<int, int> containing, Workspace workspace)
        {
            var incident = CountIncidentFacesLocal(edge, triangles, discarded, containing);
            if (incident < 1 || incident > 2 || HasNonManifoldIncidentEdgeLocal(edge.x, triangles, discarded, containing, workspace.Neighbors) ||
                HasNonManifoldIncidentEdgeLocal(edge.y, triangles, discarded, containing, workspace.Neighbors) ||
                HasDuplicateAfterCollapseLocal(edge, triangles, discarded, containing, workspace)) return false;
            var aNeighbors = workspace.Neighbors;
            var expected = workspace.Expected;
            aNeighbors.Clear(); expected.Clear();
            foreach (var ti in containing.GetValuesForKey(edge.x))
            {
                if (discarded.IsSet(ti)) continue;
                var t = triangles[ti];
                if (!math.any(t == edge.x)) continue;
                for (var j = 0; j < 3; j++)
                    if (t[j] != edge.x) aNeighbors.Add(t[j]);
                if (!math.any(t == edge.y)) continue;
                for (var j = 0; j < 3; j++)
                    if (t[j] != edge.x && t[j] != edge.y) expected.Add(t[j]);
            }
            foreach (var n in aNeighbors)
            {
                if (n == edge.y || expected.Contains(n)) continue;
                var found = false;
                foreach (var ti in containing.GetValuesForKey(edge.y))
                {
                    if (!discarded.IsSet(ti) && math.any(triangles[ti] == edge.y) && math.any(triangles[ti] == n))
                    {
                        found = true;
                        break;
                    }
                }
                // The full oracle rejects a candidate when the opposite
                // endpoint is already connected to this non-expected star
                // neighbor. This is the one-sided link condition; accepting
                // `found` here would allow duplicate/non-simplicial output.
                if (found) return false;
            }
            return true;
        }

        static bool HasNonManifoldIncidentEdgeLocal(int vertex, NativeArray<int3> triangles, NativeBitArray discarded,
            NativeParallelMultiHashMap<int, int> containing, NativeHashSet<int> neighbors)
        {
            neighbors.Clear();
            foreach (var ti in containing.GetValuesForKey(vertex))
            {
                if (discarded.IsSet(ti) || !math.any(triangles[ti] == vertex)) continue;
                var t = triangles[ti];
                for (var j = 0; j < 3; j++) if (t[j] != vertex) neighbors.Add(t[j]);
            }
            foreach (var neighbor in neighbors)
                if (CountIncidentFacesLocal(CanonicalEdge(vertex, neighbor), triangles, discarded, containing) > 2) return true;
            return false;
        }

        internal static bool HasNonManifoldIncidentEdge(int vertex, NativeArray<int3> triangles, NativeBitArray discarded)
        {
            using var neighbors = new NativeHashSet<int>(16, Allocator.Temp);
            for (var i = 0; i < triangles.Length; i++)
            {
                if (discarded.IsSet(i) || !math.any(triangles[i] == vertex)) continue;
                var t = triangles[i];
                for (var j = 0; j < 3; j++) if (t[j] != vertex) neighbors.Add(t[j]);
            }
            foreach (var neighbor in neighbors)
                if (CountIncidentFaces(CanonicalEdge(vertex, neighbor), triangles, discarded) > 2) return true;
            return false;
        }

        internal static bool LinkCondition(int2 edge, NativeArray<int3> triangles, NativeBitArray discarded)
        {
            var incident = CountIncidentFaces(edge, triangles, discarded);
            if (incident < 1 || incident > 2 || HasNonManifoldIncidentEdge(edge.x, triangles, discarded) ||
                HasNonManifoldIncidentEdge(edge.y, triangles, discarded) ||
                HasDuplicateAfterCollapse(edge, triangles, discarded)) return false;
            using var aNeighbors = new NativeHashSet<int>(16, Allocator.Temp);
            using var expected = new NativeHashSet<int>(2, Allocator.Temp);
            for (var i = 0; i < triangles.Length; i++)
            {
                if (discarded.IsSet(i)) continue;
                var t = triangles[i];
                if (math.any(t == edge.x))
                    for (var j = 0; j < 3; j++) if (t[j] != edge.x) aNeighbors.Add(t[j]);
                if (math.any(t == edge.x) && math.any(t == edge.y))
                    for (var j = 0; j < 3; j++) if (t[j] != edge.x && t[j] != edge.y) expected.Add(t[j]);
            }
            foreach (var n in aNeighbors)
            {
                if (n == edge.y || expected.Contains(n)) continue;
                for (var i = 0; i < triangles.Length; i++)
                    if (!discarded.IsSet(i) && math.any(triangles[i] == edge.y) && math.any(triangles[i] == n)) return false;
            }
            return true;
        }

        static bool HasDuplicateAfterCollapse(int2 edge, NativeArray<int3> triangles, NativeBitArray discarded)
        {
            using var seen = new NativeHashSet<int3>(math.max(4, triangles.Length), Allocator.Temp);
            for (var i = 0; i < triangles.Length; i++)
            {
                if (discarded.IsSet(i)) continue;
                var t = triangles[i];
                t = math.select(t, new int3(edge.x), t == edge.y);
                if (t.x == t.y || t.y == t.z || t.z == t.x) continue;
                var key = t;
                if (key.x > key.y) { var v = key.x; key.x = key.y; key.y = v; }
                if (key.y > key.z) { var v = key.y; key.y = key.z; key.z = v; }
                if (key.x > key.y) { var v = key.x; key.x = key.y; key.y = v; }
                if (!seen.Add(key)) return true;
            }
            return false;
        }

        static bool HasDuplicateAfterCollapseLocal(int2 edge, NativeArray<int3> triangles, NativeBitArray discarded,
            NativeParallelMultiHashMap<int, int> containing, Workspace workspace)
        {
            var incident = workspace.Incident;
            var seen = workspace.Faces;
            incident.Clear(); seen.Clear();
            foreach (var ti in containing.GetValuesForKey(edge.x)) incident.Add(ti);
            foreach (var ti in containing.GetValuesForKey(edge.y)) incident.Add(ti);
            foreach (var i in incident)
            {
                if (discarded.IsSet(i)) continue;
                var t = math.select(triangles[i], new int3(edge.x), triangles[i] == edge.y);
                if (t.x == t.y || t.y == t.z || t.z == t.x) continue;
                var key = t;
                if (key.x > key.y) { var v = key.x; key.x = key.y; key.y = v; }
                if (key.y > key.z) { var v = key.y; key.y = key.z; key.z = v; }
                if (key.x > key.y) { var v = key.x; key.x = key.y; key.y = v; }
                if (!seen.Add(key)) return true;
            }
            return false;
        }
    }
}
