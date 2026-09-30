using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace Meshia.MeshSimplification
{
    // Immutable source surface in normalized coordinates. The balanced BVH
    // keeps optional envelope checks local even on large input meshes.
    internal struct FaQemSurfaceEnvelope : IDisposable
    {
        struct Node
        {
            internal double3 Min, Max;
            internal int Start, Count, Left, Right;
        }

        struct CentroidComparer : IComparer<int3>
        {
            internal NativeArray<double3> Positions;
            internal int Axis;
            public int Compare(int3 a, int3 b)
            {
                var ca = Positions[a.x][Axis] + Positions[a.y][Axis] + Positions[a.z][Axis];
                var cb = Positions[b.x][Axis] + Positions[b.y][Axis] + Positions[b.z][Axis];
                var order = ca.CompareTo(cb);
                if (order != 0) return order;
                order = a.x.CompareTo(b.x);
                if (order != 0) return order;
                order = a.y.CompareTo(b.y);
                return order != 0 ? order : a.z.CompareTo(b.z);
            }
        }

        NativeArray<double3> positions;
        NativeList<int3> triangles;
        NativeList<Node> nodes;
        double toleranceSquared;

        internal FaQemSurfaceEnvelope(NativeArray<float3> sourcePositions, NativeArray<int3> sourceTriangles,
            NativeBitArray discarded, double3 center, double scale, double tolerance)
        {
            this = default;
            if (tolerance <= 0d) return;
            toleranceSquared = tolerance * tolerance;
            positions = new NativeArray<double3>(sourcePositions.Length, Allocator.Temp);
            for (var i = 0; i < positions.Length; i++) positions[i] = ((double3)sourcePositions[i] - center) / scale;
            triangles = new NativeList<int3>(sourceTriangles.Length, Allocator.Temp);
            for (var i = 0; i < sourceTriangles.Length; i++)
                if (!discarded.IsSet(i)) triangles.Add(sourceTriangles[i]);
            nodes = new NativeList<Node>(math.max(1, triangles.Length * 2), Allocator.Temp);
            if (triangles.Length == 0) return;
            nodes.Add(new Node { Start = 0, Count = triangles.Length });
            for (var index = 0; index < nodes.Length; index++)
            {
                var node = nodes[index];
                node.Min = new double3(double.PositiveInfinity);
                node.Max = new double3(double.NegativeInfinity);
                for (var i = node.Start; i < node.Start + node.Count; i++)
                {
                    var t = triangles[i];
                    node.Min = math.min(node.Min, math.min(positions[t.x], math.min(positions[t.y], positions[t.z])));
                    node.Max = math.max(node.Max, math.max(positions[t.x], math.max(positions[t.y], positions[t.z])));
                }
                if (node.Count > 8)
                {
                    var size = node.Max - node.Min;
                    var axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                    triangles.AsArray().GetSubArray(node.Start, node.Count).Sort(new CentroidComparer { Positions = positions, Axis = axis });
                    var half = node.Count / 2;
                    node.Left = nodes.Length;
                    node.Right = nodes.Length + 1;
                    nodes.Add(new Node { Start = node.Start, Count = half });
                    nodes.Add(new Node { Start = node.Start + half, Count = node.Count - half });
                    node.Count = 0;
                }
                nodes[index] = node;
            }
        }

        internal readonly bool Contains(double3 point)
        {
            if (!nodes.IsCreated) return true;
            if (nodes.Length == 0 || !math.all(math.isfinite(point))) return false;
            // Balanced median splits require at most 32 pending nodes for an
            // int-indexed mesh. No recursion or per-query allocation in Burst.
            var stack = new FixedList512Bytes<int>();
            stack.Add(0);
            while (stack.Length > 0)
            {
                var index = stack[stack.Length - 1];
                stack.RemoveAt(stack.Length - 1);
                var node = nodes[index];
                var outside = math.max(math.max(node.Min - point, point - node.Max), 0d);
                if (math.lengthsq(outside) > toleranceSquared) continue;
                if (node.Count == 0)
                {
                    stack.Add(node.Left);
                    stack.Add(node.Right);
                    continue;
                }
                for (var i = node.Start; i < node.Start + node.Count; i++)
                {
                    var t = triangles[i];
                    if (PointTriangleDistanceSquared(point, positions[t.x], positions[t.y], positions[t.z]) <= toleranceSquared) return true;
                }
            }
            return false;
        }

        internal static double PointTriangleDistanceSquared(double3 p, double3 a, double3 b, double3 c)
        {
            var ab = b - a;
            var ac = c - a;
            var n = math.cross(ab, ac);
            var n2 = math.lengthsq(n);
            if (n2 > 0d)
            {
                // Barycentrics of the orthogonal projection onto the plane.
                var v = math.dot(math.cross(p - a, ac), n) / n2;
                var w = math.dot(math.cross(ab, p - a), n) / n2;
                if (v >= 0d && w >= 0d && v + w <= 1d)
                {
                    var height = math.dot(p - a, n);
                    return height * height / n2;
                }
            }
            return math.min(SegmentDistanceSquared(p, a, b), math.min(SegmentDistanceSquared(p, b, c), SegmentDistanceSquared(p, c, a)));
        }

        static double SegmentDistanceSquared(double3 p, double3 a, double3 b)
        {
            var edge = b - a;
            var length = math.lengthsq(edge);
            var t = length > 0d ? math.saturate(math.dot(p - a, edge) / length) : 0d;
            return math.lengthsq(p - (a + t * edge));
        }

        public void Dispose()
        {
            if (positions.IsCreated) positions.Dispose();
            if (triangles.IsCreated) triangles.Dispose();
            if (nodes.IsCreated) nodes.Dispose();
        }
    }
}
