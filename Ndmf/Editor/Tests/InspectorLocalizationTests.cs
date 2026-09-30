#if ENABLE_MODULAR_AVATAR
using System.Collections;
using System.Linq;
using Meshia.MeshSimplification.Editor.Localization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Meshia.MeshSimplification.Ndmf.Tests
{
    public class InspectorLocalizationTests
    {
        public class TestWindow : EditorWindow { }

        [UnityTest]
        public IEnumerator ShouldTranslateBoundOptionsOnCreationAndLanguageChanges()
        {
            var locale = LocalizationProvider.CurrentLocale;
            var meshObject = new GameObject("Options localization test", typeof(MeshRenderer));
            var component = meshObject.AddComponent<MeshiaMeshSimplifier>();
            var window = ScriptableObject.CreateInstance<TestWindow>();
            UnityEditor.Editor inspector = null;
            try
            {
                // Start in Japanese: translating an already generated field can conceal
                // labels overwritten by Unity during the initial serialized binding.
                LocalizationProvider.CurrentLocale = "ja";
                var before = EditorJsonUtility.ToJson(component);
                inspector = UnityEditor.Editor.CreateEditor(component);
                var root = inspector.CreateInspectorGUI();
                window.rootVisualElement.Add(root);
                window.Show();
                foreach (var language in new[] { "ja", "en", "ja" })
                {
                    LocalizationProvider.CurrentLocale = language;
                    for (var i = 0; i < 10; i++) yield return null;
                    AssertOption<float>(root, "SkinningProtection.Strength", "Protection Strength", "保護の強さ", language);
                    AssertOption<float>(root, "SkinningProtection.MaxWeightDistance", "Maximum Skin Weight Distance", "ボーンウェイト差の上限", language);
                    AssertOption<float>(root, "SkinningProtection.MaxDiscardedWeight", "Maximum Discarded Skin Weight", "破棄するボーンウェイトの上限", language);
                    AssertOption<bool>(root, "SkinningProtection.PreserveJointTransitions", "Preserve Joint Transitions", "関節付近の頂点を保持", language);
                    AssertOption<bool>(root, "FaQem.UseInverseAreaWeighting", "Use Inverse Area Weighting", "面積の逆数による重み付け", language);
                    AssertOption<bool>(root, "FaQem.PreserveAttributeSeams", "Preserve Attribute Seams", "属性の継ぎ目を保持", language);
                    AssertOption<bool>(root, "PreserveBorderEdges", "Preserve Border Edges", "境界エッジを保持", language);
                    Assert.AreEqual(before, EditorJsonUtility.ToJson(component), "Language changes must not change simplification settings.");
                }

                // The translated controls must still write to the correct properties.
                FindOption<bool>(root, "SkinningProtection.PreserveJointTransitions").value = true;
                FindOption<float>(root, "SkinningProtection.Strength").value = 2f;
                FindOption<float>(root, "SkinningProtection.MaxWeightDistance").value = .3f;
                FindOption<float>(root, "SkinningProtection.MaxDiscardedWeight").value = .15f;
                FindOption<bool>(root, "FaQem.UseInverseAreaWeighting").value = false;
                FindOption<bool>(root, "FaQem.PreserveAttributeSeams").value = false;
                for (var i = 0; i < 10; i++) yield return null;
                Assert.IsTrue(component.options.SkinningProtection.PreserveJointTransitions);
                Assert.AreEqual(2f, component.options.SkinningProtection.Strength);
                Assert.AreEqual(.3f, component.options.SkinningProtection.MaxWeightDistance);
                Assert.AreEqual(.15f, component.options.SkinningProtection.MaxDiscardedWeight);
                Assert.IsFalse(component.options.FaQem.UseInverseAreaWeighting);
                Assert.IsFalse(component.options.FaQem.PreserveAttributeSeams);
            }
            finally
            {
                Undo.ClearUndo(component);
                window.Close();
                if (inspector != null) Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(meshObject);
                LocalizationProvider.CurrentLocale = locale;
            }
        }

        [UnityTest]
        public IEnumerator ConservativePresetTranslatesAndSupportsUndoWithoutChangingAllocations()
        {
            var locale = LocalizationProvider.CurrentLocale;
            var avatar = new GameObject("Preset test");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(avatar.transform);
            var child = new GameObject("Settings"); child.transform.SetParent(avatar.transform);
            var component = child.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            component.AutoAdjustEnabled = false; component.RefreshEntries();
            var entry = component.Entries.Single();
            entry.Options = MeshSimplifierOptions.Default; entry.PreserveJointTransitionsBones = 0;
            entry.TargetTriangleCount = 7; entry.Fixed = true; entry.Enabled = false;
            var window = ScriptableObject.CreateInstance<TestWindow>();
            UnityEditor.Editor inspector = null;
            try
            {
                LocalizationProvider.CurrentLocale = "ja";
                inspector = UnityEditor.Editor.CreateEditor(component);
                var root = inspector.CreateInspectorGUI(); window.rootVisualElement.Add(root); window.Show();
                for (var i = 0; i < 10; i++) yield return null;
                var button = root.Q<Button>("ConservativeDefaultsButton");
                Assert.That(button.text, Is.EqualTo("全メッシュに保守的な初期設定を適用"));
                var before = EditorJsonUtility.ToJson(component);
                typeof(Clickable).GetMethod("SimulateSingleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(button.clickable, new object[] { null, 0 });
                for (var i = 0; i < 10; i++) yield return null;
                Assert.That(component.Entries[0].Options, Is.EqualTo(MeshSimplifierOptions.ConservativeAvatar));
                Assert.That(component.Entries[0].PreserveJointTransitionsBones,
                    Is.EqualTo(MeshiaCascadingAvatarMeshSimplifierRendererEntry.DefaultJointBones));
                Assert.That(component.Entries[0].TargetTriangleCount, Is.EqualTo(7));
                Assert.That(component.Entries[0].Enabled, Is.False);
                Assert.That(component.Entries[0].Fixed, Is.True);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                for (var i = 0; i < 10; i++) yield return null;
                Assert.That(EditorJsonUtility.ToJson(component), Is.EqualTo(before));
                LocalizationProvider.CurrentLocale = "en";
                Assert.That(button.text, Is.EqualTo("Apply Conservative Defaults to All Meshes"));
            }
            finally
            {
                Undo.ClearUndo(component); window.Close();
                if (inspector != null) Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(avatar); LocalizationProvider.CurrentLocale = locale;
            }
        }

        private static BaseField<T> FindOption<T>(VisualElement root, string path)
        {
            return root.Query<BaseField<T>>().ToList().Single(field => !string.IsNullOrEmpty(field.bindingPath) && field.bindingPath.EndsWith(path));
        }

        private static void AssertOption<T>(VisualElement root, string path, string english, string japanese, string locale)
        {
            var field = FindOption<T>(root, path);
            var expected = locale == "ja" ? japanese : english;
            Assert.AreEqual(expected, field.label, path);
            Assert.AreEqual(expected, field.labelElement.text, path + " visible label");
        }

        [UnityTest]
        public IEnumerator ShouldTranslateGlobalAndPerMeshControlsWithoutChangingTargets()
        {
            var locale = LocalizationProvider.CurrentLocale;
            var avatar = new GameObject("Localization test avatar");
            var meshObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            meshObject.transform.SetParent(avatar.transform);
            var child = new GameObject("Simplifier");
            child.transform.SetParent(avatar.transform);
            var component = child.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            component.AutoAdjustEnabled = false;
            var window = ScriptableObject.CreateInstance<TestWindow>();
            UnityEditor.Editor inspector = null;
            try
            {
                LocalizationProvider.CurrentLocale = "en";
                inspector = UnityEditor.Editor.CreateEditor(component);
                var root = inspector.CreateInspectorGUI();
                window.rootVisualElement.Add(root);
                window.Show();
                for (var i = 0; i < 10; i++) yield return null;
                Assert.AreEqual(1, component.Entries.Count);
                var before = EditorJsonUtility.ToJson(component);
                LocalizationProvider.CurrentLocale = "ja";
                var all = root.Q<DropdownField>("AllMeshesAlgorithmField");
                Assert.AreEqual("全メッシュのアルゴリズム", all.label);
                Assert.AreEqual("NDMFビルドを解析", root.Q<Button>("AnalyzeNdmfBuildButton").text);
                var row = root.Q<DropdownField>("AlgorithmField");
                Assert.IsNotNull(row);
                Assert.AreEqual("アルゴリズム", row.label);
                Assert.AreEqual("関節を保護するボーン", root.Q<Foldout>("PreserveJointTransitionsBonesFoldout").text);
                Assert.AreEqual("FA-QEM", row.formatSelectedValueCallback(row.value));
                Assert.AreEqual(before, EditorJsonUtility.ToJson(component));
                var jointToggle = root.Q<Foldout>("PreserveJointTransitionsBonesFoldout").Children().OfType<Toggle>().First();
                var jointMask = component.Entries[0].PreserveJointTransitionsBones;
                var borderMask = component.Entries[0].PreserveBorderEdgesBones;
                Undo.IncrementCurrentGroup();
                jointToggle.value = true; // Hips is unselected in the default hand/finger mask.
                Assert.AreEqual(jointMask | (1ul << (int)HumanBodyBones.Hips), component.Entries[0].PreserveJointTransitionsBones);
                Assert.AreEqual(borderMask, component.Entries[0].PreserveBorderEdgesBones);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                var jointDeadline = EditorApplication.timeSinceStartup + 2;
                while (EditorApplication.timeSinceStartup < jointDeadline)
                {
                    jointToggle = root.Q<Foldout>("PreserveJointTransitionsBonesFoldout").Children().OfType<Toggle>().First();
                    if (!jointToggle.value) break;
                    yield return null;
                }
                Assert.AreEqual(jointMask, component.Entries[0].PreserveJointTransitionsBones);
                Assert.IsFalse(jointToggle.value, "Undo must refresh the visible joint selection.");
                Undo.IncrementCurrentGroup();
                all.value = "Meshia";
                Assert.AreEqual(MeshiaCascadingSimplificationAlgorithm.Meshia, component.Entries[0].Algorithm);
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                var deadline = EditorApplication.timeSinceStartup + 2;
                while ((all.value != "Fa Qem" || root.Q<DropdownField>("AlgorithmField").value != "Fa Qem") &&
                    EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.AreEqual(MeshiaCascadingSimplificationAlgorithm.FaQem, component.Entries[0].Algorithm);
                row = root.Q<DropdownField>("AlgorithmField");
                Assert.AreEqual("Fa Qem", row.value);
                row.value = "Uv Loop Dissolve";
                Assert.AreEqual(MeshiaCascadingSimplificationAlgorithm.UvLoopDissolve, component.Entries[0].Algorithm);
                LocalizationProvider.CurrentLocale = "en";
                Assert.AreEqual("Algorithm for All Meshes", all.label);
                Assert.AreEqual("Uv Loop Dissolve", row.value);
                Assert.AreEqual(70000, component.TargetTriangleCount);
            }
            finally
            {
                Undo.ClearUndo(component);
                window.Close();
                if (inspector != null) Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(avatar);
                LocalizationProvider.CurrentLocale = locale;
            }
        }
    }
}
#endif
