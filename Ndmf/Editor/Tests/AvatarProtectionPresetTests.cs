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

        [Test]
        public void AggressivePresetPreservesAllocationsAndSupportsUndo()
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
                    Assert.AreEqual(SkinningProtectionPolicy.Off, entry.Options.SkinningProtection.Policy);
                }
                Assert.IsFalse(component.Entries[2].Enabled);
                inspector = UnityEditor.Editor.CreateEditor(component);
                LocalizationProvider.CurrentLocale = "ja";
                var ui = inspector.CreateInspectorGUI();
                var button = ui.Q<Button>("AggressiveDefaultsButton");
                Assert.AreEqual("積極的", button.text);
                var before = EditorJsonUtility.ToJson(component);
                Undo.IncrementCurrentGroup();
                typeof(Clickable).GetMethod("SimulateSingleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(button.clickable, new object[] { null, 0 });
                Assert.AreEqual(MeshSimplifierOptions.ConservativeAvatar, component.Entries[0].Options);
                Assert.AreEqual(MeshSimplifierOptions.AvatarInitial, component.Entries[1].Options);
                Assert.AreEqual(MeshSimplifierOptions.ConservativeAvatar, component.Entries[2].Options);
                Assert.IsFalse(component.Entries[2].Enabled, "A preset must not enable an excluded face.");
                foreach (var entry in component.Entries)
                {
                    Assert.AreEqual(7, entry.TargetTriangleCount);
                    Assert.IsTrue(entry.Fixed);
                    Assert.AreEqual(MeshiaCascadingSimplificationAlgorithm.BlenderDecimate, entry.Algorithm);
                    Assert.AreEqual(.0005f, entry.Options.FaQem.MaxSurfaceDeviation);
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
    }
}
#endif
