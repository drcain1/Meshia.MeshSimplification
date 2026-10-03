#nullable enable
using System;
using System.Threading.Tasks;
using Meshia.MeshSimplification.Ndmf.Editor.Preview;
using nadena.dev.ndmf.preview;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Meshia.MeshSimplification.Ndmf.Editor.Tests
{
    public class PreviewSkinningPolicyTests
    {
        [Test]
        public void JointSelectionResolvesHumanoidSlotsIndependentlyOfBorderSelection()
        {
            var root = new GameObject("Joint selection fixture");
            root.AddComponent<nadena.dev.ndmf.runtime.components.NDMFAvatarRoot>();
            Avatar? avatar = null;
            try
            {
                var skeleton = new System.Collections.Generic.List<Transform> { root.transform };
                Transform Bone(string name, Transform parent, Vector3 position)
                {
                    var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = position;
                    skeleton.Add(t); return t;
                }
                var hips = Bone("Hips", root.transform, new Vector3(0, 1, 0));
                var spine = Bone("Spine", hips, new Vector3(0, .2f, 0));
                var chest = Bone("Chest", spine, new Vector3(0, .2f, 0));
                var neck = Bone("Neck", chest, new Vector3(0, .15f, 0));
                Bone("Head", neck, new Vector3(0, .15f, 0));
                foreach (var side in new[] { "Left", "Right" })
                {
                    var sign = side == "Left" ? -1f : 1f;
                    var leg = Bone(side + "UpperLeg", hips, new Vector3(sign * .1f, -.05f, 0));
                    var lower = Bone(side + "LowerLeg", leg, new Vector3(0, -.4f, 0));
                    Bone(side + "Foot", lower, new Vector3(0, -.4f, .02f));
                    var arm = Bone(side + "UpperArm", chest, new Vector3(sign * .2f, .1f, 0));
                    var elbow = Bone(side + "LowerArm", arm, new Vector3(sign * .25f, 0, 0));
                    Bone(side + "Hand", elbow, new Vector3(sign * .2f, 0, 0));
                }
                var description = new HumanDescription
                {
                    human = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(System.Linq.Enumerable.Skip(skeleton, 1), t => new HumanBone
                    { boneName = t.name, humanName = t.name, limit = new HumanLimit { useDefaultValues = true } })),
                    skeleton = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(skeleton, t => new SkeletonBone
                    { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale })),
                    upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f,
                    armStretch = .05f, legStretch = .05f, feetSpacing = 0
                };
                avatar = AvatarBuilder.BuildHumanAvatar(root, description);
                Assert.That(avatar.isValid && avatar.isHuman, Is.True);
                var animator = root.AddComponent<Animator>(); animator.avatar = avatar;
                var child = new GameObject("Surface"); child.transform.SetParent(root.transform);
                var renderer = child.AddComponent<SkinnedMeshRenderer>();
                var left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                var right = animator.GetBoneTransform(HumanBodyBones.RightHand);
                renderer.bones = new[] { left, right, left, hips };
                var c = root.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
                var e = new MeshiaCascadingAvatarMeshSimplifierRendererEntry(renderer);
                e.Options.SkinningProtection.PreserveJointTransitions = true;
                e.Options.SkinningProtection.Policy = SkinningProtectionPolicy.Off;
                e.PreserveBorderEdgesBones = 0;
                e.PreserveJointTransitionsBones = 1ul << (int)HumanBodyBones.LeftHand;
                c.Entries.Add(e);
                var resolved = MeshiaCascadingAvatarMeshSimplifier.GetJointProtectionOptions(root, c, e);
                Assert.That(resolved.SkinningProtection.PreserveJointTransitions, Is.True);
                Assert.That(resolved.SkinningProtection.JointProtectionBoneIndices.Length, Is.EqualTo(2));
                Assert.That(resolved.SkinningProtection.JointProtectionBoneIndices[0], Is.EqualTo(0));
                Assert.That(resolved.SkinningProtection.JointProtectionBoneIndices[1], Is.EqualTo(2));
                Assert.That(e.Options.SkinningProtection.JointProtectionBoneIndices.Length, Is.Zero, "Resolution must not change serialized options.");
                e.PreserveJointTransitionsBones = 0;
                Assert.That(MeshiaCascadingAvatarMeshSimplifier.GetJointProtectionOptions(root, c, e).SkinningProtection.PreserveJointTransitions, Is.False);
                e.PreserveJointTransitionsBones = 1ul << (int)HumanBodyBones.Head;
                Assert.That(MeshiaCascadingAvatarMeshSimplifier.GetJointProtectionOptions(root, c, e).SkinningProtection.PreserveJointTransitions, Is.False);
                Object.DestroyImmediate(animator);
                e.PreserveJointTransitionsBones = MeshiaCascadingAvatarMeshSimplifierRendererEntry.DefaultHandBones;
                Assert.That(MeshiaCascadingAvatarMeshSimplifier.GetJointProtectionOptions(root, c, e).SkinningProtection.PreserveJointTransitions, Is.False);
            }
            finally { Object.DestroyImmediate(root); if (avatar != null) Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void StandaloneResetUsesFaQemAndAvoidsZeroTargetWithoutAMesh()
        {
            var root = new GameObject("Empty renderer", typeof(MeshRenderer));
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var empty = root.AddComponent<MeshiaMeshSimplifier>();
                var populated = cube.AddComponent<MeshiaMeshSimplifier>();
                Assert.That(empty.target.Kind, Is.EqualTo(MeshSimplificationTargetKind.FaQemTriangleCount));
                Assert.That(empty.target.Value, Is.EqualTo(70000));
                Assert.That(populated.target.Value, Is.EqualTo(6));
                Assert.That(populated.options, Is.EqualTo(MeshSimplifierOptions.AvatarInitial));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cube); }
        }

        [Test]
        public void NewEntriesStartWithDeformationOffWhileSavedEntriesKeepTheirSettings()
        {
            var root = new GameObject("Default options test");
            var renderer = root.AddComponent<MeshRenderer>();
            try
            {
                var entry = new MeshiaCascadingAvatarMeshSimplifierRendererEntry(renderer);
                Assert.That(entry.Algorithm, Is.EqualTo(MeshiaCascadingSimplificationAlgorithm.FaQem));
                Assert.That(entry.Options, Is.EqualTo(MeshSimplifierOptions.AvatarInitial));
                foreach (var bone in new[] { HumanBodyBones.LeftHand, HumanBodyBones.LeftThumbDistal,
                    HumanBodyBones.RightLowerArm, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightFoot })
                    Assert.That(entry.PreserveJointTransitionsBones & (1ul << (int)bone), Is.Not.Zero);
                entry.Options = MeshSimplifierOptions.Default;
                entry.Options.SkinningProtection.Policy = SkinningProtectionPolicy.Auto;
                entry.PreserveJointTransitionsBones = 0;
                var json = JsonUtility.ToJson(entry);
                var saved = JsonUtility.FromJson<MeshiaCascadingAvatarMeshSimplifierRendererEntry>(json);
                Assert.That(saved.Options, Is.EqualTo(entry.Options));
                Assert.That(saved.PreserveJointTransitionsBones, Is.Zero);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void AutomaticDeformationProtectionSelectsClothingIndependentlyAndRejectsRigidMeshes()
        {
            var root = new GameObject("Custom rig");
            var source = new Mesh();
            try
            {
                var a = new GameObject("A").transform; a.SetParent(root.transform);
                var b = new GameObject("B").transform; b.SetParent(root.transform);
                source.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                source.triangles = new[] { 0, 1, 2 };
                source.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
                source.boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1 },
                    new BoneWeight { boneIndex0 = 1, weight0 = 1 }, new BoneWeight { boneIndex0 = 1, weight0 = 1 } };
                foreach (var name in new[] { "Boots", "Separate sleeve", "Hair" })
                {
                    var obj = new GameObject(name); obj.transform.SetParent(root.transform);
                    var renderer = obj.AddComponent<SkinnedMeshRenderer>();
                    renderer.sharedMesh = source; renderer.bones = new[] { a, b };
                    Assert.That(Meshia.MeshSimplification.Ndmf.Editor.NdmfPlugin.HasDeformingSkinning(renderer), Is.True);
                    var resolved = Meshia.MeshSimplification.Ndmf.Editor.NdmfPlugin.ResolveOptions(root, renderer,
                        MeshSimplifierOptions.ConservativeAvatar);
                    Assert.That(resolved.SkinningProtection.Enabled, Is.True, "UV preview resolves per-mesh Auto without a humanoid animator.");
                    var manual = MeshSimplifierOptions.ConservativeAvatar;
                    manual.SkinningProtection.Policy = SkinningProtectionPolicy.Off;
                    Assert.That(Meshia.MeshSimplification.Ndmf.Editor.NdmfPlugin.ResolveOptions(root, renderer, manual)
                        .SkinningProtection.Enabled, Is.False);
                    renderer.bones = new[] { a, a };
                    Assert.That(Meshia.MeshSimplification.Ndmf.Editor.NdmfPlugin.HasDeformingSkinning(renderer), Is.False,
                        "Duplicate palette slots for one transform are not a deforming rig.");
                    renderer.bones = new Transform[] { null, null };
                    Assert.That(Meshia.MeshSimplification.Ndmf.Editor.NdmfPlugin.HasDeformingSkinning(renderer), Is.False);
                }
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(source); }
        }

        [TestCase(false, SkinningProtectionPolicy.AutoDeforming, false, false, false)]
        [TestCase(true, SkinningProtectionPolicy.AutoDeforming, false, false, false)]
        [TestCase(false, SkinningProtectionPolicy.On, false, false, false)]
        [TestCase(true, SkinningProtectionPolicy.On, false, false, false)]
        [TestCase(false, SkinningProtectionPolicy.Off, true, false, false)]
        [TestCase(true, SkinningProtectionPolicy.Off, true, false, false)]
        [TestCase(false, SkinningProtectionPolicy.Auto, true, false, false)]
        [TestCase(true, SkinningProtectionPolicy.Auto, true, false, false)]
        [TestCase(false, SkinningProtectionPolicy.Off, false, true, false)]
        [TestCase(true, SkinningProtectionPolicy.Off, false, true, false)]
        [TestCase(true, SkinningProtectionPolicy.On, true, true, true)]
        public async Task ShouldUseResolvedPolicyInPreview(bool cascading, SkinningProtectionPolicy policy, bool legacyEnabled, bool jointGuard, bool unprotected)
        {
            var root = new GameObject("Preview policy fixture");
            root.AddComponent<nadena.dev.ndmf.runtime.components.NDMFAvatarRoot>();
            var child = new GameObject("Skinned surface"); child.transform.SetParent(root.transform);
            var renderer = child.AddComponent<SkinnedMeshRenderer>();
            var source = new Mesh(); var expected = new Mesh();
            var proxyObject = new GameObject("Preview proxy");
            var proxy = proxyObject.AddComponent<SkinnedMeshRenderer>();
            var context = new ComputeContext("Preview skinning regression");
            IRenderFilterNode? node = null;
            try
            {
                const int width = 8;
                var vertices = new Vector3[width * width];
                var weights = new BoneWeight[vertices.Length];
                var triangles = new int[(width - 1) * (width - 1) * 6];
                for (var y = 0; y < width; y++) for (var x = 0; x < width; x++)
                {
                    var i = y * width + x; vertices[i] = new Vector3(x, y, 0);
                    weights[i] = new BoneWeight { boneIndex0 = (x + y) % 2, weight0 = 1f };
                }
                var n = 0;
                for (var y = 0; y < width - 1; y++) for (var x = 0; x < width - 1; x++)
                {
                    var a = y * width + x;
                    triangles[n++] = a; triangles[n++] = a + 1; triangles[n++] = a + width;
                    triangles[n++] = a + 1; triangles[n++] = a + width + 1; triangles[n++] = a + width;
                }
                source.vertices = vertices; source.triangles = triangles;
                source.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity }; source.boneWeights = weights;
                var boneA = new GameObject("Bone A").transform; boneA.SetParent(root.transform);
                var boneB = new GameObject("Bone B").transform; boneB.SetParent(root.transform);
                renderer.bones = proxy.bones = new[] { boneA, boneB };
                source.RecalculateNormals(); renderer.sharedMesh = proxy.sharedMesh = source;
                var options = MeshSimplifierOptions.Default;
                options.SkinningProtection.Policy = policy; options.SkinningProtection.Enabled = legacyEnabled;
                options.SkinningProtection.MaxWeightDistance = 0f;
                options.SkinningProtection.PreserveJointTransitions = jointGuard;
                var target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 30 };
                RenderGroup group;
                IRenderFilter filter;
                if (cascading)
                {
                    var c = root.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
                    c.Entries.Add(new MeshiaCascadingAvatarMeshSimplifierRendererEntry(renderer)
                    { Options = options, DisableProtections = unprotected, Algorithm = MeshiaCascadingSimplificationAlgorithm.FaQem,
                        TargetTriangleCount = 30, PreserveBorderEdgesBones = 0 });
                    group = RenderGroup.For(renderer).WithData((c, 0));
                    filter = new MeshiaCascadingAvatarMeshSimplifierPreview();
                }
                else
                {
                    var c = child.AddComponent<MeshiaMeshSimplifier>(); c.options = options; c.target = target;
                    group = RenderGroup.For(renderer); filter = new MeshiaMeshSimplifierPreview();
                }
                var resolved = options;
                // No humanoid animator: Auto must resolve to off regardless of
                // the serialized legacy flag, exactly as it does in a build.
                resolved.SkinningProtection = options.SkinningProtection.Resolve(policy == SkinningProtectionPolicy.AutoDeforming);
                if (cascading) resolved.SkinningProtection.PreserveJointTransitions = false;
                if (unprotected) resolved = resolved.WithoutProtections();
                MeshSimplifier.Simplify(source, target, resolved, expected);
                node = await filter.Instantiate(group, new[] { ((Renderer)renderer, (Renderer)proxy) }, context);
                node.OnFrame(renderer, proxy);
                CollectionAssert.AreEqual(expected.vertices, proxy.sharedMesh.vertices);
                CollectionAssert.AreEqual(expected.triangles, proxy.sharedMesh.triangles);
                if (!resolved.SkinningProtection.Enabled && !resolved.SkinningProtection.PreserveJointTransitions)
                    Assert.That(proxy.sharedMesh.triangles.Length, Is.LessThan(source.triangles.Length));
            }
            finally
            {
                context.Invalidate(); (node as IDisposable)?.Dispose();
                Object.DestroyImmediate(proxyObject); Object.DestroyImmediate(root);
                Object.DestroyImmediate(source); Object.DestroyImmediate(expected);
            }
        }
    }
}
