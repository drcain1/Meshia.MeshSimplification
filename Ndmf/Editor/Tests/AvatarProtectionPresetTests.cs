#if ENABLE_MODULAR_AVATAR
using System.Collections.Generic;
using System.Linq;
using Meshia.MeshSimplification.Editor.Localization;
using Meshia.MeshSimplification.Ndmf.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Meshia.MeshSimplification.Ndmf.Tests
{
    public class AvatarProtectionPresetTests
    {
        [TestCase("Outfit", "Chest", false)]
        [TestCase("Hair_front", "Chest", true)]
        [TestCase("Outfit", "Hair_root", true)]
        [TestCase("髪", "頭", true)]
        [TestCase("Body_base", "Hand.L", true)]
        [TestCase("Glove", "Thumb Proximal.L", true)]
        [TestCase("Outfit", "Upper_arm.L", false)]
        [TestCase("Body", "Head", true)]
        [TestCase("Face", "Head", true)]
        public void AggressiveDetectionUsesNamesAndPositiveWeights(string rendererName, string usedBoneName, bool expected)
        {
            var root = new GameObject(rendererName);
            var used = new GameObject(usedBoneName); used.transform.SetParent(root.transform);
            var unused = new GameObject("Hand.R"); unused.transform.SetParent(root.transform);
            var mesh = new Mesh { name = "Test mesh", vertices = new[] { Vector3.zero, Vector3.up, Vector3.right },
                triangles = new[] { 0, 1, 2 }, bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity },
                boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, 3).ToArray() };
            var skin = root.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh;
            skin.bones = new[] { used.transform, unused.transform };
            try
            {
                Assert.AreEqual(expected, AvatarProtectionPreset.NeedsProtection(skin,
                    new HashSet<Transform> { unused.transform }, new HashSet<string> { "handr" }));
                Assert.IsTrue(AvatarProtectionPreset.NeedsProtection(skin,
                    new HashSet<Transform> { used.transform }, new HashSet<string>()), "Actual humanoid mappings work with arbitrary bone names.");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(mesh); }
        }

        [TestCase(false, "ja", "積極的")]
        [TestCase(false, "en", "Aggressive")]
        [TestCase(true, "ja", "強力")]
        [TestCase(true, "en", "Extreme")]
        public void ReductionPresetPreservesAllocationsAndSupportsUndo(bool extreme, string language, string label)
        {
            var locale = LocalizationProvider.CurrentLocale;
            var root = new GameObject("Preset avatar", typeof(nadena.dev.ndmf.runtime.components.NDMFAvatarRoot));
            foreach (var name in new[] { "Hair_front", "Outfit", "Body" })
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = name; cube.transform.SetParent(root.transform);
            }
            var holder = new GameObject("Settings"); holder.transform.SetParent(root.transform);
            var component = holder.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            component.AutoAdjustEnabled = false; component.RefreshEntries();
            UnityEditor.Editor inspector = null;
            try
            {
                foreach (var entry in component.Entries)
                {
                    entry.Algorithm = MeshiaCascadingSimplificationAlgorithm.BlenderDecimate;
                    entry.TargetTriangleCount = 7; entry.Fixed = true;
                    entry.DisableProtections = true;
                    entry.PreserveJointTransitionsBones = 0;
                    Assert.AreEqual(SkinningProtectionPolicy.Off, entry.Options.SkinningProtection.Policy);
                }
                Assert.IsFalse(component.Entries[2].Enabled);
                inspector = UnityEditor.Editor.CreateEditor(component);
                LocalizationProvider.CurrentLocale = language;
                var ui = inspector.CreateInspectorGUI();
                var button = ui.Q<Button>(extreme ? "ExtremeDefaultsButton" : "AggressiveDefaultsButton");
                Assert.AreEqual(label, button.text);
                var before = EditorJsonUtility.ToJson(component);
                Undo.IncrementCurrentGroup();
                typeof(Clickable).GetMethod("SimulateSingleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(button.clickable, new object[] { null, 0 });
                var protectedOptions = extreme ? MeshSimplifierOptions.ExtremeAvatar : MeshSimplifierOptions.ConservativeAvatar;
                var geometryOptions = protectedOptions;
                geometryOptions.SkinningProtection.Policy = SkinningProtectionPolicy.Off;
                geometryOptions.SkinningProtection.Enabled = false;
                geometryOptions.SkinningProtection.PreserveJointTransitions = false;
                Assert.AreEqual(protectedOptions, component.Entries[0].Options);
                Assert.AreEqual(geometryOptions, component.Entries[1].Options);
                Assert.AreEqual(protectedOptions, component.Entries[2].Options);
                Assert.IsFalse(component.Entries[2].Enabled, "A preset must not enable an excluded face.");
                foreach (var entry in component.Entries)
                {
                    Assert.AreEqual(7, entry.TargetTriangleCount);
                    Assert.IsTrue(entry.Fixed);
                    Assert.AreEqual(MeshiaCascadingSimplificationAlgorithm.BlenderDecimate, entry.Algorithm);
                    Assert.IsFalse(entry.DisableProtections);
                    Assert.AreEqual(MeshiaCascadingAvatarMeshSimplifierRendererEntry.DefaultJointBones, entry.PreserveJointTransitionsBones);
                    if (entry.Options.SkinningProtection.PreserveJointTransitions)
                    {
                        foreach (var bone in new[] { HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
                            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftHand, HumanBodyBones.RightFoot })
                            Assert.AreNotEqual(0ul, entry.PreserveJointTransitionsBones & (1ul << (int)bone),
                                "Reduction presets must retain elbow, knee, wrist and ankle coverage.");
                    }
                    Assert.AreEqual(extreme ? .001f : .0005f, entry.Options.FaQem.MaxSurfaceDeviation);
                    Assert.IsTrue(entry.Options.PreserveBorderEdges);
                    Assert.IsTrue(entry.Options.FaQem.PreserveAttributeSeams);
                    Assert.IsTrue(entry.Options.FaQem.ExperimentalUvEnabled);
                }
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Assert.AreEqual(before, EditorJsonUtility.ToJson(component));
            }
            finally
            {
                Undo.ClearUndo(component);
                if (inspector != null) Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(root); LocalizationProvider.CurrentLocale = locale;
            }
        }

        [Test]
        public void ExtremeRelaxesLimitsWithoutDisablingGeometryGuardsOrChangingDefaults()
        {
            var originalDefaults = MeshSimplifierOptions.Default;
            var originalInitial = MeshSimplifierOptions.AvatarInitial;
            var options = MeshSimplifierOptions.ExtremeAvatar;
            Assert.AreEqual(.25f, options.SkinningProtection.Strength);
            Assert.AreEqual(.25f, options.SkinningProtection.MaxWeightDistance);
            Assert.AreEqual(.1f, options.SkinningProtection.MaxDiscardedWeight);
            Assert.IsTrue(options.SkinningProtection.Resolve(true).Enabled);
            Assert.IsTrue(options.SkinningProtection.PreserveJointTransitions);
            Assert.AreEqual(.001f, options.FaQem.MaxSurfaceDeviation);
            Assert.AreEqual(.0005f, MeshSimplifierOptions.ConservativeAvatar.FaQem.MaxSurfaceDeviation);
            Assert.IsFalse(options.AllowUnsafeGeometry);
            Assert.IsTrue(options.PreserveBorderEdges);
            Assert.IsTrue(options.FaQem.PreserveAttributeSeams);
            Assert.AreEqual(.2f, options.FaQem.MinNormalDot);
            Assert.IsTrue(options.FaQem.ExperimentalUvEnabled);
            Assert.IsTrue(options.FaQem.ExperimentalJointUv);
            Assert.AreEqual(5000f, options.FaQem.ExperimentalUvWeight);
            Assert.DoesNotThrow(() => options.SkinningProtection.Validate());
            Assert.DoesNotThrow(() => options.FaQem.Validate());
            Assert.AreEqual(originalDefaults, MeshSimplifierOptions.Default);
            Assert.AreEqual(originalInitial, MeshSimplifierOptions.AvatarInitial);
            Assert.AreEqual(2f, MeshSimplifierOptions.ConservativeAvatar.SkinningProtection.Strength);
        }
    }
}
#endif
