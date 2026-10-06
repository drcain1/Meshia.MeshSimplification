#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Meshia.MeshSimplification.Ndmf.Editor
{
    // Source-space masks are copied into options, so cached count profiles and
    // asynchronous trials never refer to an already-disposed preparation buffer.
    internal static class CutOverlapProtection
    {
        internal static bool[][] Calculate(IReadOnlyList<Renderer> renderers, IReadOnlyList<Mesh> inputs, IReadOnlyList<Mesh> originals)
        {
            var result = inputs.Select(m => new bool[m.vertexCount]).ToArray();
            if (!inputs.Where((m, i) => m != originals[i]).Any()) return result;
            var surfaces = new List<Surface>(); var indices = new List<int>();
            for (var i = 0; i < inputs.Count; i++)
            {
                var positions = WorldPositions(renderers[i], inputs[i]);
                // A currently hidden NaNimation surface has no finite contact
                // geometry. Its independent visibility guard still applies.
                if (positions.Any(p => !Finite(p.x) || !Finite(p.y) || !Finite(p.z))) continue;
                surfaces.Add(new Surface(positions, Triangles(inputs[i]), Triangles(originals[i])));
                indices.Add(i);
            }
            Protect(surfaces);
            for (var i = 0; i < indices.Count; i++) result[indices[i]] = surfaces[i].Protected;
            return result;
        }

        private static bool Finite(float value) => !float.IsInfinity(value) && !float.IsNaN(value);
        private static int[] Triangles(Mesh mesh)
        {
            var result = new List<int>();
            for (var i = 0; i < mesh.subMeshCount; i++)
                if (mesh.GetTopology(i) == MeshTopology.Triangles) result.AddRange(mesh.GetTriangles(i));
            return result.ToArray();
        }

        internal static Vector3[] WorldPositions(Renderer renderer, Mesh input)
        {
            Vector3[] positions;
            if (renderer is SkinnedMeshRenderer skinned && input.bindposeCount > 0)
            {
                var scratch = new GameObject("Meshia overlap measurement") { hideFlags = HideFlags.HideAndDontSave };
                scratch.SetActive(false);
                var baked = new Mesh();
                try
                {
                    // An identity renderer bakes directly into world space through
                    // boneWorld * bindPose. Reconstructing its transform from
                    // lossyScale loses parent shear and can apply scale twice.
                    var copy = scratch.AddComponent<SkinnedMeshRenderer>();
                    copy.sharedMesh = input; copy.bones = skinned.bones; copy.rootBone = skinned.rootBone;
                    for (var j = 0; j < input.blendShapeCount; j++)
                    {
                        var index = skinned.sharedMesh.GetBlendShapeIndex(input.GetBlendShapeName(j));
                        if (index >= 0) copy.SetBlendShapeWeight(j, skinned.GetBlendShapeWeight(index));
                    }
                    copy.BakeMesh(baked); return baked.vertices;
                }
                finally { UnityEngine.Object.DestroyImmediate(baked); UnityEngine.Object.DestroyImmediate(scratch); }
            }
            else positions = input.vertices;
            var matrix = renderer.localToWorldMatrix;
            for (var i = 0; i < positions.Length; i++) positions[i] = matrix.MultiplyPoint3x4(positions[i]);
            return positions;
        }

        internal sealed class Surface
        {
            internal readonly Vector3[] Positions;
            internal readonly int[] Triangles;
            internal readonly int[] CutVertices;
            internal readonly bool[] Protected;
            internal readonly bool[] Used;
            internal readonly float Distance;
            internal readonly Bounds Bounds;
            internal readonly (int, int)[] Rim;
            internal readonly float RimWidth;
            private Tree? tree;
            internal Tree Index => tree ??= new Tree(Positions, Triangles);

            internal Surface(Vector3[] positions, int[] triangles, int[] originalTriangles)
            {
                Positions = positions; Triangles = triangles;
                Protected = new bool[positions.Length]; Used = new bool[positions.Length];
                foreach (var v in triangles) Used[v] = true;
                var oldEdges = Boundary(originalTriangles);
                var cuts = Boundary(triangles).Where(e => !oldEdges.Contains(e)).ToArray();
                CutVertices = cuts.SelectMany(e => new[] { e.Item1, e.Item2 }).Distinct().ToArray();
                var lengths = cuts.Select(e => Vector3.Distance(positions[e.Item1], positions[e.Item2]))
                    .Where(x => x > 0 && !float.IsInfinity(x) && !float.IsNaN(x)).OrderBy(x => x).ToArray();
                Distance = lengths.Length == 0 ? 0 : lengths[lengths.Length / 2] * .5f;
                var first = Array.FindIndex(Used, x => x);
                var bounds = new Bounds(first < 0 ? Vector3.zero : positions[first], Vector3.zero);
                for (var i = 0; i < positions.Length; i++) if (Used[i]) bounds.Encapsulate(positions[i]);
                Bounds = bounds;
                Distance = Mathf.Min(Distance, bounds.size.magnitude * .01f);
                // Find physical openings, not duplicated UV/normal seams. Welding
                // is only for detecting the rim; mesh topology is never changed.
                var epsilon = Mathf.Max(bounds.size.magnitude * 1e-6f, 1e-9f);
                var weld = new Dictionary<Vector3Int, int>(); var map = new int[positions.Length];
                for (var i = 0; i < positions.Length; i++)
                {
                    var p = (positions[i] - bounds.min) / epsilon;
                    var key = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
                    if (!weld.TryGetValue(key, out var representative)) weld[key] = representative = i;
                    map[i] = representative;
                }
                Rim = Boundary(triangles.Select(v => map[v]).ToArray()).Where(e => e.Item1 != e.Item2).ToArray();
                var rimLengths = Rim.Select(e => Vector3.Distance(positions[e.Item1], positions[e.Item2])).OrderBy(x => x).ToArray();
                RimWidth = rimLengths.Length == 0 ? 0 : Mathf.Min(rimLengths[rimLengths.Length / 2], bounds.size.magnitude * .08f);
            }

            internal bool NearRim(Vector3 point)
            {
                foreach (var edge in Rim)
                {
                    var a = Positions[edge.Item1]; var d = Positions[edge.Item2] - a;
                    var t = d.sqrMagnitude > 0 ? Mathf.Clamp01(Vector3.Dot(point - a, d) / d.sqrMagnitude) : 0;
                    if ((point - a - d * t).sqrMagnitude <= RimWidth * RimWidth) return true;
                }
                return false;
            }
        }

        internal static void Protect(IReadOnlyList<Surface> surfaces)
        {
            for (var i = 0; i < surfaces.Count; i++)
            {
                var cut = surfaces[i];
                if (cut.CutVertices.Length == 0 || !(cut.Distance > 0)) continue;
                for (var j = 0; j < surfaces.Count; j++)
                {
                    if (i == j) continue;
                    var other = surfaces[j];
                    if (other.Rim.Length == 0) continue;
                    var expanded = cut.Bounds; expanded.Expand(cut.Distance * 2);
                    if (!expanded.Intersects(other.Bounds)) continue;
                    // A neighboring surface must actually cover this cut. Merely
                    // sharing the avatar or having intersecting bounds is insufficient.
                    if (!cut.CutVertices.Any(v => other.Index.Find(cut.Positions[v], cut.Distance) >= 0)) continue;
                    MarkContact(cut, other, cut.Distance, other);
                    MarkContact(other, cut, cut.Distance, other);
                }
            }
            foreach (var surface in surfaces)
            {
                var seeds = (bool[])surface.Protected.Clone();
                for (var i = 0; i < surface.Triangles.Length; i += 3)
                {
                    int a = surface.Triangles[i], b = surface.Triangles[i + 1], c = surface.Triangles[i + 2];
                    if (seeds[a] || seeds[b] || seeds[c])
                        surface.Protected[a] = surface.Protected[b] = surface.Protected[c] = true;
                }
            }
        }

        private static void MarkContact(Surface a, Surface b, float distance, Surface opening)
        {
            for (var v = 0; v < a.Positions.Length; v++)
            {
                if (!a.Used[v] || !opening.NearRim(a.Positions[v])) continue;
                var tri = b.Index.Find(a.Positions[v], distance);
                if (tri < 0) continue;
                a.Protected[v] = true;
                for (var k = 0; k < 3; k++) b.Protected[b.Triangles[tri * 3 + k]] = true;
            }
        }

        internal static MeshSimplifierOptions Apply(MeshSimplifierOptions options, bool[] mask)
        {
            options.CutOverlapVertexRanges.Clear();
            if (!options.PreserveBorderEdges || options.AllowUnsafeGeometry) return options;
            // If an unusually fragmented selection exceeds the job's bounded
            // metadata, widen source-index buckets conservatively. This never
            // drops protected vertices or immediately locks the whole mesh.
            for (long width = 1; width <= Math.Max(1, (long)mask.Length * 2); width *= 2)
            {
                options.CutOverlapVertexRanges.Clear();
                var start = -1; var fits = true;
                for (long bucket = 0; bucket < mask.Length; bucket += width)
                {
                    var end = (int)Math.Min(mask.Length, bucket + width);
                    var selected = false;
                    for (var i = (int)bucket; i < end; i++) selected |= mask[i];
                    if (selected && start < 0) start = (int)bucket;
                    if (!selected && start >= 0)
                    {
                        if (!options.CutOverlapVertexRanges.TryAdd(start, (int)bucket)) { fits = false; break; }
                        start = -1;
                    }
                }
                if (fits && start >= 0) fits = options.CutOverlapVertexRanges.TryAdd(start, mask.Length);
                if (fits) return options;
            }
            return options;
        }

        private static HashSet<(int, int)> Boundary(int[] triangles)
        {
            var counts = new Dictionary<(int, int), int>();
            for (var i = 0; i < triangles.Length; i += 3)
                for (var k = 0; k < 3; k++)
                {
                    int a = triangles[i + k], b = triangles[i + (k + 1) % 3];
                    var edge = (Math.Min(a, b), Math.Max(a, b));
                    counts.TryGetValue(edge, out var n); counts[edge] = n + 1;
                }
            return counts.Where(e => e.Value == 1).Select(e => e.Key).ToHashSet();
        }

        internal sealed class Tree
        {
            private readonly Vector3[] vertices;
            private readonly int[] triangles, order;
            private readonly Bounds[] bounds;
            private readonly Node? root;
            private sealed class Node { internal Bounds Bounds; internal int Start, Count; internal Node? Left, Right; }
            internal Tree(Vector3[] vertices, int[] triangles)
            {
                this.vertices = vertices; this.triangles = triangles;
                order = Enumerable.Range(0, triangles.Length / 3).ToArray();
                bounds = new Bounds[order.Length];
                for (var i = 0; i < order.Length; i++)
                {
                    var b = new Bounds(vertices[triangles[3 * i]], Vector3.zero);
                    b.Encapsulate(vertices[triangles[3 * i + 1]]); b.Encapsulate(vertices[triangles[3 * i + 2]]); bounds[i] = b;
                }
                root = order.Length == 0 ? null : Build(0, order.Length);
            }
            private Node Build(int start, int count)
            {
                var b = bounds[order[start]];
                for (var i = start + 1; i < start + count; i++) b.Encapsulate(bounds[order[i]]);
                var n = new Node { Bounds = b, Start = start, Count = count };
                if (count <= 8) return n;
                var axis = b.size.x > b.size.y ? 0 : 1;
                if (b.size.z > b.size[axis]) axis = 2;
                Array.Sort(order, start, count, Comparer<int>.Create((a, c) => bounds[a].center[axis].CompareTo(bounds[c].center[axis])));
                n.Left = Build(start, count / 2); n.Right = Build(start + count / 2, count - count / 2);
                return n;
            }
            internal int Find(Vector3 point, float distance)
            {
                var squared = distance * distance; var result = -1;
                Search(root, point, ref squared, ref result); return result;
            }
            private void Search(Node? n, Vector3 p, ref float best, ref int result)
            {
                if (n == null || n.Bounds.SqrDistance(p) > best) return;
                if (n.Left != null) { Search(n.Left, p, ref best, ref result); Search(n.Right, p, ref best, ref result); return; }
                for (var i = n.Start; i < n.Start + n.Count; i++)
                {
                    var t = order[i];
                    if (bounds[t].SqrDistance(p) > best) continue;
                    var d = (Closest(p, vertices[triangles[3 * t]], vertices[triangles[3 * t + 1]], vertices[triangles[3 * t + 2]]) - p).sqrMagnitude;
                    if (d <= best) { best = d; result = t; }
                }
            }
            private static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
            {
                var ab = b - a; var ac = c - a; var ap = p - a;
                var d1 = Vector3.Dot(ab, ap); var d2 = Vector3.Dot(ac, ap);
                if (d1 <= 0 && d2 <= 0) return a;
                var bp = p - b; var d3 = Vector3.Dot(ab, bp); var d4 = Vector3.Dot(ac, bp);
                if (d3 >= 0 && d4 <= d3) return b;
                var vc = d1 * d4 - d3 * d2;
                if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
                var cp = p - c; var d5 = Vector3.Dot(ab, cp); var d6 = Vector3.Dot(ac, cp);
                if (d6 >= 0 && d5 <= d6) return c;
                var vb = d5 * d2 - d1 * d6;
                if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
                var va = d3 * d6 - d5 * d4;
                if (va <= 0 && d4 >= d3 && d5 >= d6) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
                var sum = va + vb + vc;
                return sum > 1e-30f ? a + ab * (vb / sum) + ac * (vc / sum) : a;
            }
        }
    }
}
