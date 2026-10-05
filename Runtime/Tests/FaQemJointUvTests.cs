using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Meshia.MeshSimplification.Tests
{
    public class FaQemJointUvTests
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

        [TestCase(1d)]
        [TestCase(100000d)]
        public void ShouldSolveCombinedPositionAndUvWithoutLosingGeometricAnchor(double weight)
        {
            var uv = FaQemUvQuadric.FromTriangle(double3.zero, new double3(1, 0, 0), new double3(0, 1, 0),
                double2.zero, new double2(1, 0), new double2(0, 1));
            var anchor = new double3(.26, .31, 0);
            var geometry = FaQemQuadric.Plane(new double3(1, 0, 0), anchor) +
                           FaQemQuadric.Plane(new double3(0, 1, 0), anchor) +
                           FaQemQuadric.Plane(new double3(0, 0, 1), anchor);
            Assert.IsTrue(uv.WithGeometry(geometry, weight).TrySolveOptimal(out var p, out var u));
            Assert.Less(math.distance(p, anchor), 1e-8);
            Assert.Less(math.distance(u, anchor.xy), 1e-8);
        }

        [Test]
        public void ShouldFallBackOnSingularJointSolve()
        {
            var source = Fixture(true); var a = new Mesh(); var b = new Mesh();
            try
            {
                var options = Options();
                var target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 90 };
                MeshSimplifier.Simplify(source, target, options, a);
                options.FaQem.ExperimentalJointUv = false;
                MeshSimplifier.Simplify(source, target, options, b);
                CollectionAssert.AreEqual(b.vertices, a.vertices);
                CollectionAssert.AreEqual(b.uv, a.uv);
                CollectionAssert.AreEqual(b.triangles, a.triangles);
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldKeepUvWindingExtraChannelsAndDeformationDeltas(bool mirrored)
        {
            var source = Fixture(false, mirrored); var output = new Mesh(); var baseline = new Mesh();
            try
            {
                var options = Options();
                var target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 90 };
                var original = source.vertices; var sourceUv = source.uv;
                MeshSimplifier.Simplify(source, target, options, output);
                options.FaQem.ExperimentalJointUv = false;
                MeshSimplifier.Simplify(source, target, options, baseline);
                Assert.IsFalse(System.Linq.Enumerable.SequenceEqual(baseline.uv, output.uv), "Fixture must exercise the joint UV write.");
                Assert.Less(output.triangles.Length, source.triangles.Length);
                var uv = new List<Vector4>(); output.GetUVs(0, uv);
                var extra = output.uv2; var normals = output.normals; var tangents = output.tangents;
                var delta = new Vector3[output.vertexCount]; var dn = new Vector3[delta.Length]; var dt = new Vector3[delta.Length];
                output.GetBlendShapeFrameVertices(0, 0, delta, dn, dt);
                for (var i = 0; i < uv.Count; i++)
                {
                    Assert.That(uv[i].x, Is.InRange(.04999f, .95001f)); Assert.That(uv[i].y, Is.InRange(.04999f, .95001f));
                    Assert.That(uv[i].z, Is.EqualTo(3).Within(1e-5)); Assert.That(uv[i].w, Is.EqualTo(4).Within(1e-5));
                    Assert.Less((extra[i] - new Vector2(7, 8)).magnitude, 1e-5);
                    var tangent = (Vector3)tangents[i];
                    Assert.That(tangent.magnitude, Is.EqualTo(1).Within(1e-5));
                    Assert.That(Vector3.Dot(normals[i].normalized, tangent), Is.EqualTo(0).Within(1e-5));
                    Assert.AreEqual(mirrored ? -1f : 1f, tangents[i].w);
                    Assert.Less((delta[i] - new Vector3(.01f, .02f, .03f)).magnitude, 1e-6);
                    Assert.Less(dn[i].magnitude, 1e-6); Assert.Less(dt[i].magnitude, 1e-5, "Translation must not rotate tangent deltas.");
                    Assert.AreEqual(1, output.boneWeights[i].weight0);
                }
                var indices = output.triangles;
                for (var i = 0; i < indices.Length; i += 3)
                {
                    Vector2 a = uv[indices[i]], b = uv[indices[i+1]], c = uv[indices[i+2]];
                    var area = (b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
                    Assert.Greater(area * (mirrored ? -1 : 1), 0);
                }
                CollectionAssert.AreEqual(original, source.vertices); CollectionAssert.AreEqual(sourceUv, source.uv);
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(output); Object.DestroyImmediate(baseline); }
        }

        [UnityTest]
        public IEnumerator ShouldMatchSyncAsyncAndBatchJointOutput()
        {
            var source = Fixture(); var a = new Mesh(); var b = new Mesh(); var c = new Mesh();
            try
            {
                var options = Options();
                var target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 90 };
                MeshSimplifier.Simplify(source, target, options, a);
                var task = MeshSimplifier.SimplifyAsync(source, target, options, null, b);
                while (!task.IsCompleted) yield return null;
                task.GetAwaiter().GetResult();
                MeshSimplifier.SimplifyBatch(new[] { (source, target, options, c) });
                foreach (var mesh in new[] { b, c })
                {
                    CollectionAssert.AreEqual(a.vertices, mesh.vertices); CollectionAssert.AreEqual(a.triangles, mesh.triangles);
                    CollectionAssert.AreEqual(a.uv, mesh.uv); CollectionAssert.AreEqual(a.tangents, mesh.tangents);
                }
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(c); }
        }

        [Test]
        public void ShouldKeepDeformedTangentFramesValid()
        {
            var source = Fixture();
            var posed = Object.Instantiate(source);
            var output = new Mesh();
            try
            {
                var vertices = source.vertices;
                var normals = source.normals;
                var tangents = source.tangents;
                var bent = posed.vertices;
                var weights = source.boneWeights;
                for (var i = 0; i < bent.Length; i++)
                {
                    bent[i].z += .07f * bent[i].y * bent[i].y;
                    weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 - vertices[i].y,
                        boneIndex1 = 1, weight1 = vertices[i].y };
                }
                posed.vertices = bent;
                posed.RecalculateNormals();
                posed.RecalculateTangents();
                var bentNormals = posed.normals;
                var bentTangents = posed.tangents;
                var dp = new Vector3[bent.Length];
                var dn = new Vector3[bent.Length];
                var dt = new Vector3[bent.Length];
                for (var i = 0; i < bent.Length; i++)
                {
                    dp[i] = bent[i] - vertices[i];
                    dn[i] = bentNormals[i] - normals[i];
                    dt[i] = (Vector3)bentTangents[i] - (Vector3)tangents[i];
                }
                source.ClearBlendShapes();
                source.AddBlendShapeFrame("Bend", 100, dp, dn, dt);
                source.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
                source.boneWeights = weights;
                var target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 90 };
                MeshSimplifier.Simplify(source, target, Options(), output);
                Assert.Less(output.triangles.Length, source.triangles.Length);
                dp = new Vector3[output.vertexCount];
                dn = new Vector3[output.vertexCount];
                dt = new Vector3[output.vertexCount];
                output.GetBlendShapeFrameVertices(0, 0, dp, dn, dt);
                normals = output.normals;
                tangents = output.tangents;
                weights = output.boneWeights;
                for (var i = 0; i < output.vertexCount; i++)
                {
                    var normal = (normals[i] + dn[i]).normalized;
                    var tangent = (Vector3)tangents[i] + dt[i];
                    Assert.That(tangent.magnitude, Is.EqualTo(1).Within(1e-4));
                    Assert.That(Vector3.Dot(normal, tangent), Is.EqualTo(0).Within(1e-4));
                    Assert.That(weights[i].weight0 + weights[i].weight1, Is.EqualTo(1).Within(1e-5));
                    Assert.That(weights[i].weight0, Is.InRange(0, 1));
                    Assert.That(weights[i].weight1, Is.InRange(0, 1));
                }
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(posed);
                Object.DestroyImmediate(output);
            }
        }
    }
}
