using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Meshia.MeshSimplification.Tests
{
    public class FaQemReplayTests
    {
        static Mesh Fixture(bool flat = false, bool mirrored = false)
        {
            const int size = 10;
            var p = new Vector3[121]; var uv = new List<Vector4>(); var extra = new List<Vector2>();
            var triangles = new List<int>();
            for (var y = 0; y <= size; y++) for (var x = 0; x <= size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                var i = y * 11 + x;
                p[i] = new Vector3(u, v, flat ? 0 : .12f * Mathf.Sin(u * 4) * Mathf.Cos(v * 3));
                float mapped = flat ? u : u + .06f * Mathf.Sin(v * 5) * Mathf.Sin(u * Mathf.PI);
                uv.Add(new Vector4(.05f + .9f * (mirrored ? 1 - mapped : mapped), .05f + .9f * v, 3, 4));
                extra.Add(new Vector2(7, 8));
                if (x < size && y < size) triangles.AddRange(new[] { i, i + 1, i + 12, i, i + 12, i + 11 });
            }
            var mesh = new Mesh { vertices = p, triangles = triangles.ToArray() };
            mesh.SetUVs(0, uv); mesh.SetUVs(1, extra); mesh.RecalculateNormals(); mesh.RecalculateTangents();
            var delta = new Vector3[p.Length]; var zero = new Vector3[p.Length];
            var weights = new BoneWeight[p.Length];
            for (var i = 0; i < p.Length; i++) { delta[i] = new Vector3(.01f, .02f, .03f); weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 }; }
            mesh.boneWeights = weights; mesh.bindposes = new[] { Matrix4x4.identity };
            mesh.AddBlendShapeFrame("Translation", 100, delta, zero, zero);
            return mesh;
        }

        static MeshSimplifierOptions Options()
        {
            var options = MeshSimplifierOptions.Default;
            options.FaQem.ExperimentalUvEnabled = true;
            options.FaQem.ExperimentalJointUv = true;
            options.FaQem.ExperimentalUvWeight = 1000;
            options.FaQem.MaxSurfaceDeviation = .01f;
            options.SkinningProtection.Enabled = true;
            return options;
        }


        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task ShouldMatchFreshSimplificationInBothSliderDirections(bool mirrored, bool degenerate)
        {
            var source = Fixture(false, mirrored);
            if (degenerate)
            {
                var positions = source.vertices;
                positions[12] = positions[11];
                source.vertices = positions;
            }
            var actual = new Mesh();
            var expected = new Mesh();
            try
            {
                var options = Options();
                var plan = await MeshSimplifier.PrepareFaQemReplayAsync(source, options);
                foreach (var count in new[] { 200, 160, 90, 0, 130, 199, 200, 30 })
                {
                    await plan.WriteAsync(count, actual);
                    MeshSimplifier.Simplify(source, new MeshSimplificationTarget
                    {
                        Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = count
                    }, options, expected);
                    CollectionAssert.AreEqual(expected.vertices, actual.vertices, "Positions at " + count);
                    CollectionAssert.AreEqual(expected.triangles, actual.triangles, "Topology at " + count);
                    CollectionAssert.AreEqual(expected.normals, actual.normals, "Normals at " + count);
                    CollectionAssert.AreEqual(expected.tangents, actual.tangents, "Tangents at " + count);
                    CollectionAssert.AreEqual(expected.boneWeights, actual.boneWeights, "Skinning at " + count);
                    CollectionAssert.AreEqual(expected.bindposes, actual.bindposes);
                    Assert.AreEqual(expected.bounds, actual.bounds);
                    for (var channel = 0; channel < 8; channel++)
                    {
                        var a = new List<Vector4>(); var b = new List<Vector4>();
                        actual.GetUVs(channel, a); expected.GetUVs(channel, b);
                        CollectionAssert.AreEqual(b, a, "UV channel " + channel + " at " + count);
                    }
                    Assert.AreEqual(expected.blendShapeCount, actual.blendShapeCount);
                    for (var shape = 0; shape < expected.blendShapeCount; shape++)
                    {
                        Assert.AreEqual(expected.GetBlendShapeName(shape), actual.GetBlendShapeName(shape));
                        var av = new Vector3[actual.vertexCount]; var an = new Vector3[av.Length]; var at = new Vector3[av.Length];
                        var ev = new Vector3[expected.vertexCount]; var en = new Vector3[ev.Length]; var et = new Vector3[ev.Length];
                        actual.GetBlendShapeFrameVertices(shape, 0, av, an, at);
                        expected.GetBlendShapeFrameVertices(shape, 0, ev, en, et);
                        CollectionAssert.AreEqual(ev, av); CollectionAssert.AreEqual(en, an); CollectionAssert.AreEqual(et, at);
                    }
                }
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                var before = actual.vertices;
                Assert.CatchAsync<System.OperationCanceledException>(async () => await plan.WriteAsync(0, actual, cancelled.Token));
                CollectionAssert.AreEqual(before, actual.vertices, "Cancellation must not replace displayed output.");
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(actual); Object.DestroyImmediate(expected); }
        }
    }
}
