#if ENABLE_MODULAR_AVATAR
using System.Collections;
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
                Assert.AreEqual("FA-QEM", row.formatSelectedValueCallback(row.value));
                Assert.AreEqual(before, EditorJsonUtility.ToJson(component));
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
