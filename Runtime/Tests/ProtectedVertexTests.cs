#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Meshia.MeshSimplification.Tests
{
    public class ProtectedVertexTests
    {
        internal static Mesh Grid()
        {
            const int n = 14;
            var v = new Vector3[n * n];
            var uv = new Vector2[v.Length];
            var indices = new System.Collections.Generic.List<int>();
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var i = y * n + x;
                    v[i] = new Vector3(x * .01f, y * .01f, 0);
                    uv[i] = new Vector2(x / (float)n, y / (float)n);
                    if (x + 1 < n && y + 1 < n)
                        indices.AddRange(new[]{i, i + 1, i + n, i + 1, i + n + 1, i + n});
                }

            var mesh = new Mesh{vertices = v, uv = uv, triangles = indices.ToArray(), normals = Enumerable.Repeat(Vector3.forward, v.Length).ToArray(), tangents = Enumerable.Repeat(new Vector4(0, 1, 0, 1), v.Length).ToArray(), colors = Enumerable.Repeat(Color.cyan, v.Length).ToArray()};
            mesh.bindposes = new[]{Matrix4x4.identity};
            mesh.boneWeights = Enumerable.Repeat(new BoneWeight{boneIndex0 = 0, weight0 = 1}, v.Length).ToArray();
            var delta = v.Select((p, i) => new Vector3(.001f * i, .002f, .003f)).ToArray();
            var normal = Enumerable.Repeat(new Vector3(.01f, .02f, .03f), v.Length).ToArray();
            var tangent = Enumerable.Repeat(new Vector3(.04f, .05f, .06f), v.Length).ToArray();
            mesh.AddBlendShapeFrame("Tracking", 50, delta, normal, tangent);
            mesh.AddBlendShapeFrame("Tracking", 100, delta.Select(d => d * 2).ToArray(), normal, tangent);
            return mesh;
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ShouldPreserveProtectedAttributesAcrossEveryPath(bool unsafeGeometry)
        {
            var source = Grid();
            var sync = new Mesh();
            var asyncMesh = new Mesh();
            var batch = new Mesh();
            var replayMesh = new Mesh();
            try
            {
                var protection = new ProtectedVertexSelection(source.vertices.Select(p => p.y >= .09f));
                var options = MeshSimplifierOptions.Default;
                options.PreserveBorderEdges = false;
                options.FaQem.MaxSurfaceDeviation = 0;
                options.FaQem.ExperimentalUvEnabled = true;
                options.FaQem.ExperimentalUvWeight = 5000;
                options.AllowUnsafeGeometry = unsafeGeometry;
                var target = new MeshSimplificationTarget{Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 30};
                var history = MeshSimplifier.SimplifyWithHistory(source, target, options, null, sync, protection);
                Assert.Less(sync.triangles.Length, source.triangles.Length, "The unprotected body must still reduce.");
                AssertProtected(source, sync, history, protection);
                await MeshSimplifier.SimplifyAsync(source, target, options, null, asyncMesh, protection: protection);
                MeshSimplifier.SimplifyBatch(new[]{(source, target, options, (BitArray? )null, batch)}, null, new[]{protection});
                var replay = await MeshSimplifier.PrepareFaQemReplayAsync(source, options, null, protection);
                await replay.WriteAsync(30, replayMesh);
                foreach (var output in new[]{asyncMesh, batch, replayMesh})
                {
                    CollectionAssert.AreEqual(sync.vertices, output.vertices);
                    CollectionAssert.AreEqual(sync.triangles, output.triangles);
                    CollectionAssert.AreEqual(sync.tangents, output.tangents);
                    AssertProtected(source, output, history, protection);
                }

                var measured = MeshSimplifier.MeasureFaQemCounts(source, 30, options, null, protection);
                var asynchronous = await MeshSimplifier.MeasureFaQemCountsAsync(source, 30, options, null, protection);
                Assert.AreEqual(sync.triangles.Length / 3, measured.FinalCount);
                Assert.AreEqual(measured.FinalCount, asynchronous.FinalCount);
            }
            finally
            {
                foreach (var mesh in new[]{source, sync, asyncMesh, batch, replayMesh})
                    Object.DestroyImmediate(mesh);
            }
        }

        private static void AssertProtected(Mesh source, Mesh output, FaQemHistory history, ProtectedVertexSelection selection)
        {
            var map = history.OutputVertexToSourceVertex;
            for (var i = 0; i < selection.Length; i++)
                if (selection[i])
                {
                    var j = Array.IndexOf(map, i);
                    Assert.GreaterOrEqual(j, 0, "Protected vertex removed");
                    Assert.AreEqual(source.vertices[i], output.vertices[j]);
                    Assert.AreEqual(source.normals[i], output.normals[j]);
                    Assert.AreEqual(source.tangents[i], output.tangents[j]);
                    Assert.AreEqual(source.uv[i], output.uv[j]);
                    Assert.AreEqual(source.colors[i], output.colors[j]);
                    Assert.AreEqual(source.boneWeights[i], output.boneWeights[j]);
                }

            Assert.AreEqual(source.blendShapeCount, output.blendShapeCount);
            for (var s = 0; s < source.blendShapeCount; s++)
                for (var f = 0; f < source.GetBlendShapeFrameCount(s); f++)
                {
                    Assert.AreEqual(source.GetBlendShapeName(s), output.GetBlendShapeName(s));
                    Assert.AreEqual(source.GetBlendShapeFrameWeight(s, f), output.GetBlendShapeFrameWeight(s, f));
                    var sv = new Vector3[source.vertexCount];
                    var sn = new Vector3[sv.Length];
                    var st = new Vector3[sv.Length];
                    var ov = new Vector3[output.vertexCount];
                    var on = new Vector3[ov.Length];
                    var ot = new Vector3[ov.Length];
                    source.GetBlendShapeFrameVertices(s, f, sv, sn, st);
                    output.GetBlendShapeFrameVertices(s, f, ov, on, ot);
                    for (var j = 0; j < map.Length; j++)
                        if (selection[map[j]])
                        {
                            Assert.AreEqual(sv[map[j]], ov[j]);
                            Assert.AreEqual(sn[map[j]], on[j]);
                            Assert.AreEqual(st[map[j]], ot[j]);
                        }
                }

            var sourceTriangles = source.triangles;
            var outputTriangles = output.triangles;
            for (var t = 0; t < sourceTriangles.Length; t += 3)
                if (selection[sourceTriangles[t]] && selection[sourceTriangles[t + 1]] && selection[sourceTriangles[t + 2]])
                {
                    var j = Array.IndexOf(history.OutputTriangleToSourceTriangle, t / 3);
                    Assert.GreaterOrEqual(j, 0, "Protected face removed");
                    for (var k = 0; k < 3; k++)
                        Assert.AreEqual(sourceTriangles[t + k], map[outputTriangles[j * 3 + k]]);
                }
        }

        [Test]
        public void ShouldRejectUnsupportedAlgorithmBeforeWritingOutput()
        {
            var source = Grid();
            var output = new Mesh();
            try
            {
                Assert.Throws<ArgumentException>(() => MeshSimplifier.SimplifyWithReport(source, new MeshSimplificationTarget{Kind = MeshSimplificationTargetKind.BlenderDecimateRatio, Value = .5f}, MeshSimplifierOptions.Default, null, output, new ProtectedVertexSelection(Enumerable.Repeat(true, source.vertexCount))));
                Assert.AreEqual(0, output.vertexCount);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(output);
            }
        }

        [Test]
        public void ShouldOwnImmutableSelection()
        {
            var bits = new[]{true, false};
            var selection = new ProtectedVertexSelection(bits);
            bits[0] = false;
            Assert.IsTrue(selection[0]);
            Assert.AreEqual(selection, new ProtectedVertexSelection(new[]{true, false}));
        }
    }
}
