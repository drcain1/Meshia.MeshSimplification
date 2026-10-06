using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Meshia.MeshSimplification.Ndmf.Editor.Tests
{
    public class CutOverlapProtectionTests
    {
        private static int[] RemoveStrip(Mesh mesh)
        {
            var v = mesh.vertices; var t = mesh.triangles;
            return Enumerable.Range(0, t.Length / 3)
                .Where(i => !Enumerable.Range(0, 3).Any(k => v[t[i * 3 + k]].y > .055f && v[t[i * 3 + k]].y < .075f))
                .SelectMany(i => new[] { t[i * 3], t[i * 3 + 1], t[i * 3 + 2] }).ToArray();
        }

        [Test]
        public void ProtectsBothContactSurfacesButNotUnrelatedGeometry()
        {
            var grid = Meshia.MeshSimplification.Ndmf.Tests.CutMeshPreparationTests.Grid();
            var source = Object.Instantiate(grid); var output = new Mesh();
            try
            {
                source.triangles = RemoveStrip(grid);
                var body = new CutOverlapProtection.Surface(grid.vertices, source.triangles, grid.triangles);
                var clothes = grid.vertices.Select(v => new Vector3(v.x, .075f + v.y * .2f, .0002f)).ToArray();
                var cloth = new CutOverlapProtection.Surface(clothes, grid.triangles, grid.triangles);
                var far = new CutOverlapProtection.Surface(clothes.Select(v => v + Vector3.right * 2).ToArray(), grid.triangles, grid.triangles);
                CutOverlapProtection.Protect(new[] { body, cloth, far });
                Assert.That(body.Protected.Count(x => x), Is.InRange(1, body.Protected.Length - 1));
                Assert.Greater(cloth.Protected.Count(x => x), 0);
                Assert.IsFalse(far.Protected.Any(x => x));
                var options = CutOverlapProtection.Apply(MeshSimplifierOptions.Default, body.Protected);
                MeshSimplifier.Simplify(source, new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 1 }, options, output);
                Assert.Less(output.triangles.Length, source.triangles.Length, "Unprotected regions must still simplify.");
                CollectionAssert.AreEquivalent(Meshia.MeshSimplification.Ndmf.Tests.CutMeshPreparationTests.BoundaryPositions(source),
                    Meshia.MeshSimplification.Ndmf.Tests.CutMeshPreparationTests.BoundaryPositions(output));
            }
            finally { Object.DestroyImmediate(grid); Object.DestroyImmediate(source); Object.DestroyImmediate(output); }
        }

        [Test]
        public void DoesNotProtectOrdinaryOverlappingMeshesWithoutACut()
        {
            var mesh = Meshia.MeshSimplification.Ndmf.Tests.CutMeshPreparationTests.Grid();
            try
            {
                var a = new CutOverlapProtection.Surface(mesh.vertices, mesh.triangles, mesh.triangles);
                var b = new CutOverlapProtection.Surface(mesh.vertices, mesh.triangles, mesh.triangles);
                CutOverlapProtection.Protect(new[] { a, b });
                Assert.IsFalse(a.Protected.Any(x => x)); Assert.IsFalse(b.Protected.Any(x => x));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void RangeStorageCopiesByValueAndHonorsDisabledProtection()
        {
            var mask = Enumerable.Range(0, 1000).Select(i => i < 600 && i % 2 == 0).ToArray();
            var full = CutOverlapProtection.Apply(MeshSimplifierOptions.Default, mask);
            for (var i = 0; i < mask.Length; i++)
                if (mask[i]) Assert.IsTrue(Enumerable.Range(0, full.CutOverlapVertexRanges.Length / 2)
                    .Any(r => i >= full.CutOverlapVertexRanges[r * 2] && i < full.CutOverlapVertexRanges[r * 2 + 1]));
            mask = Enumerable.Range(0, 400).Select(i => i < 252 && i % 2 == 0).ToArray();
            var a = CutOverlapProtection.Apply(MeshSimplifierOptions.Default, mask);
            Assert.AreEqual(252, a.CutOverlapVertexRanges.Length);
            Assert.AreEqual(251, a.CutOverlapVertexRanges[251]);
            var b = a; b.CutOverlapVertexRanges.Clear();
            Assert.AreEqual(252, a.CutOverlapVertexRanges.Length);
            Assert.AreNotEqual(a, b);
            Assert.AreEqual(0, a.WithoutProtections().CutOverlapVertexRanges.Length);
            var off = MeshSimplifierOptions.Default; off.PreserveBorderEdges = false;
            Assert.AreEqual(0, CutOverlapProtection.Apply(off, mask).CutOverlapVertexRanges.Length);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ContactLimitAllowsFlatReductionAndWorksWithoutGlobalEnvelope(bool curved)
        {
            var source = Meshia.MeshSimplification.Ndmf.Tests.CutMeshPreparationTests.Grid();
            var regular = new Mesh(); var protectedMesh = new Mesh();
            try
            {
                source.ClearBlendShapes();
                if (curved)
                {
                    source.vertices = source.vertices.Select(v => new Vector3(v.x, v.y,
                        .02f * (Mathf.Sin(v.x * 35) + Mathf.Sin(v.y * 37)))).ToArray();
                    source.RecalculateNormals();
                }
                var options = MeshSimplifierOptions.Default; options.FaQem.MaxSurfaceDeviation = 0;
                var target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 1 };
                MeshSimplifier.Simplify(source, target, options, regular);
                options = CutOverlapProtection.Apply(options, Enumerable.Repeat(true, source.vertexCount).ToArray());
                MeshSimplifier.Simplify(source, target, options, protectedMesh);
                if (curved) Assert.Greater(protectedMesh.triangles.Length, regular.triangles.Length, "Local shape protection must work when the global limit is off.");
                else
                {
                    Assert.Less(protectedMesh.triangles.Length, source.triangles.Length);
                    Assert.AreEqual(regular.triangles.Length, protectedMesh.triangles.Length, "Contact tags must not be treated as fixed attribute seams.");
                }
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(regular); Object.DestroyImmediate(protectedMesh); }
        }

        [Test]
        public void RangesSupportLargeIndicesAcrossStorageBoundary()
        {
            var ranges = new SourceVertexRanges();
            for (var i = 0; i < 126; i++) Assert.IsTrue(ranges.TryAdd(i * 2, i * 2 + 1));
            Assert.IsTrue(ranges.TryAdd(80000, 200000));
            Assert.AreEqual(80000, ranges[252]); Assert.AreEqual(200000, ranges[253]);
        }

        [Test]
        public void ContactCoordinatesRespectScaledAndRotatedRigParents()
        {
            var root = new GameObject("Contact transform fixture"); var mesh = new Mesh();
            try
            {
                root.transform.position = new Vector3(1, 2, 3);
                root.transform.rotation = Quaternion.Euler(10, 20, 30);
                root.transform.localScale = new Vector3(2, 3, 4);
                var child = new GameObject("Renderer"); child.transform.SetParent(root.transform, false);
                child.transform.localRotation = Quaternion.Euler(30, 10, 0);
                child.transform.localScale = new Vector3(.7f, 1.2f, .9f);
                var bone = new GameObject("Bone"); bone.transform.SetParent(root.transform, false);
                var renderer = child.AddComponent<SkinnedMeshRenderer>();
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; mesh.triangles = new[] { 0, 1, 2 };
                mesh.bindposes = new[] { bone.transform.worldToLocalMatrix * renderer.localToWorldMatrix };
                mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, 3).ToArray();
                renderer.sharedMesh = mesh; renderer.bones = new[] { bone.transform }; renderer.rootBone = root.transform;
                bone.transform.localPosition += Vector3.up * .1f;
                var positions = CutOverlapProtection.WorldPositions(renderer, mesh);
                var expected = bone.transform.localToWorldMatrix * mesh.bindposes[0];
                for (var i = 0; i < positions.Length; i++)
                    Assert.Less(Vector3.Distance(expected.MultiplyPoint3x4(mesh.vertices[i]), positions[i]), 1e-4f);
                Assert.AreSame(mesh, renderer.sharedMesh);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(mesh); }
        }
    }
}
