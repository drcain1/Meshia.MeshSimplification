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
        public IEnumerator AutoAdjustRedistributesBothWaysAndPreservesEditedAndLockedMeshes()
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
                for (var i = 0; i < 10; i++) yield return null;
                var rows = root.Query<TemplateContainer>().ToList().Where(x => x.userData is int).ToArray();
                Assert.AreEqual(3, rows.Length);
                CollectionAssert.AreEqual(new[] { 6, 6, 6 }, component.Entries.Select(e => e.TargetTriangleCount));
                Undo.IncrementCurrentGroup();
                rows[0].Q<SliderInt>("TargetTriangleCountSlider").value = 2;
                for (var i = 0; i < 10; i++) yield return null;
                CollectionAssert.AreEqual(new[] { 2, 8, 8 }, component.Entries.Select(e => e.TargetTriangleCount), "Lowering one target returns its budget to unlocked peers.");
                Assert.AreEqual(2, rows[0].Q<IntegerField>("TargetTriangleCountField").value);
                StringAssert.Contains("18", root.Q<Label>("AllocationSummary").text);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                for (var i = 0; i < 10; i++) yield return null;
                CollectionAssert.AreEqual(new[] { 6, 6, 6 }, component.Entries.Select(e => e.TargetTriangleCount));
                rows[0].Q<IntegerField>("TargetTriangleCountField").value = 10;
                for (var i = 0; i < 10; i++) yield return null;
                CollectionAssert.AreEqual(new[] { 10, 4, 4 }, component.Entries.Select(e => e.TargetTriangleCount), "Raising a target takes budget from unlocked peers.");
                // Rebinding/reopening an inspector is not a request to refill the budget.
                root.Q<ListView>("EntriesListView").Rebuild();
                for (var i = 0; i < 10; i++) yield return null;
                CollectionAssert.AreEqual(new[] { 10, 4, 4 }, component.Entries.Select(e => e.TargetTriangleCount));
                rows = root.Query<TemplateContainer>().ToList().Where(x => x.userData is int).ToArray();
                component.Entries[2].TargetTriangleCount = 6; component.Entries[2].Fixed = true; EditorUtility.SetDirty(component);
                rows[0].Q<IntegerField>("TargetTriangleCountField").value = 12;
                for (var i = 0; i < 10; i++) yield return null;
                CollectionAssert.AreEqual(new[] { 12, 0, 6 }, component.Entries.Select(e => e.TargetTriangleCount), "Raising a target may reduce unlocked peers, never the edited or locked row.");
                rows[0].Q<SliderInt>("TargetTriangleCountSlider").value = 6;
                for (var i = 0; i < 10; i++) yield return null;
                CollectionAssert.AreEqual(new[] { 6, 6, 6 }, component.Entries.Select(e => e.TargetTriangleCount), "Returning budget must revive a zero allocation while preserving the lock.");
                component.Entries[2].Fixed = false; component.AutoAdjustEnabled = false; EditorUtility.SetDirty(component);
                rows[1].Q<IntegerField>("TargetTriangleCountField").value = 12;
                for (var i = 0; i < 10; i++) yield return null;
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
                Assert.IsFalse(toggle.ClassListContains("partial-protection"), "Missing humanoid mappings must not change the settings indicator.");
                Assert.IsFalse(root.Q<Toggle>("OptionsToggle").value);
                var before = EditorJsonUtility.ToJson(component);
                LocalizationProvider.CurrentLocale = "ja";
                StringAssert.Contains("変形の保護", toggle.tooltip);
                Assert.AreEqual(before, EditorJsonUtility.ToJson(component));
                Undo.IncrementCurrentGroup();
                toggle.value = false;
                Assert.AreEqual(SkinningProtectionPolicy.Off, component.Entries[0].Options.SkinningProtection.Policy);
                Assert.IsFalse(component.Entries[0].Options.SkinningProtection.PreserveJointTransitions);
                Assert.AreEqual(.0005f, component.Entries[0].Options.FaQem.MaxSurfaceDeviation);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                for (var i = 0; i < 10; i++) yield return null;
                Assert.AreEqual(before, EditorJsonUtility.ToJson(component));
                toggle = root.Q<Toggle>("DeformationProtectionToggle");
                Assert.IsTrue(toggle.value);
                toggle.value = false; toggle.value = true;
                Assert.AreEqual(SkinningProtectionPolicy.On, component.Entries[0].Options.SkinningProtection.Policy);
                Assert.IsTrue(component.Entries[0].Options.SkinningProtection.PreserveJointTransitions);
                Assert.IsFalse(toggle.ClassListContains("partial-protection"), "Explicit On must be green even without humanoid bone mappings.");
                // Verify both partial combinations and one-click restoration, without relying on rig matching.
                foreach (var weightOn in new[] { true, false })
                {
                    var partialOptions = component.Entries[0].Options;
                    partialOptions.SkinningProtection.Policy = weightOn ? SkinningProtectionPolicy.On : SkinningProtectionPolicy.Off;
                    partialOptions.SkinningProtection.PreserveJointTransitions = !weightOn;
                    component.Entries[0].Options = partialOptions; EditorUtility.SetDirty(component);
                    var partialDeadline = EditorApplication.timeSinceStartup + 2;
                    while (!root.Q<Toggle>("DeformationProtectionToggle").ClassListContains("partial-protection") &&
                        EditorApplication.timeSinceStartup < partialDeadline) yield return null;
                    toggle = root.Q<Toggle>("DeformationProtectionToggle");
                    Assert.IsTrue(toggle.ClassListContains("partial-protection"));
                    toggle.value = false; // The next click on a checked partial toggle enables both.
                    Assert.IsTrue(toggle.value);
                    Assert.IsFalse(toggle.ClassListContains("partial-protection"));
                    Assert.AreEqual(SkinningProtectionPolicy.On, component.Entries[0].Options.SkinningProtection.Policy);
                    Assert.IsTrue(component.Entries[0].Options.SkinningProtection.PreserveJointTransitions);
                }
                // Automatic selection remains enabled even on a mesh weighted to only one bone.
                mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, mesh.vertexCount).ToArray();
                var autoOptions = component.Entries[0].Options;
                autoOptions.SkinningProtection.Policy = SkinningProtectionPolicy.AutoDeforming;
                component.Entries[0].Options = autoOptions; EditorUtility.SetDirty(component);
                LocalizationProvider.CurrentLocale = "en"; // Refresh the displayed state without changing settings.
                Assert.IsTrue(toggle.value);
                Assert.IsFalse(toggle.ClassListContains("partial-protection"));
                StringAssert.Contains("Automatic bone-weight selection is enabled.", toggle.tooltip);
                // Changes in the cog menu must be reflected without reopening the inspector.
                var options = component.Entries[0].Options;
                options.SkinningProtection.Policy = SkinningProtectionPolicy.Off;
                options.SkinningProtection.PreserveJointTransitions = false;
                component.Entries[0].Options = options; EditorUtility.SetDirty(component);
                var deadline = EditorApplication.timeSinceStartup + 2;
                while (root.Q<Toggle>("DeformationProtectionToggle").value && EditorApplication.timeSinceStartup < deadline)
                    yield return null;
                Assert.IsFalse(root.Q<Toggle>("DeformationProtectionToggle").value);
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
                StringAssert.Contains("4", root.Q<Label>("AllocationSummary").text);
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
        public IEnumerator BudgetBreakdownOrdersOverrunsSeparatesExclusionsAndOpensSettings()
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
                    CollectionAssert.AreEqual(new[] { 1, 0, 2, 3 }, rows.Select(row => (int)row.userData).ToArray());
                    StringAssert.Contains(language == "ja" ? "固定配分" : "Fixed allocation", rows[0].Q<Label>().text);
                    StringAssert.Contains(language == "ja" ? "軽量化対象外" : "not being simplified", rows[2].Q<Label>().text);
                    StringAssert.Contains(language == "ja" ? "未計測" : "no preview measurement", rows[3].Q<Label>().text);
                    Assert.AreEqual(language == "ja" ? "削減方法を確認" : "Find ways to reduce", button.text);
                }
                var open = root.Q("BudgetMeshRows").Query<VisualElement>("BudgetMeshRow").First().Q<Button>();
                typeof(Clickable).GetMethod("SimulateSingleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(open.clickable, new object[] { null, 0 });
                for (var i = 0; i < 10; i++) yield return null;
                var item = root.Q<ListView>("EntriesListView").Query<TemplateContainer>().ToList().First(row => row.userData is int index && index == 1);
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
