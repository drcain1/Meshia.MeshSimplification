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
        public IEnumerator LanguageChangesPreserveMeasuredCountsAndOnlyShowOnePicker()
        {
            var locale = LocalizationProvider.CurrentLocale;
            var avatar = new GameObject("Language freshness test", typeof(nadena.dev.ndmf.runtime.components.NDMFAvatarRoot));
            GameObject.CreatePrimitive(PrimitiveType.Cube).transform.SetParent(avatar.transform);
            var child = new GameObject("Settings"); child.transform.SetParent(avatar.transform);
            var component = child.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            component.RefreshEntries(); component.AutoAdjustEnabled = false;
            component.Entries[0].Options.FaQem.ExperimentalUvEnabled = false;
            component.Entries[0].Options.FaQem.ExperimentalJointUv = false;
            var inspector = UnityEditor.Editor.CreateEditor(component);
            var window = ScriptableObject.CreateInstance<TestWindow>();
            const System.Reflection.BindingFlags inst = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            const System.Reflection.BindingFlags stat = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var type = inspector.GetType();
            var key = (string)type.GetMethod("GetBuildAnalysisResultKey", stat).Invoke(null, new object[] { component });
            try
            {
                LocalizationProvider.CurrentLocale = "en";
                var root = inspector.CreateInspectorGUI();
                window.rootVisualElement.Add(root); window.Show();
                for (var i = 0; i < 10; i++) yield return null;
                root.Q<Toggle>("OptionsToggle").value = true;
                for (var i = 0; i < 10; i++) yield return null;
                MeasuredMeshBudgetTests.Seed(inspector, 12, _ => 12);
                var before = EditorJsonUtility.ToJson(component);
                var revision = type.GetProperty("CurrentAnalysisRevision", stat).GetValue(null);
                var inputs = type.GetField("meshInputRevision", stat).GetValue(null);
                var changes = new System.Collections.Generic.List<string>();
                root.RegisterCallback<UnityEditor.UIElements.SerializedPropertyChangeEvent>(evt => changes.Add(evt.changedProperty.propertyPath));
                foreach (var language in new[] { "ja", "en" })
                {
                    root.Q<DropdownField>("LanguagePicker").value = language;
                    for (var i = 0; i < 10; i++) yield return null;
                    Undo.FlushUndoRecordObjects();
                    Assert.AreEqual(before, EditorJsonUtility.ToJson(component));
                    var objectField = root.Q<UnityEditor.UIElements.ObjectField>("TargetObjectField");
                    var expectedRenderer = avatar.GetComponentInChildren<Renderer>();
                    Assert.AreSame(expectedRenderer, objectField.value);
                    StringAssert.Contains(expectedRenderer.name,
                        string.Join(" | ", objectField.Query<TextElement>().ToList().Select(label => label.text)),
                        "Language changes must preserve the displayed renderer name, not just the reference.");

                    Assert.AreEqual(revision, type.GetProperty("CurrentAnalysisRevision", stat).GetValue(null),
                        "Changing labels invalidated analysis. Property events: " + string.Join(", ", changes));
                    Assert.AreEqual(inputs, type.GetField("meshInputRevision", stat).GetValue(null));
                    Assert.AreEqual(1, root.Query<DropdownField>("LanguagePicker").ToList().Count(x => x.resolvedStyle.display != DisplayStyle.None));
                    Assert.IsTrue(root.Q<IntegerField>("TargetTriangleCountField").enabledInHierarchy);
                }
                var protection = root.Q<Toggle>("PreserveBorderEdgesToggle");
                protection.value = !protection.value;
                for (var i = 0; i < 5; i++) yield return null;
                Undo.FlushUndoRecordObjects();
                Assert.AreNotEqual(revision, type.GetProperty("CurrentAnalysisRevision", stat).GetValue(null), "Real mesh edits must still invalidate analysis.");
                Assert.AreNotEqual(inputs, type.GetField("meshInputRevision", stat).GetValue(null), "Real protection edits must discard cached inputs.");
                revision = type.GetProperty("CurrentAnalysisRevision", stat).GetValue(null);
                inputs = type.GetField("meshInputRevision", stat).GetValue(null);
                root.Q<Toggle>("ExperimentalUvToggle").value = true;
                for (var i = 0; i < 5; i++) yield return null;
                Undo.FlushUndoRecordObjects();
                Assert.AreNotEqual(revision, type.GetProperty("CurrentAnalysisRevision", stat).GetValue(null), "UV protection must invalidate the last build.");
                Assert.AreNotEqual(inputs, type.GetField("meshInputRevision", stat).GetValue(null), "UV protection must invalidate count profiles.");
                inputs = type.GetField("meshInputRevision", stat).GetValue(null);
                root.Q<FloatField>("ExperimentalUvWeightField").value = 100;
                for (var i = 0; i < 5; i++) yield return null;
                Undo.FlushUndoRecordObjects();
                Assert.AreNotEqual(inputs, type.GetField("meshInputRevision", stat).GetValue(null), "UV strength must invalidate count profiles too.");
            }
            finally
            {
                window.Close();
                ((System.Collections.IDictionary)type.GetField("BuildAnalysisCache", stat).GetValue(null)).Remove(key);
                SessionState.EraseString(key); Undo.ClearUndo(component);
                Object.DestroyImmediate(inspector); Object.DestroyImmediate(avatar);
                LocalizationProvider.CurrentLocale = locale;
            }
        }

        [UnityTest]
        public IEnumerator ObjectFieldLocalizationPreservesLateBoundAndReusedValues()
        {
            var locale = LocalizationProvider.CurrentLocale;
            var first = new GameObject("First renderer");
            var second = new GameObject("Second renderer");
            var window = ScriptableObject.CreateInstance<TestWindow>();
            try
            {
                LocalizationProvider.CurrentLocale = "en";
                var root = new VisualElement();
                var field = new UnityEditor.UIElements.ObjectField("Options")
                {
                    objectType = typeof(GameObject), tooltip = "Show mesh options"
                };
                root.Add(field);
                // ListView rows are localized before bindItem assigns their renderer.
                LocalizationProvider.Bind(root);
                window.rootVisualElement.Add(root); window.Show();
                foreach (var current in new[] { first, second })
                {
                    field.SetValueWithoutNotify(current);
                    var valueChanges = 0;
                    EventCallback<ChangeEvent<Object>> onChange = _ => valueChanges++;
                    field.RegisterValueChangedCallback(onChange);
                    foreach (var language in new[] { "ja", "en", "ja" })
                    {
                        LocalizationProvider.CurrentLocale = language;
                        root.RemoveFromHierarchy();
                        window.rootVisualElement.Add(root);
                        for (var i = 0; i < 3; i++) yield return null;
                        Assert.AreSame(current, field.value);
                        StringAssert.Contains(current.name,
                            string.Join(" | ", field.Query<TextElement>().ToList().Select(label => label.text)));
                        Assert.AreEqual(LocalizationProvider.Tr("Options"), field.label);
                        Assert.AreEqual(LocalizationProvider.Tr("Show mesh options"), field.tooltip);
                        Assert.AreEqual(0, valueChanges);
                    }
                    field.UnregisterValueChangedCallback(onChange);
                }
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(first); Object.DestroyImmediate(second);
                LocalizationProvider.CurrentLocale = locale;
            }
        }

        [UnityTest]
        public IEnumerator ExperimentalUvCheckboxKeepsStrengthAndSupportsUndoAndReset()
        {
            var obj = new GameObject("UV checkbox test", typeof(MeshRenderer));
            var component = obj.AddComponent<MeshiaMeshSimplifier>();
            component.options.FaQem.ExperimentalUvWeight = 0; // Old serialized options.
            component.options.FaQem.ExperimentalUvEnabled = false;
            component.options.FaQem.ExperimentalJointUv = false;
            var inspector = UnityEditor.Editor.CreateEditor(component);
            var window = ScriptableObject.CreateInstance<TestWindow>();
            try
            {
                var root = inspector.CreateInspectorGUI();
                window.rootVisualElement.Add(root); window.Show();
                for (var i = 0; i < 10; i++) yield return null;
                var toggle = root.Q<Toggle>("ExperimentalUvToggle");
                var weight = root.Q<FloatField>("ExperimentalUvWeightField");
                var controls = root.Q<VisualElement>("UvPreservationControls");
                var preset = root.Q<DropdownField>("UvStrengthPreset");
                Assert.IsFalse(component.options.FaQem.ExperimentalUvEnabled);
                Assert.AreEqual(0, component.options.FaQem.ExperimentalUvWeight, "Opening the UI must not edit settings.");
                Assert.AreEqual(DisplayStyle.None, controls.style.display.value);
                toggle.value = true;
                for (var i = 0; i < 5; i++) yield return null;
                Assert.IsTrue(component.options.FaQem.ExperimentalUvEnabled);
                Assert.AreEqual(5000, component.options.FaQem.ExperimentalUvWeight);
                Assert.AreEqual(DisplayStyle.Flex, controls.style.display.value);
                Assert.IsTrue(component.options.FaQem.ExperimentalJointUv);
                weight.value = 250;
                for (var i = 0; i < 5; i++) yield return null;
                Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
                toggle.value = false;
                for (var i = 0; i < 5; i++) yield return null;
                Undo.FlushUndoRecordObjects();
                Assert.IsFalse(component.options.FaQem.ExperimentalUvEnabled);
                Assert.AreEqual(250, component.options.FaQem.ExperimentalUvWeight);
                Assert.IsTrue(component.options.FaQem.ExperimentalJointUv);
                Assert.AreEqual(DisplayStyle.None, controls.style.display.value);
                Undo.PerformUndo();
                for (var i = 0; i < 5; i++) yield return null;
                Assert.IsTrue(component.options.FaQem.ExperimentalUvEnabled);
                Assert.AreEqual(250, component.options.FaQem.ExperimentalUvWeight);
                Assert.IsTrue(component.options.FaQem.ExperimentalJointUv);
                Assert.AreEqual(DisplayStyle.Flex, controls.style.display.value);
                var reset = root.Q<Button>("ResetOptionsButton");
                typeof(Clickable).GetMethod("SimulateSingleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(reset.clickable, new object[] { null, 0 });
                for (var i = 0; i < 5; i++) yield return null;
                Assert.IsTrue(component.options.FaQem.ExperimentalUvEnabled);
                Assert.IsTrue(component.options.FaQem.ExperimentalJointUv);
                Assert.AreEqual(5000, component.options.FaQem.ExperimentalUvWeight);
                Assert.AreEqual(DisplayStyle.Flex, controls.style.display.value);
            }
            finally
            {
                window.Close(); Undo.ClearUndo(component);
                Object.DestroyImmediate(inspector); Object.DestroyImmediate(obj);
            }
        }

        [UnityTest]
        public IEnumerator TexturePresetsPreserveLegacyCustomValuesAndSupportUndo()
        {
            var locale = LocalizationProvider.CurrentLocale;
            var obj = new GameObject("Legacy UV settings", typeof(MeshRenderer));
            var component = obj.AddComponent<MeshiaMeshSimplifier>();
            component.options.FaQem.ExperimentalUvEnabled = true;
            component.options.FaQem.ExperimentalJointUv = false;
            component.options.FaQem.ExperimentalUvWeight = 250;
            var inspector = UnityEditor.Editor.CreateEditor(component);
            var window = ScriptableObject.CreateInstance<TestWindow>();
            try
            {
                var before = EditorJsonUtility.ToJson(component);
                var root = inspector.CreateInspectorGUI();
                window.rootVisualElement.Add(root);
                window.Show();
                for (var i = 0; i < 10; i++) yield return null;
                var preset = root.Q<DropdownField>("UvStrengthPreset");
                var slider = root.Q<SliderInt>("UvStrengthSlider");
                Assert.IsNull(root.Q<Foldout>("UvAdvancedOptions"));
                Assert.IsNull(root.Q<DropdownField>("UvMethodField"));
                Assert.IsNull(root.Q<Toggle>("ExperimentalJointUvToggle"));
                foreach (var language in new[] { "ja", "en" })
                {
                    LocalizationProvider.CurrentLocale = language;
                    for (var i = 0; i < 5; i++) yield return null;
                    Assert.AreEqual(before, EditorJsonUtility.ToJson(component));
                    Assert.AreEqual("Custom", preset.value);
                    Assert.IsFalse(component.options.FaQem.ExperimentalJointUv);
                    Assert.AreEqual(250, root.Q<FloatField>("ExperimentalUvWeightField").value);
                }
                Assert.AreEqual(before, EditorJsonUtility.ToJson(component));
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                // A custom value nearest Low must still be selectable as Low.
                preset.value = "Low";
                for (var i = 0; i < 5; i++) yield return null;
                Undo.FlushUndoRecordObjects();
                Assert.AreEqual(1000, component.options.FaQem.ExperimentalUvWeight);
                Assert.IsFalse(component.options.FaQem.ExperimentalJointUv);
                Undo.PerformUndo();
                for (var i = 0; i < 5; i++) yield return null;
                Assert.AreEqual(before, EditorJsonUtility.ToJson(component));
                Assert.AreEqual("Custom", preset.value);
                slider.value = 1;
                for (var i = 0; i < 5; i++) yield return null;
                Assert.AreEqual(5000, component.options.FaQem.ExperimentalUvWeight);
                Assert.AreEqual("Medium", preset.value);
                slider.value = 2;
                for (var i = 0; i < 5; i++) yield return null;
                Assert.AreEqual(10000, component.options.FaQem.ExperimentalUvWeight);
                Assert.AreEqual("High", preset.value);
                var number = root.Q<FloatField>("ExperimentalUvWeightField");
                Assert.AreEqual(10000, number.value);
                number.value = 7500;
                for (var i = 0; i < 5; i++) yield return null;
                Assert.AreEqual(7500, component.options.FaQem.ExperimentalUvWeight);
                Assert.AreEqual("Custom", preset.value);
                Assert.IsFalse(component.options.FaQem.ExperimentalJointUv);
                var toggle = root.Q<Toggle>("ExperimentalUvToggle");
                toggle.value = false;
                for (var i = 0; i < 5; i++) yield return null;
                Assert.AreEqual(DisplayStyle.None, root.Q<VisualElement>("UvPreservationControls").style.display.value);
                toggle.value = true;
                for (var i = 0; i < 5; i++) yield return null;
                Assert.IsTrue(component.options.FaQem.ExperimentalJointUv);
                Assert.AreEqual(7500, component.options.FaQem.ExperimentalUvWeight);
            }
            finally
            {
                window.Close();
                Undo.ClearUndo(component);
                Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(obj);
                LocalizationProvider.CurrentLocale = locale;
            }
        }

        [UnityTest]
        public IEnumerator TextureStrengthPresetFitsNarrowCogwheelLayouts()
        {
            var locale = LocalizationProvider.CurrentLocale;
            var obj = new GameObject("UV layout test", typeof(MeshRenderer));
            var component = obj.AddComponent<MeshiaMeshSimplifier>();
            var inspector = UnityEditor.Editor.CreateEditor(component);
            var window = ScriptableObject.CreateInstance<TestWindow>();
            try
            {
                var before = EditorJsonUtility.ToJson(component);
                var root = inspector.CreateInspectorGUI();
                window.rootVisualElement.Add(root);
                window.Show();
                window.position = new Rect(50, 50, 600, 800);
                for (var i = 0; i < 10; i++) yield return null;
                root.Q<Foldout>("FaQemOptionsGroup").value = true;
                var slider = root.Q<SliderInt>("UvStrengthSlider");
                var preset = root.Q<DropdownField>("UvStrengthPreset");
                var number = root.Q<FloatField>("ExperimentalUvWeightField");
                foreach (var language in new[] { "en", "ja" })
                foreach (var width in new[] { 300f, 360f, 500f })
                {
                    LocalizationProvider.CurrentLocale = language;
                    root.style.width = width;
                    for (var i = 0; i < 5; i++) yield return null;
                    var row = slider.parent.worldBound;
                    Assert.Greater(preset.worldBound.width, 60, "Preset must have readable space.");
                    Assert.LessOrEqual(number.worldBound.xMax, row.xMax + .5f, $"{language} at {width}px");
                    Assert.LessOrEqual(preset.worldBound.xMax, number.worldBound.xMin);
                    Assert.Greater(number.worldBound.width, 50);
                    Assert.AreEqual(5000, number.value);
                    Assert.LessOrEqual(slider.worldBound.xMax, preset.worldBound.xMin);
                    Assert.Greater(slider.Q<VisualElement>(className: "unity-base-field__input").worldBound.width, 20);
                    Assert.AreEqual("Medium", preset.value);
                    Assert.AreEqual(before, EditorJsonUtility.ToJson(component));
                }
            }
            finally
            {
                window.Close();
                Undo.ClearUndo(component);
                Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(obj);
                LocalizationProvider.CurrentLocale = locale;
            }
        }

        [UnityTest]
        public IEnumerator CalculationIndicatorCoversQueuedAndRunningWorkWithoutMovingRows()
        {
            var locale = LocalizationProvider.CurrentLocale;
            var settings = new GameObject("Calculation indicator test");
            var component = settings.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            var inspector = UnityEditor.Editor.CreateEditor(component);
            var window = ScriptableObject.CreateInstance<TestWindow>();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var type = inspector.GetType();
            try
            {
                var root = inspector.CreateInspectorGUI();
                window.position = new Rect(100, 100, 400, 600);
                window.rootVisualElement.Add(root);
                window.Show();
                var indicator = root.Q<VisualElement>("BackgroundCalculationIndicator");
                var label = root.Q<Label>("BackgroundCalculationLabel");
                var foldout = root.Q<Foldout>("EstimatesAndBuildDetails");
                var summary = root.Q<Label>("AllocationSummary");
                var refresh = type.GetMethod("RefreshBackgroundCalculationIndicator", flags);
                Assert.IsTrue(foldout.Q<Toggle>().Contains(indicator));
                Assert.AreEqual(PickingMode.Ignore, indicator.pickingMode);
                foreach (var language in new[] { "en", "ja" })
                {
                    LocalizationProvider.CurrentLocale = language;
                    for (var i = 0; i < 5; i++) yield return null;
                    var rowY = summary.worldBound.y;
                    foreach (var state in new[] { "pendingOutputIndex", "estimateScheduled", "estimateRunning" })
                    {
                        var field = type.GetField(state, flags);
                        field.SetValue(inspector, state == "pendingOutputIndex" ? (object)0 : true);
                        refresh.Invoke(inspector, new object[] { root });
                        for (var i = 0; i < 5; i++) yield return null;
                        Assert.AreEqual(Visibility.Visible, indicator.resolvedStyle.visibility, state);
                        Assert.AreEqual(language == "ja" ? "計算中..." : "Calculating...", label.text);
                        Assert.IsNotNull(root.Q<Image>("BackgroundCalculationSpinner").image);
                        Assert.AreEqual(rowY, summary.worldBound.y, .5f, "The badge must not push any rows down.");
                        Assert.LessOrEqual(indicator.worldBound.xMax, root.worldBound.xMax + 1);
                        field.SetValue(inspector, state == "pendingOutputIndex" ? (object)(-1) : false);
                        refresh.Invoke(inspector, new object[] { root });
                        Assert.AreEqual(Visibility.Hidden, indicator.style.visibility.value, "Completion must clear the badge.");
                    }
                    type.GetField("estimateRunning", flags).SetValue(inspector, true);
                    foldout.value = false;
                    refresh.Invoke(inspector, new object[] { root });
                    for (var i = 0; i < 5; i++) yield return null;
                    Assert.AreEqual(Visibility.Visible, indicator.resolvedStyle.visibility);
                    Assert.Greater(indicator.worldBound.height, 0);
                    type.GetField("estimateRunning", flags).SetValue(inspector, false);
                    foldout.value = true;
                    refresh.Invoke(inspector, new object[] { root });
                }
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(settings);
                LocalizationProvider.CurrentLocale = locale;
            }
        }

        [TestCase(0)]
        [TestCase(4)]
        [TestCase(-4)]
        public void ManualRedistributionPreservesBuildCorrectionAndUndo(int reserve)
        {
            var avatar = new GameObject("Manual redistribution", typeof(nadena.dev.ndmf.runtime.components.NDMFAvatarRoot));
            for (var i = 0; i < 2; i++) GameObject.CreatePrimitive(PrimitiveType.Cube).transform.SetParent(avatar.transform);
            var settings = new GameObject("Settings"); settings.transform.SetParent(avatar.transform);
            var component = settings.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            component.RefreshEntries(); component.TargetTriangleCount = 12 + reserve;
            component.BuildTriangleReserve = reserve; component.AutoAdjustEnabled = true;
            foreach (var entry in component.Entries) entry.TargetTriangleCount = 6;
            var inspector = UnityEditor.Editor.CreateEditor(component);
            var type = inspector.GetType();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            try
            {
                Undo.FlushUndoRecordObjects();
                var before = EditorJsonUtility.ToJson(component);
                type.GetMethod("SetManualAllocation", flags).Invoke(inspector, new object[] { 0, 3 });
                CollectionAssert.AreEqual(new[] { 3, 9 }, component.Entries.Select(e => e.TargetTriangleCount));
                Assert.AreEqual(reserve, component.BuildTriangleReserve);
                Assert.IsTrue(component.AutoAdjustEnabled);
                var reduced = EditorJsonUtility.ToJson(component);
                Undo.PerformUndo(); Assert.AreEqual(before, EditorJsonUtility.ToJson(component));
                Undo.PerformRedo(); Assert.AreEqual(reduced, EditorJsonUtility.ToJson(component));
                Object.DestroyImmediate(inspector); inspector = UnityEditor.Editor.CreateEditor(component);
                type.GetMethod("AdjustQuality", flags).Invoke(inspector, new object[] { -1 });
                Assert.AreEqual(reduced, EditorJsonUtility.ToJson(component), "Reopening or adjusting must preserve the calibrated allocation total.");
                type.GetMethod("SetManualAllocation", flags).Invoke(inspector, new object[] { 0, 5 });
                CollectionAssert.AreEqual(new[] { 5, 7 }, component.Entries.Select(e => e.TargetTriangleCount));
                Assert.AreEqual(reserve, component.BuildTriangleReserve);
                Assert.AreEqual(12 + reserve, component.TargetTriangleCount);
            }
            finally
            {
                Undo.ClearUndo(component); Object.DestroyImmediate(inspector); Object.DestroyImmediate(avatar);
            }
        }

        [TestCase("stall")]
        [TestCase("limit")]
        [TestCase("cancel")]
        [TestCase("capacity")]
        [TestCase("failure")]
        [TestCase("exception")]
        [TestCase("success")]
        public void FittingCommitsOnlySuccessAndRestoresEveryTrialOtherwise(string scenario)
        {
            var targets = new[] { 100, 200 };
            var allowance = 10;
            var builds = 0;
            var corrections = 0;
            var rollbacks = 0;
            var displayedCount = 0;
            var counts = scenario == "stall" ? new[] { 76000, 74000, 73000, 73000 }
                : scenario == "success" ? new[] { 76000, 74000, 70000 }
                : new[] { 76000, 74000, 73000, 72000 };
            void Run()
            {
                Editor.MeshiaCascadingAvatarMeshSimplifierEditor.RunBoundedBuildFit(true, 70000,
                    next => scenario == "cancel" && next == 2,
                    () =>
                    {
                        if (scenario == "exception" && builds == 2) throw new System.InvalidOperationException("Build error");
                        if (scenario == "failure" && builds == 2) return null;
                        displayedCount = counts[builds++];
                        return (int?)displayedCount;
                    },
                    () =>
                    {
                        if (scenario == "capacity" && corrections == 2) return false;
                        targets[0] -= 20; targets[1] -= 40; allowance += 60; corrections++;
                        return true;
                    },
                    () => { targets[0] = 100; targets[1] = 200; allowance = 10; displayedCount = 76000; rollbacks++; });
            }
            if (scenario == "exception") Assert.Throws<System.InvalidOperationException>(() => Run());
            else Run();
            if (scenario == "success")
            {
                CollectionAssert.AreEqual(new[] { 60, 120 }, targets);
                Assert.AreEqual(130, allowance);
                Assert.AreEqual(70000, displayedCount);
                Assert.AreEqual(0, rollbacks);
            }
            else
            {
                CollectionAssert.AreEqual(new[] { 100, 200 }, targets, "Restore the starting allocations, not the previous trial.");
                Assert.AreEqual(10, allowance);
                Assert.AreEqual(76000, displayedCount, "Restored settings must display their baseline measurement.");
                Assert.AreEqual(1, rollbacks);
            }
        }

        [TestCase(71000)]
        [TestCase(72000)]
        public void NonImprovingProbeRestoresThePreviousMeasuredAllocation(int probeCount)
        {
            var allocation = 100;
            var previousAllocation = allocation;
            var displayedCount = 0;
            var builds = 0;
            var restored = false;
            var stop = Editor.MeshiaCascadingAvatarMeshSimplifierEditor.RunBoundedBuildFit(true, 70000,
                _ => false,
                () => { displayedCount = builds++ == 0 ? 71000 : probeCount; return displayedCount; },
                () => { previousAllocation = allocation; allocation = 50; return true; },
                () => { allocation = previousAllocation; displayedCount = 71000; restored = true; });
            Assert.AreEqual(Editor.MeshiaCascadingAvatarMeshSimplifierEditor.BuildFitStop.NoProgress, stop);
            Assert.IsTrue(restored);
            Assert.AreEqual(100, allocation);
            Assert.AreEqual(71000, displayedCount);
            Assert.AreEqual(2, builds);
        }

        [UnityTest]
        public IEnumerator ChangingBudgetClearsCalibrationButInspectorBindingDoesNot()
        {
            var avatar = new GameObject("Budget change test", typeof(nadena.dev.ndmf.runtime.components.NDMFAvatarRoot));
            for (var i = 0; i < 4; i++) GameObject.CreatePrimitive(PrimitiveType.Cube).transform.SetParent(avatar.transform);
            var settings = new GameObject("Settings"); settings.transform.SetParent(avatar.transform);
            var component = settings.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            component.RefreshEntries(); component.TargetTriangleCount = 30; component.BuildTriangleReserve = 4;
            foreach (var entry in component.Entries) entry.TargetTriangleCount = 4;
            component.Entries[0].Enabled = false;
            component.Entries[1].Fixed = true; component.Entries[1].TargetTriangleCount = 6;
            var inspector = UnityEditor.Editor.CreateEditor(component);
            var window = ScriptableObject.CreateInstance<TestWindow>();
            try
            {
                var root = inspector.CreateInspectorGUI(); window.rootVisualElement.Add(root); window.Show();
                UnityEditor.UIElements.BindingExtensions.Bind(root, inspector.serializedObject);
                yield return null;
                yield return null;
                Assert.AreEqual(4, component.BuildTriangleReserve, "Opening the inspector must preserve calibration.");
                var field = root.Q<IntegerField>("TargetTriangleCountField");
                Assert.IsTrue(field.isDelayed, "Typing partial budget digits must not redistribute allocations.");
                field.value = 24;
                yield return null;
                Assert.AreEqual(24, component.TargetTriangleCount);
                Assert.AreEqual(0, component.BuildTriangleReserve);
                Assert.AreEqual(3, component.Entries[2].TargetTriangleCount);
                Assert.AreEqual(3, component.Entries[3].TargetTriangleCount);
                Assert.AreEqual(6, component.Entries[1].TargetTriangleCount);
            }
            finally
            {
                window.Close();
                Undo.ClearUndo(component);
                Object.DestroyImmediate(inspector); Object.DestroyImmediate(avatar);
            }
        }

        [TestCase(true, new[] { 66016, 68657, 69985 }, -1, true, "WithinBudget", 3, 2)]
        [TestCase(true, new[] { 70780, 70293, 70000 }, -1, true, "WithinBudget", 3, 2)]
        [TestCase(false, new[] { 66016 }, -1, true, "Measured", 1, 0)]
        [TestCase(true, new[] { 69930 }, -1, true, "WithinBudget", 1, 0)]
        [TestCase(true, new[] { 74000, 73000, 72000, 71000, 70000 }, -1, true, "Limit", 4, 3)]
        [TestCase(true, new[] { 66000, 67000, 68000, 69000, 70000 }, -1, true, "Limit", 4, 3)]
        [TestCase(true, new[] { 71000, 71000 }, -1, true, "NoProgress", 2, 1)]
        [TestCase(true, new[] { 71000, 72000 }, -1, true, "NoProgress", 2, 1)]
        [TestCase(true, new[] { 66000, 65000 }, -1, true, "NoProgress", 2, 1)]
        [TestCase(true, new[] { 71000 }, -1, false, "NoCapacity", 1, 0)]
        [TestCase(true, new[] { -1 }, -1, true, "Failed", 1, 0)]
        [TestCase(true, new[] { 71000, -1 }, -1, true, "Failed", 2, 1)]
        [TestCase(true, new[] { 71000 }, 0, true, "Cancelled", 0, 0)]
        [TestCase(true, new[] { 71000 }, 1, true, "Cancelled", 1, 0)]
        [TestCase(true, new[] { 71000, 70500 }, 2, true, "Cancelled", 2, 1)]
        public void AutomaticFitIsBoundedAndNeverChangesAllocationsAfterItsLastMeasurement(
            bool auto, int[] counts, int cancelAt, bool capacity, string expectedStop, int expectedBuilds, int expectedCorrections)
        {
            var builds = 0;
            var corrections = 0;
            var settingsVersion = 0;
            var measuredVersion = 0;
            var stop = Editor.MeshiaCascadingAvatarMeshSimplifierEditor.RunBoundedBuildFit(auto, 70000,
                next => next == cancelAt,
                () =>
                {
                    measuredVersion = settingsVersion;
                    var count = counts[builds++];
                    return count < 0 ? (int?)null : count;
                },
                () =>
                {
                    if (!capacity) return false;
                    corrections++;
                    settingsVersion++;
                    return true;
                });
            Assert.AreEqual(expectedStop, stop.ToString());
            Assert.AreEqual(expectedBuilds, builds);
            Assert.AreEqual(expectedCorrections, corrections);
            Assert.AreEqual(measuredVersion, settingsVersion, "Do not leave a correction unmeasured after stopping.");
        }

        [Test]
        public void AnalysisCompletionIgnoresPendingBuildUndoButStillInvalidatesRealEdits()
        {
            var type = typeof(Editor.MeshiaCascadingAvatarMeshSimplifierEditor);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var busy = type.GetField("s_analysisInProgress", flags);
            var revision = type.GetProperty("CurrentAnalysisRevision", flags);
            var complete = type.GetMethod("CompleteTriangleAnalysis", flags);
            var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
            var settings = new GameObject("Analysis invalidation test");
            var component = settings.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            try
            {
                Undo.FlushUndoRecordObjects();
                var before = (int)revision.GetValue(null);
                busy.SetValue(null, true);
                // Like shader/build integrations, record a temporary material change
                // without flushing it before the analysis completion callback runs.
                Undo.RecordObject(material, "Build generated material");
                material.renderQueue = 2451;
                complete.Invoke(null, null);
                Assert.IsFalse((bool)busy.GetValue(null));
                Assert.AreEqual(before, revision.GetValue(null));
                Undo.FlushUndoRecordObjects();
                Assert.AreEqual(before, revision.GetValue(null), "Build Undo records must not escape the completion guard.");

                Undo.RecordObject(component, "Change requested budget");
                component.TargetTriangleCount -= 1;
                Undo.FlushUndoRecordObjects();
                Assert.Greater((int)revision.GetValue(null), before, "Real edits after analysis must still invalidate the result.");
            }
            finally
            {
                busy.SetValue(null, false);
                Undo.ClearUndo(material);
                Undo.ClearUndo(component);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase(false, false, -6)]
        [TestCase(false, false, 6)]
        [TestCase(false, true, -6)]
        [TestCase(false, true, 6)]
        [TestCase(true, false, -6)]
        [TestCase(true, false, 6)]
        [TestCase(true, true, -6)]
        [TestCase(true, true, 6)]
        public void AllocationUsesRequestedBudgetRegardlessOfAaoOrCalibration(bool reset, bool stale, int delta)
        {
            var avatar = new GameObject("Allocation test", typeof(nadena.dev.ndmf.runtime.components.NDMFAvatarRoot));
            foreach (var name in new[] { "Body", "Clothing" })
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = name; cube.transform.SetParent(avatar.transform);
                var mesh = cube.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(cube.GetComponent<MeshRenderer>());
                cube.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
            }
            var settings = new GameObject("Settings"); settings.transform.SetParent(avatar.transform);
            var component = settings.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            component.RefreshEntries(); component.AutoAdjustEnabled = false; component.TargetTriangleCount = 18;
            UnityEditor.Editor inspector = null;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var counterField = typeof(Editor.DownstreamTriangleEstimator).GetField("s_aaoCounter", flags);
            var oldCounter = counterField.GetValue(null);
            System.Collections.IDictionary cache = null;
            string key = null;
            try
            {
                // AAO predicts half the triangles survive. This is useful information,
                // but must not cause 36 raw triangles to be allocated to an 18 budget.
                Editor.DownstreamTriangleEstimator.RegisterAaoCounter(_ => 6);
                inspector = UnityEditor.Editor.CreateEditor(component);
                var type = inspector.GetType();
                cache = (System.Collections.IDictionary)type.GetField("BuildAnalysisCache", flags).GetValue(null);
                key = (string)type.GetMethod("GetBuildAnalysisResultKey", flags).Invoke(null, new object[] { component });
                var revision = (int)type.GetProperty("CurrentAnalysisRevision", flags).GetValue(null);
                var result = System.Activator.CreateInstance(type.GetNestedType("BuildAnalysisResult", System.Reflection.BindingFlags.NonPublic),
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                    new object[] { 18 + delta, 18, stale ? revision - 1 : revision, null }, null);
                type.GetMethod("StoreBuildAnalysisResult", flags).Invoke(null, new object[] { component, result });
                if (reset)
                {
                    foreach (var entry in component.Entries) { entry.Enabled = true; entry.Fixed = true; }
                    var button = inspector.CreateInspectorGUI().Q<Button>("ResetButton");
                    typeof(Clickable).GetMethod("SimulateSingleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .Invoke(button.clickable, new object[] { null, 0 });
                }
                else
                {
                    type.GetMethod("AdjustQuality", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .Invoke(inspector, new object[] { -1 });
                }
                Assert.IsFalse(component.Entries[0].Enabled);
                Assert.AreEqual(6, component.Entries[1].TargetTriangleCount, "Reserve the face's 12 raw triangles and allocate the remaining 6, regardless of estimates.");
                Assert.AreEqual(18, (int)type.GetMethod("GetTotalSimplifiedTriangleCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(inspector, new object[] { false }));
            }
            finally
            {
                counterField.SetValue(null, oldCounter); Editor.DownstreamTriangleEstimator.Invalidate();
                if (key != null) { cache.Remove(key); SessionState.EraseString(key); }
                Undo.ClearUndo(component);
                if (inspector != null) Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(avatar);
            }
        }

        [UnityTest]
        public IEnumerator AutoAdjustRedistributesBothDirectionsWithoutChangingEditedOrLockedMeshes()
        {
            var avatar = new GameObject("Budget test", typeof(nadena.dev.ndmf.runtime.components.NDMFAvatarRoot));
            for (var i = 0; i < 3; i++)
            {
                var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mesh.name = "Mesh " + i; mesh.transform.SetParent(avatar.transform);
            }
            var settings = new GameObject("Settings"); settings.transform.SetParent(avatar.transform);
            var component = settings.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            component.RefreshEntries(); component.TargetTriangleCount = 18; component.AutoAdjustEnabled = true;
            foreach (var entry in component.Entries) entry.TargetTriangleCount = 6;
            var window = ScriptableObject.CreateInstance<TestWindow>();
            UnityEditor.Editor inspector = null;
            try
            {
                inspector = UnityEditor.Editor.CreateEditor(component);
                var root = inspector.CreateInspectorGUI(); window.rootVisualElement.Add(root); window.Show();
                yield return new WaitForSecondsRealtime(.6f);
                var rows = root.Query<TemplateContainer>().ToList().Where(x => x.userData is int).ToArray();
                Assert.AreEqual(3, rows.Length);
                MeasuredMeshBudgetTests.Seed(inspector, 18, x => x);
                CollectionAssert.AreEqual(new[] { 6, 6, 6 }, component.Entries.Select(e => e.TargetTriangleCount));
                Undo.IncrementCurrentGroup();
                rows[0].Q<SliderInt>("TargetTriangleCountSlider").value = 2;
                yield return new WaitForSecondsRealtime(.6f);
                CollectionAssert.AreEqual(new[] { 2, 8, 8 }, component.Entries.Select(e => e.TargetTriangleCount), "Lowering one target returns its allocation to unlocked peers.");
                Assert.AreEqual(0, component.BuildTriangleReserve);
                Assert.AreEqual(2, rows[0].Q<IntegerField>("TargetTriangleCountField").value);
                StringAssert.Contains("Estimated output", root.Q<Label>("AllocationSummary").text);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                yield return new WaitForSecondsRealtime(.6f);
                CollectionAssert.AreEqual(new[] { 6, 6, 6 }, component.Entries.Select(e => e.TargetTriangleCount));
                Assert.AreEqual(0, component.BuildTriangleReserve, "Redistribution must not create a build allowance.");
                MeasuredMeshBudgetTests.Seed(inspector, 18, x => x);
                rows[0].Q<IntegerField>("TargetTriangleCountField").value = 10;
                yield return new WaitForSecondsRealtime(.6f);
                CollectionAssert.AreEqual(new[] { 10, 4, 4 }, component.Entries.Select(e => e.TargetTriangleCount), "Raising a target takes budget from unlocked peers.");
                // Rebinding/reopening an inspector is not a request to refill the budget.
                root.Q<ListView>("EntriesListView").Rebuild();
                yield return new WaitForSecondsRealtime(.6f);
                CollectionAssert.AreEqual(new[] { 10, 4, 4 }, component.Entries.Select(e => e.TargetTriangleCount));
                rows = root.Query<TemplateContainer>().ToList().Where(x => x.userData is int).ToArray();
                component.Entries[2].TargetTriangleCount = 6; component.Entries[2].Fixed = true; EditorUtility.SetDirty(component);
                MeasuredMeshBudgetTests.Seed(inspector, 20, x => x);
                rows[0].Q<IntegerField>("TargetTriangleCountField").value = 12;
                yield return new WaitForSecondsRealtime(.6f);
                CollectionAssert.AreEqual(new[] { 12, 0, 6 }, component.Entries.Select(e => e.TargetTriangleCount), "Raising a target may reduce unlocked peers, never the edited or locked row.");
                rows[0].Q<SliderInt>("TargetTriangleCountSlider").value = 6;
                yield return new WaitForSecondsRealtime(.6f);
                CollectionAssert.AreEqual(new[] { 6, 6, 6 }, component.Entries.Select(e => e.TargetTriangleCount), "Lowering a target restores zeroed unlocked peers while preserving locked allocations.");
                Assert.AreEqual(0, component.BuildTriangleReserve);
                component.Entries[2].Fixed = false; component.AutoAdjustEnabled = false; EditorUtility.SetDirty(component);
                MeasuredMeshBudgetTests.Seed(inspector, 18, x => x);
                rows[1].Q<IntegerField>("TargetTriangleCountField").value = 12;
                yield return new WaitForSecondsRealtime(.6f);
                CollectionAssert.AreEqual(new[] { 6, 12, 6 }, component.Entries.Select(e => e.TargetTriangleCount), "Auto Adjust off must leave peers alone.");
            }
            finally
            {
                Undo.ClearUndo(component); window.Close();
                if (inspector != null) Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(avatar);
            }
        }

        [TestCase(0)]
        [TestCase(6)]
        public void AdjustSpendsSpareBudgetEvenFromZero(int initial)
        {
            var avatar = new GameObject("Budget test", typeof(nadena.dev.ndmf.runtime.components.NDMFAvatarRoot));
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube); mesh.transform.SetParent(avatar.transform);
            var settings = new GameObject("Settings"); settings.transform.SetParent(avatar.transform);
            var component = settings.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            component.RefreshEntries(); component.TargetTriangleCount = 12; component.Entries[0].TargetTriangleCount = initial;
            UnityEditor.Editor inspector = null;
            try
            {
                inspector = UnityEditor.Editor.CreateEditor(component);
                inspector.GetType().GetMethod("AdjustQuality", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(inspector, new object[] { -1 });
                Assert.AreEqual(12, component.Entries[0].TargetTriangleCount);
            }
            finally
            {
                Undo.ClearUndo(component);
                if (inspector != null) Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(avatar);
            }
        }

        [Test]
        public void ResetReservesExcludedFaceGeometryBeforeAllocatingTheRemainingBudget()
        {
            var avatar = new GameObject("Reset test", typeof(nadena.dev.ndmf.runtime.components.NDMFAvatarRoot));
            var face = GameObject.CreatePrimitive(PrimitiveType.Cube);
            face.name = "Body"; face.transform.SetParent(avatar.transform);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body_base"; body.transform.SetParent(avatar.transform);
            var settings = new GameObject("Settings"); settings.transform.SetParent(avatar.transform);
            var component = settings.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            component.AutoAdjustEnabled = false; component.TargetTriangleCount = 18;
            UnityEditor.Editor inspector = null;
            try
            {
                inspector = UnityEditor.Editor.CreateEditor(component);
                foreach (var entry in component.Entries) { entry.Enabled = true; entry.Fixed = true; }
                var root = inspector.CreateInspectorGUI();
                var reset = root.Q<Button>("ResetButton");
                typeof(Clickable).GetMethod("SimulateSingleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(reset.clickable, new object[] { null, 0 });
                Assert.AreEqual(2, component.Entries.Count);
                var faceEntry = component.Entries[0];
                var bodyEntry = component.Entries[1];
                Assert.IsFalse(faceEntry.Enabled);
                Assert.IsTrue(bodyEntry.Enabled);
                Assert.IsFalse(bodyEntry.Fixed);
                Assert.AreEqual(6, bodyEntry.TargetTriangleCount, "Reserve the face's 12 triangles from the total budget of 18.");
            }
            finally
            {
                Undo.ClearUndo(component);
                if (inspector != null) Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(avatar);
            }
        }

        [UnityTest]
        public IEnumerator DeformationShortcutSupportsAutoExplicitPoliciesUndoAndLocalization()
        {
            var locale = LocalizationProvider.CurrentLocale;
            var avatar = new GameObject("Protection test", typeof(nadena.dev.ndmf.runtime.components.NDMFAvatarRoot));
            var meshObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            meshObject.transform.SetParent(avatar.transform);
            var mesh = Object.Instantiate(meshObject.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(meshObject.GetComponent<MeshRenderer>());
            var skin = meshObject.AddComponent<SkinnedMeshRenderer>();
            var bone = new GameObject("Bone"); bone.transform.SetParent(avatar.transform);
            skin.bones = new[] { avatar.transform, bone.transform };
            mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
            mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = .5f, boneIndex1 = 1, weight1 = .5f }, mesh.vertexCount).ToArray();
            skin.sharedMesh = mesh;
            var settings = new GameObject("Settings"); settings.transform.SetParent(avatar.transform);
            var component = settings.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            component.AutoAdjustEnabled = false; component.RefreshEntries();
            component.Entries[0].Options = MeshSimplifierOptions.ConservativeAvatar;
            var window = ScriptableObject.CreateInstance<TestWindow>();
            UnityEditor.Editor inspector = null;
            try
            {
                LocalizationProvider.CurrentLocale = "en";
                inspector = UnityEditor.Editor.CreateEditor(component);
                var root = inspector.CreateInspectorGUI(); window.rootVisualElement.Add(root); window.Show();
                for (var i = 0; i < 10; i++) yield return null;
                var toggle = root.Q<Toggle>("DeformationProtectionToggle");
                Assert.IsNotNull(toggle);
                Assert.IsTrue(toggle.value, "Auto is an enabled protection setting.");
                Assert.IsFalse(toggle.ClassListContains("partial-protection"));
                var savedGeometry = component.Entries[0].Options.FaQem;
                var before = EditorJsonUtility.ToJson(component);
                LocalizationProvider.CurrentLocale = "ja";
                StringAssert.Contains("形状と変形", toggle.tooltip);
                Assert.AreEqual(before, EditorJsonUtility.ToJson(component));
                Undo.IncrementCurrentGroup();
                toggle.value = false; // Green -> yellow.
                Assert.IsTrue(toggle.ClassListContains("partial-protection"));
                Assert.AreEqual(SkinningProtectionPolicy.Off, component.Entries[0].Options.SkinningProtection.Policy);
                Assert.IsFalse(component.Entries[0].DisableProtections);
                Assert.AreEqual(savedGeometry, component.Entries[0].Options.FaQem);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                for (var i = 0; i < 10; i++) yield return null;
                Assert.AreEqual(before, EditorJsonUtility.ToJson(component));
                toggle = root.Q<Toggle>("DeformationProtectionToggle");
                toggle.value = false; // Green -> yellow again.
                toggle.value = false; // Yellow remains checked; next click -> gray.
                Assert.IsTrue(component.Entries[0].DisableProtections);
                Assert.IsTrue(toggle.ClassListContains("no-protection"));
                Assert.IsFalse(toggle.value);
                Assert.IsFalse(toggle.ClassListContains("partial-protection"));
                Assert.AreEqual(savedGeometry, component.Entries[0].Options.FaQem);
                Assert.IsFalse(root.Q<UnityEditor.UIElements.PropertyField>("OptionsField").enabledSelf);
                StringAssert.Contains("保護なし", toggle.tooltip);
                var unprotected = MeshiaCascadingAvatarMeshSimplifier.GetJointProtectionOptions(avatar, component, component.Entries[0]);
                Assert.IsTrue(unprotected.AllowUnsafeGeometry);
                Assert.IsFalse(unprotected.PreserveBorderEdges);
                Assert.IsFalse(unprotected.FaQem.PreserveAttributeSeams);
                Assert.AreEqual(0, unprotected.FaQem.MaxSurfaceDeviation);
                Assert.IsFalse(unprotected.SkinningProtection.Enabled);
                Assert.IsNull(MeshiaCascadingAvatarMeshSimplifier.GetPreserveBorderEdgesBoneIndices(avatar, component, component.Entries[0]));
                LocalizationProvider.CurrentLocale = "en";
                StringAssert.Contains("No protection", toggle.tooltip);
                toggle.value = true; // Gray -> green, saved geometry settings return.
                Assert.IsFalse(component.Entries[0].DisableProtections);
                Assert.AreEqual(SkinningProtectionPolicy.On, component.Entries[0].Options.SkinningProtection.Policy);
                Assert.IsTrue(component.Entries[0].Options.SkinningProtection.PreserveJointTransitions);
                Assert.AreEqual(savedGeometry, component.Entries[0].Options.FaQem);
                Assert.IsTrue(root.Q<UnityEditor.UIElements.PropertyField>("OptionsField").enabledSelf);
                Assert.IsFalse(toggle.ClassListContains("partial-protection"));
                Assert.IsFalse(toggle.ClassListContains("no-protection"));
                // A partial custom selection is yellow, not falsely reported as unprotected.
                var options = component.Entries[0].Options;
                options.SkinningProtection.PreserveJointTransitions = false;
                component.Entries[0].Options = options; EditorUtility.SetDirty(component);
                for (var i = 0; i < 10; i++) yield return null;
                Assert.IsTrue(root.Q<Toggle>("DeformationProtectionToggle").ClassListContains("partial-protection"));
            }
            finally
            {
                Undo.ClearUndo(component); window.Close();
                if (inspector != null) Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(avatar); Object.DestroyImmediate(mesh);
                LocalizationProvider.CurrentLocale = locale;
            }
        }

        [TestCase("en", false, false, null, 0, true)]
        [TestCase("ja", false, false, null, 0, true)]
        [TestCase("en", true, false, null, 10, false)]
        [TestCase("ja", true, false, null, 90000, false)]
        [TestCase("en", true, true, null, 90000, true)]
        [TestCase("ja", true, true, null, 90000, true)]
        [TestCase("en", true, false, "Build reported errors; count may be incomplete.", 0, true)]
        [TestCase("ja", true, false, "Build reported errors; count may be incomplete.", 10, true)]
        public void PreviewShortfallsAreCompactAndSupersededOnlyByCurrentSuccessfulAnalysis(
            string language, bool analyzed, bool stale, string error, int triangles, bool showPreview)
        {
            var locale = LocalizationProvider.CurrentLocale;
            var avatar = new GameObject("Preview diagnostic test");
            avatar.AddComponent<nadena.dev.ndmf.runtime.components.NDMFAvatarRoot>();
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(avatar.transform);
            var child = new GameObject("Settings"); child.transform.SetParent(avatar.transform);
            var component = child.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            component.AutoAdjustEnabled = false;
            component.RefreshEntries();
            var renderer = cube.GetComponent<Renderer>();
            var cache = Editor.Preview.MeshiaCascadingAvatarMeshSimplifierPreview.TriangleCountCache;
            UnityEditor.Editor inspector = null;
            System.Collections.IDictionary analysisCache = null;
            string analysisKey = null;
            try
            {
                LocalizationProvider.CurrentLocale = language;
                inspector = UnityEditor.Editor.CreateEditor(component);
                var entry = new MeshiaCascadingAvatarMeshSimplifierRendererEntry(renderer);
                component.Entries.Clear();
                component.Entries.Add(entry);
                entry.TargetTriangleCount = 4;
                cache[renderer] = (12, 10);
                var type = inspector.GetType();
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
                analysisCache = (System.Collections.IDictionary)type.GetField("BuildAnalysisCache", flags).GetValue(null);
                analysisKey = (string)type.GetMethod("GetBuildAnalysisResultKey", flags).Invoke(null, new object[] { component });
                analysisCache.Remove(analysisKey);
                SessionState.EraseString(analysisKey);
                var root = inspector.CreateInspectorGUI();
                var refresh = type.GetMethod("RefreshPreviewShortfalls", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                refresh.Invoke(inspector, new object[] { root });
                Assert.AreEqual(DisplayStyle.Flex, root.Q("PreviewShortfalls").style.display.value);
                Assert.AreEqual(1, root.Q("PreviewShortfalls").Query<HelpBox>().ToList().Count);
                Assert.AreEqual(HelpBoxMessageType.Info, root.Q<HelpBox>("PreviewShortfallSummary").messageType);
                var foldout = root.Q<Foldout>("BudgetBreakdown");
                Assert.IsFalse(foldout.value, "Per-mesh details must start collapsed.");
                Assert.AreEqual(language == "ja" ? "メッシュごとの配分と削減方法" : "Mesh budget breakdown and next steps", foldout.text);
                Assert.IsNotNull(root.Q<ScrollView>("BudgetMeshScroll"));

                if (analyzed)
                {
                    var revision = (int)type.GetProperty("CurrentAnalysisRevision", flags).GetValue(null);
                    var resultType = type.GetNestedType("BuildAnalysisResult", System.Reflection.BindingFlags.NonPublic);
                    var result = System.Activator.CreateInstance(resultType,
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                        new object[] { triangles, 12, stale ? revision - 1 : revision, error }, null);
                    type.GetMethod("StoreBuildAnalysisResult", flags).Invoke(null, new object[] { component, result });
                }
                refresh.Invoke(inspector, new object[] { root });
                Assert.AreEqual(showPreview ? DisplayStyle.Flex : DisplayStyle.None, root.Q("PreviewShortfalls").style.display.value);
                type.GetMethod("RefreshBudgetGuidance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(inspector, new object[] { root });
                var summary = root.Q<Label>("BudgetSummary");
                var resultLabel = root.Q<Label>("BuildResultSummary");
                Assert.IsTrue(root.Q<Foldout>("EstimatesAndBuildDetails").value);
                Assert.IsFalse(root.Q<Foldout>("CalculationDetails").value);
                Assert.AreEqual(language == "ja" ? "三角形数の予算" : "Triangle budget", root.Q<Foldout>("EstimatesAndBuildDetails").text);
                Assert.AreEqual(language == "ja" ? "計算の詳細" : "Calculation details", root.Q<Foldout>("CalculationDetails").text);
                Assert.AreEqual(2, root.Q<Foldout>("EstimatesAndBuildDetails").Query<Button>().ToList().Count(b => b.name == "AnalyzeNdmfBuildButton" || b.name == "FindBudgetReductionsButton"));
                Assert.AreEqual(analyzed && error == null && !stale && triangles > component.TargetTriangleCount,
                    summary.ClassListContains("budget-warning"));
                Assert.AreEqual(analyzed && error != null, summary.ClassListContains("budget-error"));
                Assert.AreEqual(analyzed && stale, resultLabel.ClassListContains("budget-out-of-date"));
                Assert.IsNotEmpty(root.Q<Label>("AllocationSummary").text);
                if (analyzed && error == null)
                {
                    StringAssert.Contains(triangles.ToString("N0"), resultLabel.text);
                    if (stale) StringAssert.Contains(language == "ja" ? "更新が必要" : "Out of date", resultLabel.text);
                    else if (triangles > component.TargetTriangleCount)
                        StringAssert.Contains((triangles - component.TargetTriangleCount).ToString("N0"), summary.text);
                }
                else if (!analyzed)
                {
                    StringAssert.Contains("10", resultLabel.text);
                    StringAssert.Contains(language == "ja" ? "推定" : "estimate", resultLabel.text);
                }
                root.Q<Foldout>("CalculationDetails").value = true;
                type.GetMethod("RefreshBudgetGuidance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(inspector, new object[] { root });
                StringAssert.Contains("12", root.Q<Label>("TriangleCountLabel").text);
                if (error != null) StringAssert.Contains(LocalizationProvider.Tr(error), root.Q<Label>("TriangleCountLabel").text);
                // A missing preview must not be replaced by a target and called measured.
                if (!analyzed)
                {
                    cache.Remove(renderer);
                    type.GetMethod("RefreshBudgetGuidance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(inspector, new object[] { root });
                    Assert.AreEqual(language == "ja" ? "ビルド結果: 未解析" : "Build result: not analyzed", resultLabel.text);
                    cache[renderer] = (12, 10);
                }

                // Disabled entries and absent cached previews must not leave a notice behind.
                entry.Enabled = false;
                refresh.Invoke(inspector, new object[] { root });
                Assert.AreEqual(DisplayStyle.None, root.Q("PreviewShortfalls").style.display.value);
                entry.Enabled = true;
                cache.Remove(renderer);
                refresh.Invoke(inspector, new object[] { root });
                Assert.AreEqual(DisplayStyle.None, root.Q("PreviewShortfalls").style.display.value);
            }
            finally
            {
                cache.Remove(renderer);
                if (analysisKey != null) { analysisCache.Remove(analysisKey); SessionState.EraseString(analysisKey); }
                Undo.ClearUndo(component);
                if (inspector != null) Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(avatar);
                LocalizationProvider.CurrentLocale = locale;
            }
        }

        [UnityTest]
        public IEnumerator BudgetBreakdownShowsOutputSeparatesExclusionsAndOpensSettings()
        {
            var locale = LocalizationProvider.CurrentLocale;
            var avatar = new GameObject("Budget test", typeof(nadena.dev.ndmf.runtime.components.NDMFAvatarRoot));
            var cubes = new GameObject[4];
            for (var i = 0; i < cubes.Length; i++)
            {
                cubes[i] = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cubes[i].name = "Mesh " + i;
                cubes[i].transform.SetParent(avatar.transform);
            }
            var child = new GameObject("Settings"); child.transform.SetParent(avatar.transform);
            var component = child.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            component.AutoAdjustEnabled = false;
            var window = ScriptableObject.CreateInstance<TestWindow>();
            UnityEditor.Editor inspector = null;
            var cache = Editor.Preview.MeshiaCascadingAvatarMeshSimplifierPreview.TriangleCountCache;
            try
            {
                inspector = UnityEditor.Editor.CreateEditor(component);
                component.Entries.Clear();
                foreach (var cube in cubes) component.Entries.Add(new MeshiaCascadingAvatarMeshSimplifierRendererEntry(cube.GetComponent<Renderer>()));
                component.Entries[0].TargetTriangleCount = 8;
                component.Entries[1].TargetTriangleCount = 2;
                component.Entries[1].Fixed = true;
                component.Entries[2].Enabled = false;
                cache[cubes[0].GetComponent<Renderer>()] = (12, 10);
                cache[cubes[1].GetComponent<Renderer>()] = (12, 10);
                var root = inspector.CreateInspectorGUI(); window.rootVisualElement.Add(root); window.Show();
                for (var i = 0; i < 10; i++) yield return null;
                var before = EditorJsonUtility.ToJson(component);
                var button = root.Q<Button>("FindBudgetReductionsButton");
                typeof(Clickable).GetMethod("SimulateSingleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(button.clickable, new object[] { null, 0 });
                Assert.IsTrue(root.Q<Foldout>("BudgetBreakdown").value);
                foreach (var language in new[] { "en", "ja", "en" })
                {
                    LocalizationProvider.CurrentLocale = language;
                    var rows = root.Q("BudgetMeshRows").Query<VisualElement>("BudgetMeshRow").ToList();
                    CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, rows.Select(row => (int)row.userData).ToArray());
                    StringAssert.Contains(language == "ja" ? "自動調整では固定" : "Fixed for Auto Adjust", rows[1].Q<Label>().text);
                    StringAssert.Contains(language == "ja" ? "軽量化対象外" : "not being simplified", rows[2].Q<Label>().text);
                    StringAssert.Contains(language == "ja" ? "未計測" : "not measured yet", rows[3].Q<Label>().text);
                    Assert.AreEqual(language == "ja" ? "削減方法を確認" : "Find ways to reduce", button.text);
                }
                var open = root.Q("BudgetMeshRows").Query<VisualElement>("BudgetMeshRow").First().Q<Button>();
                typeof(Clickable).GetMethod("SimulateSingleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(open.clickable, new object[] { null, 0 });
                for (var i = 0; i < 10; i++) yield return null;
                var item = root.Q<ListView>("EntriesListView").Query<TemplateContainer>().ToList().First(row => row.userData is int index && index == 0);
                Assert.IsTrue(item.Q<Toggle>("OptionsToggle").value);
                Assert.AreEqual(before, EditorJsonUtility.ToJson(component), "Guidance and navigation must not modify mesh settings.");
            }
            finally
            {
                foreach (var cube in cubes) cache.Remove(cube.GetComponent<Renderer>());
                window.Close();
                Undo.ClearUndo(component);
                if (inspector != null) Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(avatar);
                LocalizationProvider.CurrentLocale = locale;
            }
        }

        private static void AssertAlgorithmOptions(VisualElement root, MeshSimplificationTargetKind kind)
        {
            var fa = kind == MeshSimplificationTargetKind.FaQemTriangleCount;
            var uv = kind == MeshSimplificationTargetKind.UvLoopDissolveTriangleCount;
            var blender = kind == MeshSimplificationTargetKind.BlenderDecimateRatio;
            var meshia = !fa && !uv && !blender;
            foreach (var expected in new[]
            {
                ("LegacyOptionsGroup", meshia),
                ("PreserveBorderEdgesToggle", meshia || fa),
                ("SkinningProtectionGroup", !meshia),
                ("FaQemOptionsGroup", fa),
                ("UvFallbackProtectionHelp", uv),
            })
            {
                var element = root.Q(expected.Item1);
                Assert.IsNotNull(element, expected.Item1);
                Assert.AreEqual(expected.Item2 ? DisplayStyle.Flex : DisplayStyle.None,
                    element.resolvedStyle.display, kind + " / " + expected.Item1);
                if (expected.Item2) Assert.IsTrue(element.enabledInHierarchy, expected.Item1);
            }
        }

        [UnityTest]
        public IEnumerator ShouldShowOnlyUsedStandaloneOptionsAfterSwitchingAlgorithmsAndUndo()
        {
            var locale = LocalizationProvider.CurrentLocale;
            var meshObject = new GameObject("Algorithm options test", typeof(MeshRenderer));
            var component = meshObject.AddComponent<MeshiaMeshSimplifier>();
            var options = component.options;
            var window = ScriptableObject.CreateInstance<TestWindow>();
            UnityEditor.Editor inspector = null;
            try
            {
                inspector = UnityEditor.Editor.CreateEditor(component);
                var root = inspector.CreateInspectorGUI();
                window.rootVisualElement.Add(root); window.Show();
                for (var i = 0; i < 10; i++) yield return null;
                AssertAlgorithmOptions(root, MeshSimplificationTargetKind.FaQemTriangleCount);
                foreach (var kind in new[] { MeshSimplificationTargetKind.BlenderDecimateRatio,
                    MeshSimplificationTargetKind.UvLoopDissolveTriangleCount, MeshSimplificationTargetKind.AbsoluteTriangleCount,
                    MeshSimplificationTargetKind.FaQemTriangleCount })
                {
                    Undo.IncrementCurrentGroup();
                    root.Q<DropdownField>("TargetKindField").value = ObjectNames.NicifyVariableName(kind.ToString());
                    foreach (var language in new[] { "ja", "en" })
                    {
                        LocalizationProvider.CurrentLocale = language;
                        for (var i = 0; i < 10; i++) yield return null;
                        AssertAlgorithmOptions(root, kind);
                        Assert.AreEqual(1, root.Query<DropdownField>("LanguagePicker").ToList().Count(x => x.resolvedStyle.display != DisplayStyle.None));
                        Assert.AreEqual(options, component.options, "Visibility changes must preserve saved options.");
                    }
                }
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                var deadline = EditorApplication.timeSinceStartup + 2;
                while (root.Q("LegacyOptionsGroup").resolvedStyle.display != DisplayStyle.Flex &&
                    EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.AreEqual(MeshSimplificationTargetKind.AbsoluteTriangleCount, component.target.Kind);
                AssertAlgorithmOptions(root, component.target.Kind);
            }
            finally
            {
                Undo.ClearUndo(component); window.Close();
                if (inspector != null) Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(meshObject); LocalizationProvider.CurrentLocale = locale;
            }
        }

        [UnityTest]
        public IEnumerator ShouldShowOnlyUsedCascadingOptionsAfterSwitchingAlgorithmsAndUndo()
        {
            var locale = LocalizationProvider.CurrentLocale;
            var avatar = new GameObject("Algorithm options test");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(avatar.transform);
            var child = new GameObject("Settings"); child.transform.SetParent(avatar.transform);
            var component = child.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            component.AutoAdjustEnabled = false; component.RefreshEntries();
            var options = component.Entries[0].Options;
            var window = ScriptableObject.CreateInstance<TestWindow>();
            UnityEditor.Editor inspector = null;
            try
            {
                inspector = UnityEditor.Editor.CreateEditor(component);
                var root = inspector.CreateInspectorGUI();
                window.rootVisualElement.Add(root); window.Show();
                for (var i = 0; i < 10; i++) yield return null;
                root.Q<Toggle>("OptionsToggle").value = true;
                for (var i = 0; i < 10; i++) yield return null;
                AssertAlgorithmOptions(root, MeshSimplificationTargetKind.FaQemTriangleCount);
                foreach (var item in new[]
                {
                    ("Blender Decimate", MeshSimplificationTargetKind.BlenderDecimateRatio),
                    ("Uv Loop Dissolve", MeshSimplificationTargetKind.UvLoopDissolveTriangleCount),
                    ("Meshia", MeshSimplificationTargetKind.AbsoluteTriangleCount),
                    ("Fa Qem", MeshSimplificationTargetKind.FaQemTriangleCount),
                })
                {
                    Undo.IncrementCurrentGroup();
                    root.Q<DropdownField>("AlgorithmField").value = item.Item1;
                    foreach (var language in new[] { "ja", "en" })
                    {
                        LocalizationProvider.CurrentLocale = language;
                        for (var i = 0; i < 10; i++) yield return null;
                        AssertAlgorithmOptions(root, item.Item2);
                        Assert.AreEqual(options, component.Entries[0].Options);
                        Assert.AreEqual(DisplayStyle.Flex,
                            root.Q<Toggle>("DeformationProtectionToggle").resolvedStyle.display);
                    }
                }
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                var deadline = EditorApplication.timeSinceStartup + 2;
                while (root.Q("LegacyOptionsGroup").resolvedStyle.display != DisplayStyle.Flex &&
                    EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.AreEqual(MeshiaCascadingSimplificationAlgorithm.Meshia, component.Entries[0].Algorithm);
                AssertAlgorithmOptions(root, MeshSimplificationTargetKind.AbsoluteTriangleCount);
            }
            finally
            {
                Undo.ClearUndo(component); window.Close();
                if (inspector != null) Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(avatar); LocalizationProvider.CurrentLocale = locale;
            }
        }

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
                    AssertOption<bool>(root, "SkinningProtection.PreserveJointTransitions", "Preserve Vertices Near Joints", "関節付近の頂点を保持", language);
                    AssertOption<bool>(root, "FaQem.UseInverseAreaWeighting", "Use Inverse Area Weighting", "面積の逆数による重み付け", language);
                    AssertOption<bool>(root, "FaQem.PreserveAttributeSeams", "Lock Coincident Split Vertices", "同じ位置にある分離頂点の固定", language);
                    Assert.AreEqual(language == "ja" ? "テクスチャの歪みを抑える" : "Preserve texture mapping", root.Q<Toggle>("ExperimentalUvToggle").label);
                    Assert.IsNull(root.Q<DropdownField>("UvMethodField"));
                    Assert.AreEqual(language == "ja" ? "中" : "Medium", root.Q<DropdownField>("UvStrengthPreset").formatSelectedValueCallback("Medium"));
                    Assert.AreEqual(5000, root.Q<FloatField>("ExperimentalUvWeightField").value);
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
                Assert.That(button.text, Is.EqualTo("保守的"));
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
                Assert.That(button.text, Is.EqualTo("Conservative"));
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
                Assert.AreEqual("ビルド解析", root.Q<Button>("AnalyzeNdmfBuildButton").text);
                Assert.AreEqual("三角形数の予算", root.Q<Foldout>("EstimatesAndBuildDetails").text);
                Assert.IsFalse(root.Q<Foldout>("CalculationDetails").value);
                root.Q<Foldout>("CalculationDetails").value = true;
                StringAssert.Contains("元の三角形数:", root.Q<Label>("TriangleCountLabel").text);
                root.Q<Foldout>("CalculationDetails").value = false;
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
