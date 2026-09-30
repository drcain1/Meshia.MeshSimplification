using System;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Meshia.MeshSimplification.Tests
{
    /// <summary>Explicit editor benchmark; excluded from automatic test discovery.</summary>
    public static class FaQemVerificationBenchmark
    {
        [Serializable]
        public sealed class Result
        {
            public int InputTriangles;
            public int RequestedTriangles;
            public int OutputTriangles;
            public int OutputVertices;
            public double ElapsedMilliseconds;
            public float MaximumVertexHeightError;
            public bool SourceUnchanged;
            public string StopReason;
        }

        public static Result RunGrid(int cells, float ratio)
        {
            if (cells < 2 || cells > 128 || ratio <= 0 || ratio >= 1)
                throw new ArgumentOutOfRangeException();
            var source = new Mesh { name = "Transient FA-QEM benchmark surface" };
            var output = new Mesh();
            try
            {
                var vertices = new Vector3[(cells + 1) * (cells + 1)];
                var uv = new Vector2[vertices.Length];
                var indices = new int[cells * cells * 6];
                for (var y = 0; y <= cells; y++)
                for (var x = 0; x <= cells; x++)
                {
                    var i = y * (cells + 1) + x;
                    var u = (float)x / cells;
                    var v = (float)y / cells;
                    vertices[i] = new Vector3(u, v, Height(u, v));
                    uv[i] = new Vector2(u, v);
                }
                var cursor = 0;
                for (var y = 0; y < cells; y++)
                for (var x = 0; x < cells; x++)
                {
                    var a = y * (cells + 1) + x;
                    var b = a + 1;
                    var d = a + cells + 1;
                    var c = d + 1;
                    indices[cursor++] = a;
                    indices[cursor++] = b;
                    indices[cursor++] = c;
                    indices[cursor++] = a;
                    indices[cursor++] = c;
                    indices[cursor++] = d;
                }
                source.vertices = vertices;
                source.uv = uv;
                source.triangles = indices;
                source.RecalculateNormals();
                source.RecalculateTangents();
                var target = new MeshSimplificationTarget
                {
                    Kind = MeshSimplificationTargetKind.FaQemTriangleCount,
                    Value = Mathf.RoundToInt(indices.Length / 3 * ratio),
                };
                var timer = Stopwatch.StartNew();
                var report = MeshSimplifier.SimplifyWithReport(source, target, MeshSimplifierOptions.Default, output);
                timer.Stop();
                var error = 0f;
                foreach (var p in output.vertices)
                {
                    if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) ||
                        float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z))
                        throw new InvalidOperationException("Nonfinite benchmark output.");
                    error = Mathf.Max(error, Mathf.Abs(p.z - Height(p.x, p.y)));
                }
                return new Result
                {
                    InputTriangles = indices.Length / 3,
                    RequestedTriangles = (int)target.Value,
                    OutputTriangles = output.triangles.Length / 3,
                    OutputVertices = output.vertexCount,
                    ElapsedMilliseconds = timer.Elapsed.TotalMilliseconds,
                    MaximumVertexHeightError = error,
                    SourceUnchanged = vertices.SequenceEqual(source.vertices) && indices.SequenceEqual(source.triangles),
                    StopReason = report.FaQemTermination.ToString(),
                };
            }
            finally
            {
                Object.DestroyImmediate(output);
                Object.DestroyImmediate(source);
            }
        }

        private static float Height(float x, float y) => 0.1f * Mathf.Sin(x * Mathf.PI * 2) * Mathf.Sin(y * Mathf.PI * 2);
    }
}
