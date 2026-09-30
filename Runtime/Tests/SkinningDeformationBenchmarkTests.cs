#nullable enable
using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Meshia.MeshSimplification.Tests
{
    public sealed class SkinningDeformationBenchmarkTests
    {
        [TestCase(MeshSimplificationTargetKind.BlenderDecimateRatio, 0.5f)]
        [TestCase(MeshSimplificationTargetKind.FaQemTriangleCount, 4f)]
        public void ProtectionDecisionIsInvariantToUniformMeshScale(MeshSimplificationTargetKind kind, float value)
        {
            var source = CreateJointStrip(); var scaled = Object.Instantiate(source); var a = new Mesh(); var b = new Mesh();
            try
            {
                var vertices = scaled.vertices;
                for (var i = 0; i < vertices.Length; i++) vertices[i] *= 17f;
                scaled.vertices = vertices; scaled.RecalculateBounds();
                var options = MeshSimplifierOptions.Default;
                options.SkinningProtection = new SkinningProtectionOptions { Enabled = true, Strength = 4f, MaxWeightDistance = .2f, MaxDiscardedWeight = .05f };
                var target = new MeshSimplificationTarget { Kind = kind, Value = value };
                MeshSimplifier.Simplify(source, target, options, a);
                MeshSimplifier.Simplify(scaled, target, options, b);
                Assert.That(b.triangles, Is.EqualTo(a.triangles));
                Assert.That(b.boneWeights, Is.EqualTo(a.boneWeights));
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(scaled); Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }

        [TestCase(MeshSimplificationTargetKind.BlenderDecimateRatio, 0.5f)]
        [TestCase(MeshSimplificationTargetKind.FaQemTriangleCount, 4f)]
        public void ProtectionDoesNotIncreaseCorrespondedPoseErrorAtSameBudget(MeshSimplificationTargetKind kind, float value)
        {
            var source = CreateJointStrip();
            var baseline = new Mesh();
            var matchedBaseline = new Mesh();
            var protectedMesh = new Mesh();
            try
            {
                var target = new MeshSimplificationTarget { Kind = kind, Value = value };
                MeshSimplifier.Simplify(source, target, MeshSimplifierOptions.Default, baseline);
                var options = MeshSimplifierOptions.Default;
                options.SkinningProtection = new SkinningProtectionOptions
                {
                    Enabled = true,
                    Strength = 4f,
                    MaxWeightDistance = .2f,
                    MaxDiscardedWeight = .05f,
                };
                MeshSimplifier.Simplify(source, target, options, protectedMesh);
                var achievedTriangles = protectedMesh.triangles.Length / 3;
                var matchedTarget = new MeshSimplificationTarget
                {
                    Kind = kind,
                    Value = kind == MeshSimplificationTargetKind.BlenderDecimateRatio
                        ? achievedTriangles / 8f
                        : achievedTriangles,
                };
                MeshSimplifier.Simplify(source, matchedTarget, MeshSimplifierOptions.Default, matchedBaseline);

                var neutral = new[] { Matrix4x4.identity, Matrix4x4.identity };
                var bent = new[] { AboutPivot(new Vector3(2, 0, 0), 65), AboutPivot(new Vector3(2, 0, 0), -65) };
                var opposite = new[] { AboutPivot(new Vector3(2, 0, 0), -50), AboutPivot(new Vector3(2, 0, 0), 50) };
                var baselineError = Math.Max(PoseDisplacementRms(source, matchedBaseline, neutral, bent), PoseDisplacementRms(source, matchedBaseline, neutral, opposite));
                var protectedError = Math.Max(PoseDisplacementRms(source, protectedMesh, neutral, bent), PoseDisplacementRms(source, protectedMesh, neutral, opposite));
                var scale = source.bounds.size.magnitude;
                var baselineStatic = StaticProjectionRms(source, matchedBaseline) / scale;
                var protectedStatic = StaticProjectionRms(source, protectedMesh) / scale;

                Assert.That(baseline.triangles.Length / 3, Is.GreaterThanOrEqualTo(1));
                Assert.That(protectedMesh.triangles.Length / 3, Is.GreaterThanOrEqualTo(1));
                Assert.That(protectedMesh.triangles.Length, Is.EqualTo(matchedBaseline.triangles.Length),
                    $"The fixture must compare an equal achieved triangle budget for {kind}.");
                TestContext.WriteLine($"{kind}: requested baseline triangles={baseline.triangles.Length / 3}, protected achieved={achievedTriangles}; " +
                    $"equal-budget normalized pose RMS baseline={baselineError / scale:G6}, protected={protectedError / scale:G6}");
                TestContext.WriteLine($"{kind}: normalized static RMS baseline={baselineStatic:G6}, protected={protectedStatic:G6}");
                Assert.That(!float.IsNaN(baselineStatic) && !float.IsInfinity(baselineStatic) &&
                            !float.IsNaN(protectedStatic) && !float.IsInfinity(protectedStatic), Is.True);
                Assert.That(protectedError / scale, Is.LessThanOrEqualTo(baselineError / scale + 1e-5f),
                    $"{kind}: normalized deformation RMS baseline={baselineError / scale:G6}, protected={protectedError / scale:G6}; triangles {baseline.triangles.Length / 3}/{protectedMesh.triangles.Length / 3}");
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(baseline);
                Object.DestroyImmediate(matchedBaseline);
                Object.DestroyImmediate(protectedMesh);
            }
        }

        static Mesh CreateJointStrip()
        {
            const int columns = 5;
            var vertices = new Vector3[columns * 2];
            var weights = new BoneWeight[vertices.Length];
            for (var x = 0; x < columns; x++)
            for (var row = 0; row < 2; row++)
            {
                var i = x * 2 + row;
                vertices[i] = new Vector3(x, row == 0 ? -.25f : .25f, 0);
                var right = x <= 1 ? 0f : x >= 3 ? 1f : .5f;
                weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f - right, boneIndex1 = 1, weight1 = right };
            }
            var triangles = new int[(columns - 1) * 6];
            for (var x = 0; x < columns - 1; x++)
            {
                var o = x * 6; var a = x * 2;
                triangles[o] = a; triangles[o + 1] = a + 1; triangles[o + 2] = a + 3;
                triangles[o + 3] = a; triangles[o + 4] = a + 3; triangles[o + 5] = a + 2;
            }
            var mesh = new Mesh { name = "two-bone correspondence strip", vertices = vertices, triangles = triangles, boneWeights = weights };
            mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        static Matrix4x4 AboutPivot(Vector3 pivot, float degrees) =>
            Matrix4x4.Translate(pivot) * Matrix4x4.Rotate(Quaternion.Euler(0, 0, degrees)) * Matrix4x4.Translate(-pivot);

        static float PoseDisplacementRms(Mesh source, Mesh output, Matrix4x4[] neutral, Matrix4x4[] pose)
        {
            var sv = source.vertices; var sw = source.boneWeights; var st = source.triangles;
            var sourceNeutral = Skin(sv, sw, neutral); var sourcePose = Skin(sv, sw, pose);
            var ov = output.vertices; var ow = output.boneWeights;
            var outputNeutral = Skin(ov, ow, neutral); var outputPose = Skin(ov, ow, pose);
            var sum = 0f;
            for (var i = 0; i < ov.Length; i++)
            {
                ClosestSourceTriangle(ov[i], sv, st, out var tri, out var bary);
                var referenceNeutral = Bary(sourceNeutral, st, tri, bary);
                var referencePose = Bary(sourcePose, st, tri, bary);
                var actualDisplacement = outputPose[i] - outputNeutral[i];
                var referenceDisplacement = referencePose - referenceNeutral;
                sum += (actualDisplacement - referenceDisplacement).sqrMagnitude;
            }
            return Mathf.Sqrt(sum / Math.Max(1, ov.Length));
        }

        static float StaticProjectionRms(Mesh source, Mesh output)
        {
            var sv = source.vertices; var st = source.triangles; var ov = output.vertices; var sum = 0f;
            foreach (var p in ov)
            {
                ClosestSourceTriangle(p, sv, st, out var tri, out var bary);
                var q = Bary(sv, st, tri, bary);
                sum += (p - q).sqrMagnitude;
            }
            return Mathf.Sqrt(sum / Math.Max(1, ov.Length));
        }

        static Vector3[] Skin(Vector3[] vertices, BoneWeight[] weights, Matrix4x4[] matrices)
        {
            var result = new Vector3[vertices.Length];
            for (var i = 0; i < result.Length; i++)
            {
                var w = weights[i]; var p = vertices[i];
                result[i] = matrices[w.boneIndex0].MultiplyPoint3x4(p) * w.weight0 + matrices[w.boneIndex1].MultiplyPoint3x4(p) * w.weight1;
                result[i] += matrices[w.boneIndex2].MultiplyPoint3x4(p) * w.weight2 + matrices[w.boneIndex3].MultiplyPoint3x4(p) * w.weight3;
            }
            return result;
        }

        static Vector3 Bary(Vector3[] values, int[] triangles, int triangle, Vector3 bary)
        {
            var o = triangle * 3;
            return values[triangles[o]] * bary.x + values[triangles[o + 1]] * bary.y + values[triangles[o + 2]] * bary.z;
        }

        static void ClosestSourceTriangle(Vector3 point, Vector3[] vertices, int[] triangles, out int bestTriangle, out Vector3 bestBary)
        {
            bestTriangle = 0; bestBary = new Vector3(1, 0, 0); var best = float.PositiveInfinity;
            for (var t = 0; t < triangles.Length / 3; t++)
            {
                var o = t * 3; var a = vertices[triangles[o]]; var b = vertices[triangles[o + 1]]; var c = vertices[triangles[o + 2]];
                var bary = ClosestBarycentric(point, a, b, c); var q = a * bary.x + b * bary.y + c * bary.z;
                var d = (q - point).sqrMagnitude;
                if (d < best) { best = d; bestTriangle = t; bestBary = bary; }
            }
        }

        // Ericson's closest-point regions, returning clamped triangle barycentrics.
        static Vector3 ClosestBarycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = b - a; var ac = c - a; var ap = p - a;
            var d1 = Vector3.Dot(ab, ap); var d2 = Vector3.Dot(ac, ap); if (d1 <= 0 && d2 <= 0) return new Vector3(1, 0, 0);
            var bp = p - b; var d3 = Vector3.Dot(ab, bp); var d4 = Vector3.Dot(ac, bp); if (d3 >= 0 && d4 <= d3) return new Vector3(0, 1, 0);
            var vc = d1 * d4 - d3 * d2; if (vc <= 0 && d1 >= 0 && d3 <= 0) { var v = d1 / (d1 - d3); return new Vector3(1 - v, v, 0); }
            var cp = p - c; var d5 = Vector3.Dot(ab, cp); var d6 = Vector3.Dot(ac, cp); if (d6 >= 0 && d5 <= d6) return new Vector3(0, 0, 1);
            var vb = d5 * d2 - d1 * d6; if (vb <= 0 && d2 >= 0 && d6 <= 0) { var w = d2 / (d2 - d6); return new Vector3(1 - w, 0, w); }
            var va = d3 * d6 - d5 * d4; if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) { var w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); return new Vector3(0, 1 - w, w); }
            var denom = 1f / (va + vb + vc); var v2 = vb * denom; var w2 = vc * denom; return new Vector3(1 - v2 - w2, v2, w2);
        }
    }
}
