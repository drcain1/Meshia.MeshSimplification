using System;
using System.Collections.Generic;
using Meshia.MeshSimplification;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Meshia.MeshSimplification.Tests
{
    public class FaQemEngineTests
    {
        [TestCase(MeshSimplificationTargetKind.FaQemTriangleCount)]
        [TestCase(MeshSimplificationTargetKind.BlenderDecimateRatio)]
        [TestCase(MeshSimplificationTargetKind.AbsoluteTriangleCount)]
        [TestCase(MeshSimplificationTargetKind.UvLoopDissolveTriangleCount)]
        public void ShouldAllowUnprotectedReductionWithoutChangingSavedOptions(MeshSimplificationTargetKind kind)
        {
            var source = MakeGrid(8, 8);
            var output = new Mesh();
            var guarded = new Mesh();
            try
            {
                var saved = MeshSimplifierOptions.ConservativeAvatar;
                var options = saved.WithoutProtections();
                Assert.AreEqual(MeshSimplifierOptions.ConservativeAvatar, saved);
                Assert.AreNotEqual(saved, options);
                Assert.IsFalse(options.SkinningProtection.Resolve(true).Enabled);
                var target = new MeshSimplificationTarget { Kind = kind,
                    Value = kind == MeshSimplificationTargetKind.BlenderDecimateRatio ? .05f : 6 };
                MeshSimplifier.Simplify(source, target, options, output);
                Assert.Less(output.triangles.Length, source.triangles.Length);
                foreach (var index in output.triangles) Assert.That(index, Is.InRange(0, output.vertexCount - 1));
                foreach (var v in output.vertices)
                    Assert.IsFalse(float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.z));
                if (kind == MeshSimplificationTargetKind.FaQemTriangleCount)
                {
                    MeshSimplifier.Simplify(source, target, saved, guarded);
                    Assert.Less(output.triangles.Length, guarded.triangles.Length, "No protection must release boundary and shape constraints.");
                    var profile = MeshSimplifier.MeasureFaQemCounts(source, 0, options);
                    Assert.IsTrue(profile.TryGetOutput(6, out var count));
                    Assert.AreEqual(output.triangles.Length / 3, count);
                }
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(output); Object.DestroyImmediate(guarded); }
        }

        [TestCase(false, false, 0f)]
        [TestCase(true, false, 0f)]
        [TestCase(false, true, 0f)]
        [TestCase(true, true, 0f)]
        [TestCase(false, false, 10f)]
        [TestCase(true, false, 1000f)]
        [TestCase(true, true, 10f)]
        [TestCase(true, false, 1000f, true)]
        [TestCase(true, true, 1000f, true)]
        public void ShouldMatchFreshRunsAcrossRecordedCountSequence(bool borders, bool deformed, float uvWeight, bool joint = false)
        {
            var source = MakeGrid(8, 8);
            var output = new Mesh();
            try
            {
                var options = MeshSimplifierOptions.Default;
                options.PreserveBorderEdges = borders;
                options.FaQem.ExperimentalUvEnabled = uvWeight > 0;
                options.FaQem.ExperimentalUvWeight = uvWeight;
                options.FaQem.ExperimentalJointUv = joint;
                if (uvWeight > 0)
                {
                    var uv = new Vector2[source.vertexCount];
                    var positions = source.vertices;
                    for (var i = 0; i < uv.Length; i++)
                        uv[i] = new Vector2(positions[i].x / 8 + .05f * Mathf.Sin(positions[i].y), positions[i].y / 8);
                    source.uv = uv;
                }
                if (deformed)
                {
                    var points = source.vertices;
                    var deltas = new Vector3[points.Length];
                    for (var i = 0; i < points.Length; i++)
                    {
                        points[i].z = Mathf.Sin(points[i].x) * .25f;
                        deltas[i] = new Vector3(0, 0, Mathf.Cos(points[i].y) * .1f);
                    }
                    source.vertices = points;
                    source.AddBlendShapeFrame("Bend", 100, deltas, null, null);
                    source.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
                    var weights = new BoneWeight[points.Length];
                    for (var i = 0; i < weights.Length; i++)
                        weights[i] = new BoneWeight { boneIndex0 = points[i].x < 4 ? 0 : 1, weight0 = 1 };
                    source.boneWeights = weights;
                    options.SkinningProtection.Enabled = true;
                    options.SkinningProtection.PreserveJointTransitions = true;
                    source.RecalculateNormals();
                    source.RecalculateBounds();
                }
                var before = source.vertices;
                var profile = MeshSimplifier.MeasureFaQemCounts(source, 0, options);
                foreach (var count in new[] { 0, 1, 2, 15, 31, 32, 33, 61, 95, 127, 128, 200 })
                {
                    var target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = count };
                    MeshSimplifier.Simplify(source, target, options, output);
                    Assert.That(profile.TryGetOutput(count, out var measured), Is.True);
                    Assert.That(measured, Is.EqualTo(output.triangles.Length / 3), $"Request {count}");
                }
                CollectionAssert.AreEqual(before, source.vertices);
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(output); }
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator ShouldMeasureCountsAsynchronouslyWithoutChangingSource()
        {
            var source = MakeGrid(8, 8);
            try
            {
                var vertices = source.vertices;
                var indices = source.triangles;
                var expected = MeshSimplifier.MeasureFaQemCounts(source, 0, MeshSimplifierOptions.Default);
                var pending = MeshSimplifier.MeasureFaQemCountsAsync(source, 0, MeshSimplifierOptions.Default);
                while (!pending.IsCompleted) yield return null;
                var actual = pending.GetAwaiter().GetResult();
                for (var target = 0; target <= 128; target++)
                {
                    Assert.True(expected.TryGetOutput(target, out var a));
                    Assert.True(actual.TryGetOutput(target, out var b));
                    Assert.AreEqual(a, b);
                }
                CollectionAssert.AreEqual(vertices, source.vertices);
                CollectionAssert.AreEqual(indices, source.triangles);
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void ShouldCaptureCountProfileWithoutChangingBatchGeometry()
        {
            var source = MakeGrid(8, 8);
            var first = new Mesh();
            var second = new Mesh();
            try
            {
                var options = MeshSimplifierOptions.Default;
                var target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 61 };
                var profiles = new List<FaQemCountProfile>();
                MeshSimplifier.SimplifyBatch(new[] { (source, target, options, (System.Collections.BitArray)null, first) }, profiles);
                MeshSimplifier.Simplify(source, target, options, second);
                CollectionAssert.AreEqual(first.triangles, second.triangles);
                CollectionAssert.AreEqual(first.vertices, second.vertices);
                Assert.That(profiles[0].TryGetOutput(61, out var output), Is.True);
                Assert.That(output, Is.EqualTo(first.triangles.Length / 3));
                Assert.That(profiles[0].TryGetOutput(60, out _), Is.EqualTo(profiles[0].ReachedLimit));
                MeshSimplifier.Simplify(source, new MeshSimplificationTarget { Kind = target.Kind, Value = 99 }, options, second);
                Assert.That(profiles[0].TryGetOutput(99, out output), Is.True);
                Assert.That(output, Is.EqualTo(second.triangles.Length / 3));
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [Test]
        public void ShouldPreserveEarlyReturnBeforeDegenerateCleanupInCountProfile()
        {
            var source = MakeGrid(3, 3);
            var output = new Mesh();
            try
            {
                var triangles = new List<int>(source.triangles);
                triangles.AddRange(new[] { 0, 1, 2 }); // Collinear rest-pose face.
                source.triangles = triangles.ToArray();
                var profile = MeshSimplifier.MeasureFaQemCounts(source, 0, MeshSimplifierOptions.Default);
                foreach (var count in new[] { 0, 1, 17, 18, 19, 20 })
                {
                    MeshSimplifier.Simplify(source, new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = count }, MeshSimplifierOptions.Default, output);
                    Assert.That(profile.TryGetOutput(count, out var actual), Is.True);
                    Assert.That(actual, Is.EqualTo(output.triangles.Length / 3), $"Request {count}");
                }
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(output); }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void ShouldCompleteRepeatedLargeGuardedBatches(bool preserveBorders)
        {
            var source = MakeGrid(96, 96);
            var first = new Mesh();
            var second = new Mesh();
            try
            {
                var vertices = source.vertices;
                for (var i = 0; i < vertices.Length; i++)
                    vertices[i].z = 2f * Mathf.Sin(vertices[i].x * .08f) * Mathf.Cos(vertices[i].y * .06f);
                source.vertices = vertices;
                source.RecalculateNormals();
                source.RecalculateBounds();
                var options = MeshSimplifierOptions.Default;
                options.PreserveBorderEdges = preserveBorders;
                options.FaQem.MaxSurfaceDeviation = .001f;
                var target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 5000 };
                int[] expected = null;
                // Exercise many candidate checks and simultaneous jobs, rather
                // than only the tiny fixtures that missed Temp accumulation.
                for (var pass = 0; pass < 3; pass++)
                {
                    MeshSimplifier.SimplifyBatch(new[] { (source, target, options, first), (source, target, options, second) });
                    Assert.That(first.triangles.Length / 3, Is.InRange(4998, 5000));
                    CollectionAssert.AreEqual(first.triangles, second.triangles);
                    CollectionAssert.AreEqual(first.vertices, second.vertices);
                    if (expected != null) CollectionAssert.AreEqual(expected, first.triangles);
                    expected = first.triangles;
                }
                Assert.That(source.triangles.Length / 3, Is.EqualTo(18432));
            }
            finally
            {
                Object.DestroyImmediate(source); Object.DestroyImmediate(first); Object.DestroyImmediate(second);
            }
        }

        [TestCase(0.01f)]
        [TestCase(1f)]
        [TestCase(10000f)]
        public void ShouldKeepCurvedSurfaceSamplesWithinOriginalEnvelope(float scale)
        {
            var source = MakeGrid(12, 12);
            var output = new Mesh();
            var unguarded = new Mesh();
            try
            {
                var points = source.vertices;
                for (var i = 0; i < points.Length; i++)
                {
                    var p = points[i];
                    points[i] = scale * new Vector3(p.x, p.y, 2f * Mathf.Cos(p.x * .35f) * Mathf.Cos(p.y * .25f));
                }
                source.vertices = points;
                source.RecalculateNormals();
                source.RecalculateBounds();
                var options = MeshSimplifierOptions.Default;
                var target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 60 };
                options.FaQem.MaxSurfaceDeviation = 0f; // Explicit unguarded comparison; the default now enables the envelope.
                MeshSimplifier.Simplify(source, target, options, unguarded);
                options.FaQem.MaxSurfaceDeviation = .001f;
                MeshSimplifier.Simplify(source, target, options, output);
                var limit = source.bounds.size.magnitude * options.FaQem.MaxSurfaceDeviation;
                Assert.That(MaxSampleDistance(output, source), Is.LessThanOrEqualTo(limit * 1.001d));
                Assert.That(MaxSampleDistance(unguarded, source), Is.GreaterThan(limit));
                Assert.That(output.triangles.Length, Is.LessThan(source.triangles.Length), "The guard must still permit simplification.");
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(output);
                Object.DestroyImmediate(unguarded);
            }
        }

        // Brute-force source search independently checks the production BVH.
        static double MaxSampleDistance(Mesh output, Mesh source)
        {
            var v = output.vertices;
            var t = output.triangles;
            var original = source.vertices;
            var originalTriangles = source.triangles;
            double maximum = 0;
            for (var i = 0; i < t.Length; i += 3)
            {
                Unity.Mathematics.double3 a = (Unity.Mathematics.float3)v[t[i]];
                Unity.Mathematics.double3 b = (Unity.Mathematics.float3)v[t[i + 1]];
                Unity.Mathematics.double3 c = (Unity.Mathematics.float3)v[t[i + 2]];
                foreach (var p in new[] { a, b, c, (a+b)*.5d, (a+c)*.5d, (b+c)*.5d, (a+b+c)/3d })
                {
                    double distance = double.PositiveInfinity;
                    for (var j = 0; j < originalTriangles.Length; j += 3)
                        distance = Math.Min(distance, FaQemSurfaceEnvelope.PointTriangleDistanceSquared(p,
                            (Unity.Mathematics.float3)original[originalTriangles[j]],
                            (Unity.Mathematics.float3)original[originalTriangles[j+1]],
                            (Unity.Mathematics.float3)original[originalTriangles[j+2]]));
                    maximum = Math.Max(maximum, distance);
                }
            }
            return Math.Sqrt(maximum);
        }

        static Mesh MakeGrid(int width, int height)
        {
            var mesh = new Mesh { name = "FA-QEM test grid" };
            var vertices = new List<Vector3>((width + 1) * (height + 1));
            var triangles = new List<int>(width * height * 6);
            for (var y = 0; y <= height; y++)
                for (var x = 0; x <= width; x++)
                    vertices.Add(new Vector3(x, y, 0));
            int Index(int x, int y) => y * (width + 1) + x;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var a = Index(x, y); var b = Index(x + 1, y);
                    var c = Index(x + 1, y + 1); var d = Index(x, y + 1);
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(a); triangles.Add(c); triangles.Add(d);
                }
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        [Test]
        public void ShouldKeepOpenBoundaryVerticesWithDefaultOptions()
        {
            var source = MakeGrid(6, 6);
            var output = new Mesh();
            try
            {
                var history = MeshSimplifier.SimplifyWithHistory(source,
                    new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 40 },
                    MeshSimplifierOptions.Default, null, output);
                var original = source.vertices;
                var simplified = output.vertices;
                for (var index = 0; index < original.Length; index++)
                {
                    var point = original[index];
                    if (point.x != 0 && point.x != 6 && point.y != 0 && point.y != 6) continue;
                    var outputIndex = Array.IndexOf(history.OutputVertexToSourceVertex, index);
                    Assert.That(outputIndex, Is.GreaterThanOrEqualTo(0), "Boundary vertex must survive");
                    Assert.That(simplified[outputIndex], Is.EqualTo(point), "Boundary vertex must stay fixed");
                }
                Assert.That(output.triangles.Length, Is.LessThan(source.triangles.Length));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(output);
            }
        }

        static Mesh MakeTetrahedron()
        {
            var mesh = new Mesh { name = "FA-QEM closed tetrahedron" };
            mesh.vertices = new[]
            {
                new Vector3(1, 1, 1), new Vector3(-1, -1, 1),
                new Vector3(-1, 1, -1), new Vector3(1, -1, -1),
            };
            mesh.triangles = new[]
            {
                0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3,
            };
            mesh.RecalculateNormals();
            return mesh;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldKeepSplitSurfaceSeamClosed(bool distinctUvs)
        {
            var source = MakeGrid(4, 4);
            var output = new Mesh();
            try
            {
                var vertices = new List<Vector3>(source.vertices);
                var triangles = source.triangles;
                var copies = new Dictionary<int, int>();
                for (var y = 0; y <= 4; y++)
                {
                    var original = y * 5 + 2;
                    copies.Add(original, vertices.Count);
                    vertices.Add(vertices[original]);
                }
                // Split the grid along x=2 without opening a geometric gap.
                for (var i = 0; i < triangles.Length; i += 3)
                {
                    var centerX = (vertices[triangles[i]].x + vertices[triangles[i + 1]].x + vertices[triangles[i + 2]].x) / 3f;
                    if (centerX <= 2) continue;
                    for (var j = 0; j < 3; j++)
                        if (copies.TryGetValue(triangles[i + j], out var copy)) triangles[i + j] = copy;
                }
                var uv = new Vector2[vertices.Count];
                for (var i = 0; i < vertices.Count; i++)
                    uv[i] = new Vector2(vertices[i].x / 4f, vertices[i].y / 4f);
                if (distinctUvs)
                    foreach (var copy in copies.Values) uv[copy] += Vector2.right;
                source.SetVertices(vertices);
                source.triangles = triangles;
                source.uv = uv;
                source.RecalculateNormals();
                var history = MeshSimplifier.SimplifyWithHistory(source,
                    new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 12 },
                    MeshSimplifierOptions.Default, null, output);
                var reverse = new Dictionary<int, int>();
                for (var i = 0; i < history.OutputVertexToSourceVertex.Length; i++)
                    reverse.Add(history.OutputVertexToSourceVertex[i], i);
                var result = output.vertices;
                foreach (var pair in copies)
                {
                    Assert.That(reverse.ContainsKey(pair.Key), Is.True, "Original seam endpoint was collapsed independently.");
                    Assert.That(reverse.ContainsKey(pair.Value), Is.True, "Split seam endpoint was collapsed independently.");
                    Assert.That(result[reverse[pair.Key]], Is.EqualTo(vertices[pair.Key]));
                    Assert.That(result[reverse[pair.Value]], Is.EqualTo(vertices[pair.Key]));
                }
                Assert.That(output.triangles.Length, Is.LessThan(source.triangles.Length), "Interior reduction must still work.");
                CollectionAssert.AreEqual(vertices, source.vertices);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(output);
            }
        }

        [Test]
        public void ShouldProduceDeterministicValidGridOutput()
        {
            var source = MakeGrid(4, 4);
            var first = new Mesh();
            var second = new Mesh();
            try
            {
                var target = new MeshSimplificationTarget
                {
                    Kind = MeshSimplificationTargetKind.FaQemTriangleCount,
                    Value = 12,
                };
                MeshSimplifier.Simplify(source, target, MeshSimplifierOptions.Default, first);
                MeshSimplifier.Simplify(source, target, MeshSimplifierOptions.Default, second);
                Assert.That(first.triangles, Is.EqualTo(second.triangles));
                Assert.That(first.vertexCount, Is.GreaterThan(0));
                foreach (var t in first.triangles)
                    Assert.That(t, Is.InRange(0, first.vertexCount - 1));
                for (var i = 0; i < first.triangles.Length; i += 3)
                    Assert.That(first.triangles[i], Is.Not.EqualTo(first.triangles[i + 1]));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void ShouldNormalizeUniformScaleWithoutChangingTriangleBudget()
        {
            var source = MakeGrid(3, 3);
            var scaled = Object.Instantiate(source);
            var vertices = scaled.vertices;
            for (var i = 0; i < vertices.Length; i++) vertices[i] *= 10000f;
            scaled.vertices = vertices;
            var target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 8 };
            var a = new Mesh();
            var b = new Mesh();
            try
            {
                MeshSimplifier.Simplify(source, target, MeshSimplifierOptions.Default, a);
                MeshSimplifier.Simplify(scaled, target, MeshSimplifierOptions.Default, b);
                Assert.That(a.triangles.Length, Is.EqualTo(b.triangles.Length));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(scaled);
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void ShouldKeepClosedMeshOutputNonDegenerateAndPreserveSource()
        {
            var source = MakeTetrahedron();
            var sourceVertices = source.vertices;
            var sourceTriangles = source.triangles;
            var output = new Mesh();
            try
            {
                MeshSimplifier.Simplify(source,
                    new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 2 },
                    MeshSimplifierOptions.Default, output);
                Assert.That(source.vertices, Is.EqualTo(sourceVertices));
                Assert.That(source.triangles, Is.EqualTo(sourceTriangles));
                Assert.That(output.triangles.Length, Is.GreaterThanOrEqualTo(3));
                var faces = new HashSet<string>();
                for (var i = 0; i < output.triangles.Length; i += 3)
                {
                    Assert.That(output.triangles[i], Is.Not.EqualTo(output.triangles[i + 1]));
                    var face = new[] { output.triangles[i], output.triangles[i + 1], output.triangles[i + 2] };
                    Array.Sort(face);
                    Assert.That(faces.Add($"{face[0]}:{face[1]}:{face[2]}"), Is.True, "Duplicate output face");
                    var a = output.vertices[face[0]];
                    var b = output.vertices[face[1]];
                    var c = output.vertices[face[2]];
                    Assert.That(Vector3.Cross(b - a, c - a).sqrMagnitude, Is.GreaterThan(1e-12f));
                }
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(output);
            }
        }

        [Test]
        public void ShouldKeepScaleExtremeOutputFiniteAndNonDegenerate()
        {
            foreach (var scale in new[] { 1e-8f, 1e8f })
            {
                var source = MakeGrid(2, 2);
                var output = new Mesh();
                try
                {
                    var vertices = source.vertices;
                    for (var i = 0; i < vertices.Length; i++) vertices[i] *= scale;
                    source.vertices = vertices;
                    MeshSimplifier.Simplify(source,
                        new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 4 },
                    MeshSimplifierOptions.Default, output);
                    Assert.That(output.triangles.Length, Is.GreaterThan(0));
                    for (var i = 0; i < output.triangles.Length; i += 3)
                    {
                        var a = output.vertices[output.triangles[i]];
                        var b = output.vertices[output.triangles[i + 1]];
                        var c = output.vertices[output.triangles[i + 2]];
                        Assert.That(Vector3.Cross(b - a, c - a).sqrMagnitude, Is.GreaterThan(0f));
                    }
                }
                finally
                {
                    Object.DestroyImmediate(source);
                    Object.DestroyImmediate(output);
                }
            }
        }
    }
}
