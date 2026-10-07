using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Meshia.MeshSimplification.Ndmf.Editor.Tests
{
    public class CutOverlapProtectionTests
    {
        [Test]
        public async System.Threading.Tasks.Task ShouldPreserveContactJointInBothBuildAndAsyncPreview()
        {
            var source = Meshia.MeshSimplification.Ndmf.Tests.CutMeshPreparationTests.Grid();
            var regular = new Mesh();
            var built = new Mesh();
            var preview = new Mesh();
            try
            {
                source.ClearBlendShapes();
                source.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
                source.boneWeights = source.vertices.Select(v => new BoneWeight
                {
                    boneIndex0 = 0,
                    boneIndex1 = 1,
                    weight0 = 1 - Mathf.Clamp01((v.y - .03f) / .07f),
                    weight1 = Mathf.Clamp01((v.y - .03f) / .07f)
                }).ToArray();
                var options = MeshSimplifierOptions.Default;
                options.SkinningProtection.Enabled = false;
                options.SkinningProtection.PreserveJointTransitions = false;
                var target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 1 };
                MeshSimplifier.Simplify(source, target, options, regular);
                options = CutOverlapProtection.ApplyResolved(options, new CutOverlapProtection.Protection(
                    source.vertices.Select(v => v.y > .02f && v.y < .11f).ToArray(), new[] { 0, 1 }));
                MeshSimplifier.Simplify(source, target, options, built);
                await MeshSimplifier.SimplifyAsync(source, target, options, null, preview);
                Assert.Greater(built.triangles.Length, regular.triangles.Length, "The bending transition must retain support geometry.");
                CollectionAssert.AreEqual(built.vertices, preview.vertices);
                CollectionAssert.AreEqual(built.triangles, preview.triangles);
                CollectionAssert.AreEqual(built.boneWeights, preview.boneWeights);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(regular);
                Object.DestroyImmediate(built);
                Object.DestroyImmediate(preview);
            }
        }

        [Test]
        public void ShouldFollowContactInfluencesWithoutSelectingUnrelatedJoints()
        {
            var mesh = Meshia.MeshSimplification.Ndmf.Tests.CutMeshPreparationTests.Grid();
            try
            {
                var body = new CutOverlapProtection.Surface(mesh.vertices, RemoveStrip(mesh), mesh.triangles);
                var positions = mesh.vertices.Select(v => new Vector3(v.x, .04f + v.y * .7f, .0002f)).ToArray();
                var clothing = new CutOverlapProtection.Surface(positions, mesh.triangles, mesh.triangles);
                clothing.Influences = mesh.vertices.Select(v => v.y < .09f ? new[] { 3, 7 } : new[] { 11, 12 }).ToArray();
                var unrelated = new CutOverlapProtection.Surface(positions.Select(p => p + Vector3.right).ToArray(), mesh.triangles, mesh.triangles);
                unrelated.Influences = clothing.Influences;
                CutOverlapProtection.Protect(new[] { body, clothing, unrelated });
                CollectionAssert.AreEquivalent(new[] { 3, 7 }, clothing.JointBones);
                Assert.IsTrue(clothing.Protected[8 * 14 + 7], "The selected bending band extends beyond direct cut contact.");
                Assert.IsEmpty(unrelated.JointBones);
                Assert.IsFalse(unrelated.Protected.Any(x => x));
                Assert.IsEmpty(body.JointBones, "Rigid/unskinned cut geometry needs no new joint selection.");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void ShouldMergeAutomaticJointsWithoutChangingUserOptionsOrDisabledProtection()
        {
            var protection = new CutOverlapProtection.Protection(new[] { false, true, true }, new[] { 3, 7 });
            var original = MeshSimplifierOptions.Default;
            original.SkinningProtection.PreserveJointTransitions = true;
            original.SkinningProtection.JointProtectionBoneIndices.Add(15);
            var resolved = CutOverlapProtection.ApplyResolved(original, protection);
            Assert.AreEqual(1, original.SkinningProtection.JointProtectionBoneIndices.Length);
            Assert.AreEqual(0, original.CutOverlapVertexRanges.Length);
            Assert.AreEqual(3, resolved.SkinningProtection.JointProtectionBoneIndices.Length);
            Assert.AreEqual(15, resolved.SkinningProtection.JointProtectionBoneIndices[0]);
            Assert.IsTrue(resolved.SkinningProtection.PreserveJointTransitions);
            var all = original;
            all.SkinningProtection.JointProtectionBoneIndices.Clear();
            Assert.AreEqual(0, CutOverlapProtection.ApplyResolved(all, protection).SkinningProtection.JointProtectionBoneIndices.Length);
            var off = MeshSimplifierOptions.Default;
            off.PreserveBorderEdges = false;
            Assert.AreEqual(off, CutOverlapProtection.ApplyResolved(off, protection));
            off = MeshSimplifierOptions.Default.WithoutProtections();
            Assert.AreEqual(off, CutOverlapProtection.ApplyResolved(off, protection));
        }

        [Test]
        public void ShouldReadAllBoneInfluencesAndIgnoreNegligibleWeights()
        {
            var owner = new GameObject("Influence fixture");
            var mesh = new Mesh();
            try
            {
                var renderer = owner.AddComponent<SkinnedMeshRenderer>();
                var bones = new Transform[6];
                for (var i = 0; i < bones.Length; i++)
                {
                    bones[i] = new GameObject("Bone " + i).transform;
                    bones[i].SetParent(owner.transform, false);
                }
                mesh.vertices = new[] { Vector3.zero };
                mesh.bindposes = Enumerable.Repeat(Matrix4x4.identity, 6).ToArray();
                using var counts = new Unity.Collections.NativeArray<byte>(new byte[] { 6 }, Unity.Collections.Allocator.Temp);
                var values = new[] { .4f, .2f, .15f, .1495f, .1f, .0005f };
                using var weights = new Unity.Collections.NativeArray<BoneWeight1>(values.Select((w, i) =>
                    new BoneWeight1 { boneIndex = i, weight = w }).ToArray(), Unity.Collections.Allocator.Temp);
                mesh.SetBoneWeights(counts, weights);
                renderer.bones = bones;
                renderer.sharedMesh = mesh;
                var influences = CutOverlapProtection.ReadInfluences(renderer, mesh);
                CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, influences[0]);
                Assert.AreSame(mesh, renderer.sharedMesh);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(mesh);
            }
        }

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
