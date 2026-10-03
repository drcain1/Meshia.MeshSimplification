using System.Collections;
using Meshia.MeshSimplification.Editor;
using Meshia.MeshSimplification.Editor.Localization;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Meshia.MeshSimplification.Tests
{
    public class EditorLocalizationTests
    {
        public class Host : ScriptableObject
        {
            public MeshSimplifierOptions Options = MeshSimplifierOptions.Default;
            public MeshSimplificationTarget Target = new() { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 70000 };
        }

        public class TestWindow : EditorWindow { }

        [UnityTest]
        public IEnumerator ShouldSwitchAndReattachOptionsWithoutChangingSettings()
        {
            var previousLocale = LocalizationProvider.CurrentLocale;
            var host = ScriptableObject.CreateInstance<Host>();
            var window = ScriptableObject.CreateInstance<TestWindow>();
            try
            {
                LocalizationProvider.CurrentLocale = "en";
                using var serialized = new SerializedObject(host);
                var root = new MeshSimplifierOptionsDrawer().CreatePropertyGUI(serialized.FindProperty("Options"));
                window.rootVisualElement.Add(root);
                window.Show();
                for (var i = 0; i < 5; i++) yield return null;
                Assert.AreEqual(DisplayStyle.Flex, root.Q<DropdownField>("LanguagePicker").resolvedStyle.display,
                    "A standalone options drawer needs its own language picker.");
                var before = EditorJsonUtility.ToJson(host);
                LocalizationProvider.CurrentLocale = "ja";
                Assert.AreEqual("ボーンによる変形の保護", root.Q<Foldout>("SkinningProtectionGroup").text);
                Assert.AreEqual("変形するメッシュを自動保護", root.Q<Toggle>("SkinningProtectionAuto").label);
                Assert.AreEqual("境界エッジを保持", root.Q<Toggle>("PreserveBorderEdgesToggle").label);
                var deviation = root.Q<Slider>("SurfaceDeviationSlider");
                Assert.AreEqual("元の表面からのずれの上限", deviation.label);
                Assert.AreEqual("元の表面からのずれの上限", deviation.labelElement.text);
                var plane = root.Query<FloatField>().Where(f => f.bindingPath.EndsWith("FaQem.PlaneAreaWeight")).First();
                Assert.AreEqual("平面評価の面積除数", plane.labelElement.text);
                StringAssert.Contains("目標三角形数", deviation.tooltip);
                Assert.AreEqual(before, EditorJsonUtility.ToJson(host));

                root.RemoveFromHierarchy();
                LocalizationProvider.CurrentLocale = "en";
                Assert.AreEqual("ボーンによる変形の保護", root.Q<Foldout>("SkinningProtectionGroup").text);
                window.rootVisualElement.Add(root);
                for (var i = 0; i < 5; i++) yield return null;
                Assert.AreEqual("Skinning Protection", root.Q<Foldout>("SkinningProtectionGroup").text);
                Assert.AreEqual("Maximum Surface Deviation", deviation.label);
                Assert.AreEqual(before, EditorJsonUtility.ToJson(host));
                plane.value = -1f;
                Assert.AreEqual(0.000001f, host.Options.FaQem.PlaneAreaWeight);
                root.Q<FloatField>("SurfaceDeviationField").value = 0.5f;
                Assert.AreEqual(0.1f, host.Options.FaQem.MaxSurfaceDeviation);
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(host);
                LocalizationProvider.CurrentLocale = previousLocale;
            }
        }

        [UnityTest]
        public IEnumerator ShouldTuneSmallDeviationsWithoutClampingSavedLargerValues()
        {
            var host = ScriptableObject.CreateInstance<Host>();
            var window = ScriptableObject.CreateInstance<TestWindow>();
            try
            {
                host.Options.FaQem.MaxSurfaceDeviation = .02f;
                using var serialized = new SerializedObject(host);
                var root = new MeshSimplifierOptionsDrawer().CreatePropertyGUI(serialized.FindProperty("Options"));
                window.rootVisualElement.Add(root);
                window.Show();
                for (var i = 0; i < 5; i++) yield return null;
                var slider = root.Q<Slider>("SurfaceDeviationSlider");
                var field = root.Q<FloatField>("SurfaceDeviationField");
                Assert.AreEqual(.005f, slider.highValue);
                Assert.AreEqual(.005f, slider.value);
                Assert.AreEqual(.02f, field.value);
                Assert.AreEqual(.02f, host.Options.FaQem.MaxSurfaceDeviation);
                Undo.IncrementCurrentGroup();
                slider.value = .00051f;
                Assert.That(host.Options.FaQem.MaxSurfaceDeviation, Is.EqualTo(.00051f).Within(1e-8f));
                Assert.That(field.value, Is.EqualTo(.00051f).Within(1e-8f));
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                for (var i = 0; i < 5; i++) yield return null;
                Assert.AreEqual(.02f, host.Options.FaQem.MaxSurfaceDeviation);
                Assert.AreEqual(.02f, field.value);
                field.value = 0f;
                Assert.AreEqual(0f, host.Options.FaQem.MaxSurfaceDeviation);
                field.value = .00235f;
                Assert.AreEqual(.00235f, host.Options.FaQem.MaxSurfaceDeviation);
                Assert.AreEqual(.00235f, slider.value);
                field.value = float.NaN;
                Assert.AreEqual(.00235f, host.Options.FaQem.MaxSurfaceDeviation);
            }
            finally
            {
                Undo.ClearUndo(host);
                window.Close();
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator ShouldKeepEnumValuesAndUndoIndependentOfDisplayLanguage()
        {
            var previousLocale = LocalizationProvider.CurrentLocale;
            var host = ScriptableObject.CreateInstance<Host>();
            var window = ScriptableObject.CreateInstance<TestWindow>();
            try
            {
                LocalizationProvider.CurrentLocale = "ja";
                using var serialized = new SerializedObject(host);
                var root = new VisualElement();
                var field = new DropdownField("Target Kind");
                root.Add(field);
                LocalizationProvider.BindEnum(field, serialized.FindProperty("Target.Kind"));
                LocalizationProvider.Bind(root);
                window.rootVisualElement.Add(root);
                window.Show();
                for (var i = 0; i < 5; i++) yield return null;
                Assert.AreEqual("FA-QEMの目標三角形数", field.formatSelectedValueCallback(field.value));
                Undo.IncrementCurrentGroup();
                field.value = field.choices[(int)MeshSimplificationTargetKind.UvLoopDissolveTriangleCount];
                Assert.AreEqual(MeshSimplificationTargetKind.UvLoopDissolveTriangleCount, host.Target.Kind);
                LocalizationProvider.CurrentLocale = "en";
                Assert.AreEqual("Uv Loop Dissolve Triangle Count", field.value);
                Assert.AreEqual(70000, host.Target.Value);
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                var deadline = EditorApplication.timeSinceStartup + 2;
                while (field.value != "Fa Qem Triangle Count" && EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.AreEqual(MeshSimplificationTargetKind.FaQemTriangleCount, host.Target.Kind);
                Assert.AreEqual("Fa Qem Triangle Count", field.value);
            }
            finally
            {
                Undo.ClearUndo(host);
                window.Close();
                Object.DestroyImmediate(host);
                LocalizationProvider.CurrentLocale = previousLocale;
            }
        }
    }
}
