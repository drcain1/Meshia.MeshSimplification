#if ENABLE_MODULAR_AVATAR
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Meshia.MeshSimplification.Ndmf.Editor;
using Meshia.MeshSimplification.Editor.Localization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Meshia.MeshSimplification.Ndmf.Tests
{
    public class MeasuredMeshBudgetTests
    {
        [UnityTest]
        public System.Collections.IEnumerator AsyncMeasurementYieldsCachesAndRetainsItsSourceUntilFinished()
        {
            var source = new Mesh();
            var completion = new TaskCompletionSource<int>();
            var calls = 0;
            var mesh = new MeasuredMeshResponse(0, 100, 50, 60,
                _ => throw new Exception("Blocking evaluator must not run"), source,
                _ => { calls++; return completion.Task; });
            try
            {
                var pending = mesh.MeasureAsync(40);
                Assert.IsFalse(pending.IsCompleted, "Return control while work is pending.");
                yield return null;
                Assert.IsFalse(pending.IsCompleted);
                completion.SetResult(55);
                while (!pending.IsCompleted) yield return null;
                Assert.AreEqual(55, pending.Result);
                Assert.AreEqual(55, mesh.MeasureAsync(40).Result);
                Assert.AreEqual(1, calls, "Reuse measured output.");
            }
            finally { mesh.Dispose(); }
            Assert.IsTrue(source == null);

            source = new Mesh();
            completion = new TaskCompletionSource<int>();
            mesh = new MeasuredMeshResponse(0, 100, 50, 60,
                _ => throw new Exception("Blocking evaluator must not run"), source, _ => completion.Task);
            var outstanding = mesh.MeasureAsync(30);
            mesh.Dispose();
            Assert.IsTrue(source != null, "Do not destroy a source still used by a Unity job.");
            completion.SetResult(40);
            while (!outstanding.IsCompleted) yield return null;
            Assert.AreEqual(40, outstanding.Result);
            Assert.IsFalse(mesh.Outputs.ContainsKey(30), "A disposed cache must not publish late results.");
            Assert.IsTrue(source == null);
        }

        [UnityTest]
        public System.Collections.IEnumerator InspectorSkipsSupersededMeasurementsWithoutBlocking()
        {
            var avatar = new GameObject("Async budget fixture", typeof(nadena.dev.ndmf.runtime.components.NDMFAvatarRoot));
            for (var i = 0; i < 4; i++) GameObject.CreatePrimitive(PrimitiveType.Cube).transform.SetParent(avatar.transform);
            var settings = new GameObject("Settings"); settings.transform.SetParent(avatar.transform);
            var component = settings.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            component.RefreshEntries();
            foreach (var entry in component.Entries) entry.TargetTriangleCount = 6;
            var inspector = UnityEditor.Editor.CreateEditor(component);
            var completion = new TaskCompletionSource<int>();
            var type = inspector.GetType();
            var key = (string)type.GetMethod("GetBuildAnalysisResultKey", Stat).Invoke(null, new object[] { component });
            try
            {
                Seed(inspector, 24, x => x);
                var inputs = (MeasuredMeshSet)type.GetField("measuredMeshes", Inst).GetValue(inspector);
                inputs.Meshes[0] = new MeasuredMeshResponse(0, 12, 6, 6,
                    _ => throw new Exception("Must not block"), evaluateAsync: _ => completion.Task);
                var secondCalls = 0;
                inputs.Meshes[1] = new MeasuredMeshResponse(1, 12, 6, 6,
                    _ => throw new Exception("Must not block"), evaluateAsync: x => { secondCalls++; return Task.FromResult(x); });
                component.Entries[0].TargetTriangleCount = 4;
                component.Entries[1].TargetTriangleCount = 4;
                var snapshot = type.GetMethod("CaptureAllocations", Inst).Invoke(inspector, null);
                var method = type.GetMethod("MeasureAllocationProjectionAsync", Inst);
                var task = (Task)method.Invoke(inspector, new object[] { new VisualElement(), snapshot, new[] { 0, 1 }, "first" });
                Assert.IsFalse(task.IsCompleted);
                component.Entries[1].TargetTriangleCount = 3;
                yield return null;
                Assert.IsFalse(task.IsCompleted, "Editor updates continue while the first mesh runs.");
                completion.SetResult(4);
                while (!task.IsCompleted) yield return null;
                Assert.IsFalse(task.IsFaulted);
                Assert.AreEqual(0, secondCalls, "Skip the superseded target instead of queueing every slider value.");
                snapshot = type.GetMethod("CaptureAllocations", Inst).Invoke(inspector, null);
                task = (Task)method.Invoke(inspector, new object[] { new VisualElement(), snapshot, new[] { 1 }, "latest" });
                while (!task.IsCompleted) yield return null;
                Assert.IsFalse(task.IsFaulted);
                Assert.AreEqual(1, secondCalls);
                Assert.AreEqual(3, inputs.Meshes[1].Outputs[3]);
            }
            finally
            {
                completion.TrySetResult(4);
                ((System.Collections.IDictionary)type.GetField("BuildAnalysisCache", Stat).GetValue(null)).Remove(key);
                SessionState.EraseString(key); Undo.ClearUndo(component);
                Object.DestroyImmediate(inspector); Object.DestroyImmediate(avatar);
            }
        }

        [TestCase(-200)]
        [TestCase(200)]
        public void OnlyMeasuredResponsesEarnBudget(int change)
        {
            var flat = new MeasuredMeshResponse(0, 2000, 1000, 1000, _ => 1000);
            var responsive = new MeasuredMeshResponse(1, 2000, 1000, 1000, x => x);
            var plan = MeasuredMeshBudget.Plan(new[] { flat, responsive }, new[] { 1000, 1000 }, change);
            Assert.IsFalse(plan.ContainsKey(0));
            Assert.That(Math.Abs(responsive.Measure(plan[1]) - 1000 - change), Is.LessThan(40));
        }

        [TestCase(-200)]
        [TestCase(200)]
        public void ReversedResponseIsRejected(int change)
        {
            var mesh = new MeasuredMeshResponse(0, 2000, 1000, 1000, x => 2000 - x);
            Assert.IsEmpty(MeasuredMeshBudget.Plan(new[] { mesh }, new[] { 1000 }, change));
        }

        [Test]
        public void TrialsAreCachedAndBounded()
        {
            var calls = 0;
            var mesh = new MeasuredMeshResponse(0, 2000, 1000, 1000, x => { calls++; return x; });
            var first = MeasuredMeshBudget.Plan(new[] { mesh }, new[] { 1000 }, -100);
            Assert.That(calls, Is.InRange(1, 4));
            var measured = calls;
            CollectionAssert.AreEquivalent(first, MeasuredMeshBudget.Plan(new[] { mesh }, new[] { 1000 }, -100));
            Assert.AreEqual(measured, calls);
        }

        [Test]
        public void DiscontinuousOvershootAndZeroAllocationsAreRejected()
        {
            var mesh = new MeasuredMeshResponse(0, 1000, 100, 900, _ => 1);
            Assert.IsEmpty(MeasuredMeshBudget.Plan(new[] { mesh }, new[] { 100 }, -100));
            var linear = new MeasuredMeshResponse(0, 100, 50, 50, x => x);
            var plan = MeasuredMeshBudget.Plan(new[] { linear }, new[] { 50 }, -1000);
            Assert.AreEqual(1, plan[0]);
            Assert.Throws<OperationCanceledException>(() =>
                MeasuredMeshBudget.Plan(new[] { linear }, new[] { 50 }, -10, () => true));
        }

        [Test]
        public void IncreaseChoosesMeasuredUndershootInsteadOfCrossingBudget()
        {
            var mesh = new MeasuredMeshResponse(0, 2000, 1000, 1000, x => x);
            var plan = MeasuredMeshBudget.Plan(new[] { mesh }, new[] { 1000 }, 200);
            Assert.That(mesh.Measure(plan[0]) - 1000, Is.InRange(170, 200));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MeasuredPlansAreVerifiedAndRollbackWhenDownstreamSavingsDisappear(bool removeSavings)
        {
            var targets = new[] { 1000 };
            var builds = 0;
            var mesh = new MeasuredMeshResponse(0, 2000, 1000, 1000, x => x);
            var stop = MeshiaCascadingAvatarMeshSimplifierEditor.RunBoundedBuildFit(true, 800,
                _ => false,
                () => { builds++; return removeSavings ? 1000 : mesh.Measure(targets[0]); },
                () =>
                {
                    var plan = MeasuredMeshBudget.Plan(new[] { mesh }, targets, 800L - mesh.Measure(targets[0]));
                    foreach (var pair in plan) targets[pair.Key] = pair.Value;
                    return plan.Count > 0;
                },
                () => targets[0] = 1000);
            if (removeSavings)
            {
                Assert.AreEqual(MeshiaCascadingAvatarMeshSimplifierEditor.BuildFitStop.NoProgress, stop);
                Assert.AreEqual(1000, targets[0]);
            }
            else
            {
                Assert.AreEqual(MeshiaCascadingAvatarMeshSimplifierEditor.BuildFitStop.WithinBudget, stop);
                Assert.That(mesh.Measure(targets[0]), Is.InRange(799, 800));
            }
            Assert.That(builds, Is.InRange(2, 4));
        }

        private const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        public void ProjectionUsesOutputChangesAndRequiresCompatibleInputs(bool invalidate, bool recreate, bool plateau)
        {
            WithInspector((component, inspector) =>
            {
                var type = inspector.GetType();
                Seed(inspector, 20, plateau ? _ => 8 : x => x + 2);
                component.Entries[0].TargetTriangleCount = 3;
                var set = (MeasuredMeshSet)type.GetField("measuredMeshes", Inst).GetValue(inspector);
                set.Meshes[0].Measure(3);
                if (invalidate) type.GetMethod("InvalidateMeshInputs", Stat).Invoke(null, null);
                if (recreate) { set.Dispose(); type.GetField("measuredMeshes", Inst).SetValue(inspector, null); }
                type.GetMethod("InvalidateTriangleAnalysis", Stat).Invoke(null, null);
                var root = inspector.CreateInspectorGUI();
                type.GetMethod("RefreshBudgetGuidance", Inst).Invoke(inspector, new object[] { root });
                var text = root.Q<Label>("AllocationSummary").text;
                if (invalidate || recreate) StringAssert.Contains("analyze to update", text);
                else StringAssert.Contains(plateau ? "~20" : "~17", text);
            });
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        public void CorrectionHonorsAutoAdjustLocksExclusionsAndUndo(bool auto, bool stale)
        {
            WithInspector((component, inspector) =>
            {
                component.AutoAdjustEnabled = auto;
                component.TargetTriangleCount = 26;
                component.Entries[0].Enabled = false;
                component.Entries[1].Fixed = true;
                var type = inspector.GetType();
                Seed(inspector, 30, x => x);
                if (stale) type.GetMethod("InvalidateTriangleAnalysis", Stat).Invoke(null, null);
                Undo.IncrementCurrentGroup();
                var before = EditorJsonUtility.ToJson(component);
                var changed = (bool)type.GetMethod("ApplyAnalyzedBudgetCorrection", Inst).Invoke(inspector, new object[] { 30 });
                Assert.AreEqual(auto && !stale, changed);
                Assert.AreEqual(6, component.Entries[0].TargetTriangleCount);
                Assert.AreEqual(6, component.Entries[1].TargetTriangleCount);
                Assert.AreEqual(26, component.TargetTriangleCount);
                if (!changed) { Assert.AreEqual(before, EditorJsonUtility.ToJson(component)); return; }
                Assert.That(component.Entries.Skip(2).Sum(e => e.TargetTriangleCount), Is.LessThan(12));
                Assert.IsTrue(component.Entries.All(e => e.TargetTriangleCount > 0));
                Undo.PerformUndo();
                Assert.AreEqual(before, EditorJsonUtility.ToJson(component));
            });
        }

        private static void Seed(UnityEditor.Editor inspector, int total, Func<int, int> response)
        {
            var type = inspector.GetType();
            var component = (MeshiaCascadingAvatarMeshSimplifier)inspector.target;
            Undo.FlushUndoRecordObjects();
            var snapshot = type.GetMethod("CaptureAllocations", Inst).Invoke(inspector, null);
            snapshot.GetType().GetField("Outputs").SetValue(snapshot, Enumerable.Repeat(response(6), 4).ToArray());
            var set = new MeasuredMeshSet
            {
                Settings = (string)snapshot.GetType().GetField("Settings").GetValue(snapshot),
                InputRevision = (int)type.GetField("meshInputRevision", Stat).GetValue(null)
            };
            for (var i = 0; i < 4; i++) set.Meshes[i] = new MeasuredMeshResponse(i, 12, 6, response(6), response);
            type.GetField("measuredMeshes", Inst).SetValue(inspector, set);
            var revision = (int)type.GetProperty("CurrentAnalysisRevision", Stat).GetValue(null);
            var result = Activator.CreateInstance(type.GetNestedType("BuildAnalysisResult", BindingFlags.NonPublic),
                Inst, null, new[] { (object)total, 24, revision, null, snapshot }, null);
            type.GetMethod("StoreBuildAnalysisResult", Stat).Invoke(null, new object[] { component, result });
        }

        private static void WithInspector(Action<MeshiaCascadingAvatarMeshSimplifier, UnityEditor.Editor> run)
        {
            var locale = LocalizationProvider.CurrentLocale;
            LocalizationProvider.CurrentLocale = "en";
            var avatar = new GameObject("Measured budget fixture", typeof(nadena.dev.ndmf.runtime.components.NDMFAvatarRoot));
            for (var i = 0; i < 4; i++) GameObject.CreatePrimitive(PrimitiveType.Cube).transform.SetParent(avatar.transform);
            var settings = new GameObject("Settings"); settings.transform.SetParent(avatar.transform);
            var component = settings.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            component.RefreshEntries(); component.TargetTriangleCount = 18;
            foreach (var entry in component.Entries) entry.TargetTriangleCount = 6;
            var inspector = UnityEditor.Editor.CreateEditor(component);
            var type = inspector.GetType();
            var key = (string)type.GetMethod("GetBuildAnalysisResultKey", Stat).Invoke(null, new object[] { component });
            try { run(component, inspector); }
            finally
            {
                EditorUtility.ClearProgressBar();
                ((System.Collections.IDictionary)type.GetField("BuildAnalysisCache", Stat).GetValue(null)).Remove(key);
                SessionState.EraseString(key); Undo.ClearUndo(component);
                Object.DestroyImmediate(inspector); Object.DestroyImmediate(avatar);
                LocalizationProvider.CurrentLocale = locale;
            }
        }
    }
}
#endif
