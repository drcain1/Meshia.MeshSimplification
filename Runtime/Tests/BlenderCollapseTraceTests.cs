#nullable enable
using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Meshia.MeshSimplification.Tests
{
    public sealed class BlenderCollapseTraceTests
    {
        [Test]
        public void TraceDoesNotChangeSimplifiedGeometryOrSkinning()
        {
            var source = CreateStrip();
            var ordinary = new Mesh();
            var traced = new Mesh();
            try
            {
                var target = BlenderTarget(.25f);
                var options = ProtectedOptions();
                MeshSimplifier.Simplify(source, target, options, ordinary);
                var trace = MeshSimplifier.SimplifyWithBlenderTraceForDiagnostics(
                    source, target, options, null, new[] { 4, 5 }, 64, traced);

                Assert.That(traced.vertices, Is.EqualTo(ordinary.vertices));
                Assert.That(traced.triangles, Is.EqualTo(ordinary.triangles));
                Assert.That(traced.boneWeights, Is.EqualTo(ordinary.boneWeights));
                Assert.That(traced.bindposes, Is.EqualTo(ordinary.bindposes));
                using var ordinaryBonesPerVertex = ordinary.GetBonesPerVertex();
                using var tracedBonesPerVertex = traced.GetBonesPerVertex();
                using var ordinaryWeights = ordinary.GetAllBoneWeights();
                using var tracedWeights = traced.GetAllBoneWeights();
                Assert.That(tracedBonesPerVertex.ToArray(), Is.EqualTo(ordinaryBonesPerVertex.ToArray()));
                Assert.That(tracedWeights.ToArray(), Is.EqualTo(ordinaryWeights.ToArray()));
                Assert.That(trace.records.Length, Is.GreaterThan(0));
                var priorSequence = -1;
                foreach (var record in trace.records)
                {
                    Assert.That(record.acceptedSequence, Is.GreaterThan(priorSequence));
                    Assert.That(record.vertexALineage | record.vertexBLineage, Is.InRange(1UL, 3UL));
                    Assert.That(record.acceptanceTotalCost,
                        Is.EqualTo(record.acceptanceGeometricCost + record.skinningCost).Within(1e-6f));
                    Assert.That(record.queuedMinusAcceptanceCost,
                        Is.EqualTo(record.queuedTotalCost - record.acceptanceTotalCost).Within(1e-6f));
                    priorSequence = record.acceptedSequence;
                }
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(ordinary);
                Object.DestroyImmediate(traced);
            }
        }

        [Test]
        public void TraceCapacityIsBoundedAndReportsTruncation()
        {
            var source = CreateStrip();
            var destination = new Mesh();
            try
            {
                var trace = MeshSimplifier.SimplifyWithBlenderTraceForDiagnostics(
                    source, BlenderTarget(.1f), UnrestrictedOptions(), null,
                    new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, 1, destination);
                Assert.That(trace.records, Has.Length.EqualTo(1));
                Assert.That(trace.truncated, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(destination);
            }
        }

        [Test]
        public void TraceValidatesSelectionAndCapacity()
        {
            var source = CreateStrip();
            var destination = new Mesh();
            try
            {
                Assert.Throws<ArgumentException>(() => MeshSimplifier.SimplifyWithBlenderTraceForDiagnostics(
                    source, BlenderTarget(.5f), UnrestrictedOptions(), null, new[] { 1, 1 }, 8, destination));
                Assert.Throws<ArgumentOutOfRangeException>(() => MeshSimplifier.SimplifyWithBlenderTraceForDiagnostics(
                    source, BlenderTarget(.5f), UnrestrictedOptions(), null, new[] { source.vertexCount }, 8, destination));
                Assert.Throws<ArgumentOutOfRangeException>(() => MeshSimplifier.SimplifyWithBlenderTraceForDiagnostics(
                    source, BlenderTarget(.5f), UnrestrictedOptions(), null, new[] { 1 }, 0, destination));
                Assert.Throws<ArgumentException>(() => MeshSimplifier.SimplifyWithBlenderTraceForDiagnostics(
                    source, new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.RelativeTriangleCount, Value = .5f },
                    UnrestrictedOptions(), null, new[] { 1 }, 8, destination));
                var invalidOptions = UnrestrictedOptions();
                invalidOptions.SkinningProtection.Strength = float.NaN;
                Assert.Throws<ArgumentOutOfRangeException>(() => MeshSimplifier.SimplifyWithBlenderTraceForDiagnostics(
                    source, BlenderTarget(.5f), invalidOptions, null, new[] { 1 }, 8, destination));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(destination);
            }
        }

        [Test]
        public void OrdinaryBlenderSimplificationWorksWithoutTraceStorage()
        {
            var source = CreateStrip();
            var destination = new Mesh();
            try
            {
                MeshSimplifier.Simplify(source, BlenderTarget(.5f), UnrestrictedOptions(), destination);
                Assert.That(destination.vertexCount, Is.GreaterThan(0));
                Assert.That(destination.triangles.Length, Is.LessThan(source.triangles.Length));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(destination);
            }
        }

        // Every vertex of the trace fixture is on a border; allow collapses to exercise tracing.
        static MeshSimplifierOptions UnrestrictedOptions()
        {
            var options = MeshSimplifierOptions.Default;
            options.PreserveBorderEdges = false;
            return options;
        }

        static MeshSimplificationTarget BlenderTarget(float ratio) => new()
        {
            Kind = MeshSimplificationTargetKind.BlenderDecimateRatio,
            Value = ratio,
        };

        static MeshSimplifierOptions ProtectedOptions()
        {
            var options = UnrestrictedOptions();
            options.SkinningProtection = new SkinningProtectionOptions
            {
                Enabled = true,
                Strength = 4f,
                MaxWeightDistance = 1f,
                MaxDiscardedWeight = 1f,
            };
            return options;
        }

        static Mesh CreateStrip()
        {
            const int columns = 6;
            var vertices = new Vector3[columns * 2];
            var weights = new BoneWeight[vertices.Length];
            for (var x = 0; x < columns; x++)
            for (var row = 0; row < 2; row++)
            {
                var index = x * 2 + row;
                vertices[index] = new Vector3(x, row, .05f * (x % 2));
                var right = x / (float)(columns - 1);
                weights[index] = new BoneWeight
                {
                    boneIndex0 = 0,
                    weight0 = 1f - right,
                    boneIndex1 = 1,
                    weight1 = right,
                };
            }
            var triangles = new int[(columns - 1) * 6];
            for (var x = 0; x < columns - 1; x++)
            {
                var offset = x * 6;
                var a = x * 2;
                triangles[offset] = a;
                triangles[offset + 1] = a + 1;
                triangles[offset + 2] = a + 3;
                triangles[offset + 3] = a;
                triangles[offset + 4] = a + 3;
                triangles[offset + 5] = a + 2;
            }
            var mesh = new Mesh
            {
                vertices = vertices,
                triangles = triangles,
                boneWeights = weights,
                bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity },
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
