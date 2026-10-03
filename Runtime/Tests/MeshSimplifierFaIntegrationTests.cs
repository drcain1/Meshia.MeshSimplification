using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Meshia.MeshSimplification.Tests
{
    public class MeshSimplifierFaIntegrationTests
    {
        [Test]
        public void ShouldResolveLegacySettingsWithoutErasingIntentionalZeroWeights()
        {
            Assert.That(default(FaQemOptions).Effective.Equals(FaQemOptions.Default), Is.True);
            var configured = FaQemOptions.Default;
            configured.BoundaryWeight = configured.NormalWeight = configured.AreaWeight = 0;
            configured.Validate();
            Assert.That(configured.Effective.BoundaryWeight, Is.Zero);
            Assert.That(configured.Equals(default(FaQemOptions)), Is.False);
        }

        [TestCase(0f)]
        [TestCase(.002f)]
        public void ShouldPreserveSavedSurfaceDeviationInsteadOfReplacingItWithDefault(float tolerance)
        {
            var configured = FaQemOptions.Default;
            configured.MaxSurfaceDeviation = tolerance;
            var restored = JsonUtility.FromJson<FaQemOptions>(JsonUtility.ToJson(configured));
            restored.Validate();
            Assert.That(restored.Effective.MaxSurfaceDeviation, Is.EqualTo(tolerance));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-0.001f)]
        [TestCase(0.2f)]
        public void ShouldRejectInvalidSurfaceDeviation(float tolerance)
        {
            var options = FaQemOptions.Default;
            options.MaxSurfaceDeviation = tolerance;
            Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        }

        [Test]
        public void ShouldIncludeSurfaceDeviationInOptionEquality()
        {
            var a = FaQemOptions.Default;
            var b = a;
            b.MaxSurfaceDeviation = 0.001f;
            Assert.That(a.Equals(b), Is.False);
            Assert.That(default(FaQemOptions).Effective.MaxSurfaceDeviation, Is.EqualTo(.0005f));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-1f)]
        public void ShouldRejectInvalidFaBudgetsBeforeChangingDestination(float budget)
        {
            var source = Grid();
            var destination = Grid();
            try
            {
                var before = destination.vertices;
                Assert.Throws<ArgumentOutOfRangeException>(() => MeshSimplifier.Simplify(source,
                    new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = budget },
                    MeshSimplifierOptions.Default, destination));
                CollectionAssert.AreEqual(before, destination.vertices);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(destination); }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ShouldMatchSynchronousAsynchronousAndBatchFaOutput(bool enableSmartLink)
        {
            var source = Grid();
            var sync = new Mesh(); var asyncMesh = new Mesh(); var batch = new Mesh();
            var target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 30 };
            var options = MeshSimplifierOptions.Default;
            options.EnableSmartLink = enableSmartLink;
            options.FaQem.MaxSurfaceDeviation = 0.001f;
            try
            {
                var report = MeshSimplifier.SimplifyWithReport(source, target, options, sync);
                await MeshSimplifier.SimplifyAsync(source, target, options, asyncMesh);
                MeshSimplifier.SimplifyBatch(new[] { (source, target, options, batch) });
                CollectionAssert.AreEqual(sync.vertices, asyncMesh.vertices);
                CollectionAssert.AreEqual(sync.triangles, asyncMesh.triangles);
                CollectionAssert.AreEqual(sync.vertices, batch.vertices);
                CollectionAssert.AreEqual(sync.triangles, batch.triangles);
                Assert.That(report.UsedFaQem, Is.True);
                Assert.That(report.OutputTriangleCount, Is.EqualTo(sync.triangles.Length / 3));
                Assert.That(report.InputTriangleCount, Is.EqualTo(source.triangles.Length / 3));
                Assert.That(sync.vertexCount, Is.GreaterThan(0));
                Assert.That(sync.triangles.Length, Is.LessThan(source.triangles.Length));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(sync);
                UnityEngine.Object.DestroyImmediate(asyncMesh); UnityEngine.Object.DestroyImmediate(batch);
            }
        }

        [Test]
        public void ShouldReturnStableOutputMapsAndAcceptedHistory()
        {
            var source = Grid(); var destination = new Mesh();
            try
            {
                var sourceVertices = source.vertices;
                var sourceTriangles = source.triangles;
                var history = MeshSimplifier.SimplifyWithHistory(source,
                    new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 30 },
                    MeshSimplifierOptions.Default, null, destination);
                Assert.That(history.Records.Length, Is.GreaterThan(0));
                Assert.That(history.OutputVertexToSourceVertex.Length, Is.EqualTo(destination.vertexCount));
                Assert.That(history.OutputTriangleToSourceTriangle.Length, Is.EqualTo(destination.triangles.Length / 3));
                Assert.That(history.OutputVertexToSourceVertex.Distinct().Count(), Is.EqualTo(destination.vertexCount));
                Assert.That(history.Records.All(r => r.IncidentFaceStart >= 0 && r.IncidentFaceCount > 0 &&
                    r.IncidentFaceStart + r.IncidentFaceCount <= history.AffectedFaces.Length), Is.True);
                CollectionAssert.AreEqual(sourceVertices, source.vertices);
                CollectionAssert.AreEqual(sourceTriangles, source.triangles);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(destination); }
        }

        [Test]
        public void ShouldKeepOutputUntouchedWhenAlreadyCancelled()
        {
            var source = Grid(); var destination = new Mesh();
            try
            {
                Assert.CatchAsync<OperationCanceledException>(async () => await MeshSimplifier.SimplifyAsync(source,
                    new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 30 },
                    MeshSimplifierOptions.Default, destination, new CancellationToken(true)));
                Assert.That(destination.vertexCount, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(destination); }
        }

        static Mesh Grid()
        {
            const int size = 7;
            var vertices = new Vector3[size * size];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var indices = new int[(size - 1) * (size - 1) * 6];
            for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
            {
                var i = y * size + x; vertices[i] = new Vector3(x, y, 0);
                normals[i] = Vector3.forward; uv[i] = new Vector2(x / 6f, y / 6f);
            }
            var p = 0;
            for (var y = 0; y < size - 1; y++) for (var x = 0; x < size - 1; x++)
            {
                var a = y * size + x;
                indices[p++] = a; indices[p++] = a + 1; indices[p++] = a + size;
                indices[p++] = a + 1; indices[p++] = a + size + 1; indices[p++] = a + size;
            }
            return new Mesh { name = "FA-QEM API fixture", vertices = vertices, normals = normals, uv = uv, triangles = indices };
        }
    }
}
