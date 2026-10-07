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
        [Test]
        public async Task ConcurrentRequestsShareOneCountProfileAndRetainSourceUntilComplete()
        {
            var source = CutMeshPreparationTests.Grid();
            var completion = new TaskCompletionSource<FaQemCountProfile>();
            var calls = 0;
            var profile = MeshSimplifier.MeasureFaQemCounts(source, 0, MeshSimplifierOptions.Default);
            var response = new MeasuredMeshResponse(0, source.triangles.Length / 3, 300, 300,
                _ => throw new Exception("Must not use blocking measurement"), source,
                measureProfileAsync: () => { calls++; return completion.Task; });
            try
            {
                var first = response.MeasureAsync(100);
                var second = response.MeasureAsync(150);
                Assert.AreEqual(1, calls);
                response.Dispose();
                Assert.IsTrue(source != null);
                completion.SetResult(profile);
                var outputs = await Task.WhenAll(first, second);
                profile.TryGetOutput(100, out var expectedFirst);
                profile.TryGetOutput(150, out var expectedSecond);
                CollectionAssert.AreEqual(new[] { expectedFirst, expectedSecond }, outputs);
                Assert.IsTrue(source == null);
                Assert.IsFalse(response.Outputs.ContainsKey(100));
            }
            finally { response.Dispose(); }
        }

        [Test]
        public void CutMigrationUsesSurvivingOutputWithoutLoweringOtherMeshesBaseline()
        {
            var targets = new[] { 8550, 1000 };
            var outputs = new[] { 8550, 1000 };
            MeasuredMeshBudget.RebaseCutBudgets(targets, outputs,
                new System.Collections.Generic.Dictionary<int, int> { [0] = 4944 });
            CollectionAssert.AreEqual(new[] { 4944, 1000 }, targets);
            CollectionAssert.AreEqual(new[] { 4944, 1000 }, outputs);
            using var body = new MeasuredMeshResponse(0, 9258, 8550, 8550, x => Math.Max(5274, x));
            using var other = new MeasuredMeshResponse(1, 2000, 1000, 1000, x => x);
            var plan = MeasuredMeshBudget.Plan(new[] { body, other }, new[] { 8550, 1000 }, -3400,
                startingTargets: targets, startingOutputs: outputs);
            Assert.LessOrEqual(body.Measure(plan[0]), 5500);
            Assert.GreaterOrEqual(plan[1], 750, "Uncut meshes retain the normal 25% per-run limit.");
        }

        [TestCase(115, true)]
        [TestCase(0, false)]
        [TestCase(-1, false)]
        [TestCase(10000, false)]
        public void LearnsOnlyBoundedPositiveFinalResponseFromIsolatedVerifiedChanges(int finalDelta, bool expected)
        {
            var learned = MeasuredMeshBudget.TryLearnFinalScale(new[] { 1000, 200 }, new[] { 1292, 200 },
                new[] { 1000, 300 }, new[] { 1292, 300 }, 69672, 69672 + finalDelta, out var index, out var scale);
            Assert.AreEqual(expected, learned);
            Assert.AreEqual(0, index);
            if (expected) Assert.AreEqual(115d / 292, scale);
            Assert.False(MeasuredMeshBudget.TryLearnFinalScale(new[] { 1000, 200 }, new[] { 1292, 210 },
                new[] { 1000, 300 }, new[] { 1292, 300 }, 69672, 69787, out _, out _));
        }

        [TestCase(100, 1250)]
        [TestCase(-100, 750)]
        [TestCase(-150, 750)]
        public void UsesLearnedFinalResponseWithoutExceedingReductionFloor(int change, int expectedTarget)
        {
            using var mesh = new MeasuredMeshResponse(0, 2000, 1000, 1000, x => x);
            var plan = MeasuredMeshBudget.Plan(new[] { mesh }, new[] { 1000 }, change,
                finalScales: new System.Collections.Generic.Dictionary<int, double> { [0] = .4 });
            Assert.AreEqual(expectedTarget, plan[0]);
        }

        [Test]
        public void LearnsDownstreamLossDuringFittingInsteadOfRepeatedlyUnderfilling()
        {
            using var mesh = new MeasuredMeshResponse(0, 2000, 1000, 1000, x => x);
            var scales = new System.Collections.Generic.Dictionary<int, double>();
            var target = 1000;
            var beforeTarget = target;
            var beforeFinal = 700;
            var final = beforeFinal;
            var builds = 0;
            var stop = MeshiaCascadingAvatarMeshSimplifierEditor.RunBoundedBuildFit(true, 800,
                _ => false,
                () =>
                {
                    builds++;
                    final = 300 + (int)(target * .4);
                    if (MeasuredMeshBudget.TryLearnFinalScale(new[] { beforeTarget }, new[] { target },
                        new[] { beforeTarget }, new[] { target }, beforeFinal, final, out var index, out var scale)) scales[index] = scale;
                    beforeTarget = target; beforeFinal = final;
                    return final;
                },
                () =>
                {
                    var plan = MeasuredMeshBudget.Plan(new[] { mesh }, new[] { target }, 800 - final, finalScales: scales);
                    if (!plan.TryGetValue(0, out var next)) return false;
                    target = next; return true;
                }, initialEstimate: 700);
            Assert.AreEqual(MeshiaCascadingAvatarMeshSimplifierEditor.BuildFitStop.WithinBudget, stop);
            Assert.AreEqual(2, builds);
            Assert.AreEqual(800, final);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void CountProfilesServeNewRequestsWithoutRepeatedSimplification(bool asynchronous)
        {
            var source = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }
            };
            try
            {
                var partial = MeshSimplifier.MeasureFaQemCounts(source, 1, MeshSimplifierOptions.Default);
                var complete = MeshSimplifier.MeasureFaQemCounts(source, 0, MeshSimplifierOptions.Default);
                var calls = 0;
                using var response = new MeasuredMeshResponse(0, 1, 1, 1,
                    _ => throw new Exception("Legacy evaluator must not run"),
                    countProfile: partial,
                    measureProfile: () => { calls++; return complete; },
                    measureProfileAsync: () => { calls++; return Task.FromResult(complete); });
                Assert.AreEqual(1, response.Measure(2), "Higher targets are already covered by the build profile.");
                Assert.AreEqual(0, calls);
                if (asynchronous) response.MeasureAsync(0).GetAwaiter().GetResult();
                else response.Measure(0);
                Assert.AreEqual(1, calls);
                Assert.True(response.TryGetOutput(3, out var output));
                Assert.AreEqual(1, output);
                response.Measure(0);
                Assert.AreEqual(1, calls, "The complete count sequence is reusable.");
            }
            finally { Object.DestroyImmediate(source); }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PreplanningAlwaysRunsARealVerificationEvenWhenNoCorrectionIsAvailable(bool capacity)
        {
            var corrected = false;
            var builds = 0;
            var stop = MeshiaCascadingAvatarMeshSimplifierEditor.RunBoundedBuildFit(true, 70000,
                _ => false,
                () => { builds++; Assert.AreEqual(capacity, corrected); return 70000; },
                () => { corrected = capacity; return capacity; },
                initialEstimate: 70700);
            Assert.AreEqual(MeshiaCascadingAvatarMeshSimplifierEditor.BuildFitStop.WithinBudget, stop);
            Assert.AreEqual(1, builds);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedVerificationRestoresAllocationsChangedBeforeFirstBuild(bool throws)
        {
            var allocation = 100;
            void Run() => MeshiaCascadingAvatarMeshSimplifierEditor.RunBoundedBuildFit(true, 70000,
                _ => false, () => throws ? throw new InvalidOperationException("Failed") : (int?)null,
                () => { allocation = 90; return true; }, () => allocation = 100, 70700);
            if (throws) Assert.Throws<InvalidOperationException>(Run);
            else Run();
            Assert.AreEqual(100, allocation);
        }

        [TestCase(false, 70700)]
        [TestCase(true, 69960)]
        public void PreplanningPreservesManualModeAndVerifiesPredictedSuccess(bool automatic, int estimate)
        {
            var builds = 0;
            var corrections = 0;
            MeshiaCascadingAvatarMeshSimplifierEditor.RunBoundedBuildFit(automatic, 70000,
                _ => false, () => { builds++; return 69990; }, () => { corrections++; return true; }, initialEstimate: estimate);
            Assert.AreEqual(1, builds);
            Assert.AreEqual(0, corrections);
        }
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
            Assert.AreEqual(38, plan[0], "Automatic fitting preserves at least 75% of the starting target.");
            Assert.Throws<OperationCanceledException>(() =>
                MeasuredMeshBudget.Plan(new[] { linear }, new[] { 50 }, -10, () => true));
        }

        [Test]
        public void LargeProtectedOverrunIsSharedWithoutSacrificingTheHalo()
        {
            var halo = new MeasuredMeshResponse(0, 8340, 1988, 1988, x => x);
            var body = new MeasuredMeshResponse(1, 22160, 15183, 15183, x => x);
            var shoes = new MeasuredMeshResponse(2, 8892, 3086, 6066, _ => 6066);
            var targets = new[] { 1988, 15183, 3086 };
            var plan = MeasuredMeshBudget.Plan(new[] { halo, body, shoes }, targets, -2980);
            Assert.That(plan[0], Is.InRange(1491, 1987));
            Assert.That(plan[1], Is.InRange(11388, 15182));
            Assert.IsFalse(plan.ContainsKey(2));
            Assert.That((1988 - plan[0]) / 1988.0,
                Is.EqualTo((15183 - plan[1]) / 15183.0).Within(.002));
            Assert.That((1988 - plan[0]) + (15183 - plan[1]), Is.InRange(2978, 2980));
        }

        [Test]
        public void VerificationPassesCannotErodeTheStartingSafetyFloor()
        {
            var mesh = new MeasuredMeshResponse(0, 2000, 1000, 1000, x => x);
            var start = new[] { 1000 };
            var first = MeasuredMeshBudget.Plan(new[] { mesh }, start, -900, startingTargets: start, startingOutputs: start);
            Assert.AreEqual(750, first[0]);
            Assert.IsEmpty(MeasuredMeshBudget.Plan(new[] { mesh }, new[] { 750 }, -650,
                startingTargets: start, startingOutputs: start));
        }

        [Test]
        public void NonlinearOutputCannotLoseMoreThanQuarterEvenAboveTargetFloor()
        {
            var mesh = new MeasuredMeshResponse(0, 2000, 1000, 1000, x => x < 999 ? 100 : 1000);
            Assert.IsEmpty(MeasuredMeshBudget.Plan(new[] { mesh }, new[] { 1000 }, -900));
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

        [TestCase("linear", 20)]
        [TestCase("plateau", 0)]
        [TestCase("cliff", 0)]
        [TestCase("floor", 10)]
        [TestCase("reversed", 0)]
        public void ManualReductionOnlyOffersMeasuredSavingsWithinRequestedAmount(string scenario, int expected)
        {
            var calls = 0;
            int Output(int n) => scenario switch
            {
                "plateau" => 100,
                "cliff" => n < 90 ? 20 : 100,
                "floor" => Math.Max(90, n),
                "reversed" => 200 - n,
                _ => n
            };
            using var mesh = new MeasuredMeshResponse(0, 200, 100, 100,
                _ => throw new Exception("Must use asynchronous measurement"), evaluateAsync: n =>
                { calls++; return Task.FromResult(Output(n)); });
            var target = MeasuredMeshBudget.FindReductionAsync(mesh, 100, 20, () => true).GetAwaiter().GetResult();
            Assert.AreEqual(expected, 100 - mesh.Outputs[target]);
            Assert.That(target, Is.InRange(1, 100));
            Assert.That(calls, Is.LessThanOrEqualTo(8));
        }

        [UnityTest]
        public System.Collections.IEnumerator ManualReductionRejectsResultsAfterSettingsChange()
        {
            var completion = new TaskCompletionSource<int>();
            var valid = true;
            using var mesh = new MeasuredMeshResponse(0, 100, 100, 100, _ => 0,
                evaluateAsync: _ => completion.Task);
            var task = MeasuredMeshBudget.FindReductionAsync(mesh, 100, 20, () => valid);
            Assert.IsFalse(task.IsCompleted);
            valid = false;
            completion.SetResult(80);
            while (!task.IsCompleted) yield return null;
            Assert.IsTrue(task.IsCanceled);
        }

        [Test]
        public void ExplicitReductionKeepsOtherMeshesAndReservesSavingsWithUndo()
        {
            WithInspector((component, inspector) =>
            {
                component.BuildTriangleReserve = -8;
                component.AutoAdjustEnabled = true;
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();
                inspector.GetType().GetMethod("ApplyMeshReduction", Inst).Invoke(inspector, new object[] { 0, 4 });
                Assert.AreEqual(4, component.Entries[0].TargetTriangleCount);
                Assert.IsTrue(component.Entries.Skip(1).All(x => x.TargetTriangleCount == 6));
                Assert.AreEqual(-6, component.BuildTriangleReserve);
                Assert.IsTrue(component.AutoAdjustEnabled);
                Undo.PerformUndo();
                Assert.IsTrue(component.Entries.All(x => x.TargetTriangleCount == 6));
                Assert.AreEqual(-8, component.BuildTriangleReserve);
            });
        }

        [Test]
        public void MeshPopupUsesRemainingMeasuredGapAndRejectsChangedBudget()
        {
            WithInspector((component, inspector) =>
            {
                Seed(inspector, 24, x => x);
                component.Entries[0].TargetTriangleCount = 3;
                var set = (MeasuredMeshSet)inspector.GetType().GetField("measuredMeshes", Inst).GetValue(inspector);
                set.Meshes[0].Measure(3);
                var type = inspector.GetType().GetNestedType("MeshOutputPopup", BindingFlags.NonPublic);
                var popup = Activator.CreateInstance(type, Inst, null, new object[] { inspector, 0 }, null);
                Assert.AreEqual(3, type.GetField("reduction", Inst).GetValue(popup));
                Assert.IsTrue((bool)type.GetMethod("Current", Inst).Invoke(popup, null));
                component.TargetTriangleCount++;
                Assert.IsFalse((bool)type.GetMethod("Current", Inst).Invoke(popup, null));
            });
        }

        [TestCase(0)]
        [TestCase(6)]
        [TestCase(9)]
        public void ExplicitReductionRejectsInvalidOrNonReducingTargets(int target)
        {
            WithInspector((component, inspector) =>
            {
                inspector.GetType().GetMethod("ApplyMeshReduction", Inst).Invoke(inspector, new object[] { 0, target });
                Assert.AreEqual(6, component.Entries[0].TargetTriangleCount);
                Assert.AreEqual(0, component.BuildTriangleReserve);
            });
        }

        [Test]
        public void ExplicitReductionHonorsLocks()
        {
            WithInspector((component, inspector) =>
            {
                component.Entries[0].Fixed = true;
                inspector.GetType().GetMethod("ApplyMeshReduction", Inst).Invoke(inspector, new object[] { 0, 3 });
                Assert.AreEqual(6, component.Entries[0].TargetTriangleCount);
                Assert.AreEqual(0, component.BuildTriangleReserve);
            });
        }

        [TestCase(60, 60)]
        [TestCase(100, 100)]
        [TestCase(150, 150)]
        public void OutputEditsSearchMeasuredCountsInBothDirections(int desired, int expected)
        {
            using var mesh = new MeasuredMeshResponse(0, 200, 60, 100, x => x + 40,
                evaluateAsync: x => Task.FromResult(Math.Min(200, x + 40)));
            var target = MeasuredMeshBudget.FindOutputTargetAsync(mesh, 60, desired, () => true).GetAwaiter().GetResult();
            Assert.AreEqual(expected, mesh.Outputs[target]);
            if (desired == 100) Assert.AreEqual(60, target, "Displaying 100 must not rewrite the request to 100.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UndoRedoRestoresMeasuredSliderValuesAndKeepsThemEditable(bool autoAdjust)
        {
            WithInspector((component, inspector) =>
            {
                component.AutoAdjustEnabled = autoAdjust;
                Seed(inspector, 32, x => x + 2);
                var type = inspector.GetType();
                var inputs = (MeasuredMeshSet)type.GetField("measuredMeshes", Inst).GetValue(inspector);
                var row = new TemplateContainer { userData = 0 };
                row.Add(new SliderInt(0, 12) { name = "TargetTriangleCountSlider" });
                row.Add(new IntegerField { name = "TargetTriangleCountField" });
                row.Add(new Label { name = "MeasuredOutputHint" });
                var root = new VisualElement();
                root.Add(row);
                void Refresh()
                {
                    var snapshot = type.GetMethod("CaptureAllocations", Inst).Invoke(inspector, null);
                    type.GetMethod("RefreshAllocationRows", Inst).Invoke(inspector, new[] { root, snapshot });
                }
                type.GetMethod("SetManualAllocation", Inst).Invoke(inspector, new object[] { 0, 3 });
                var edited = component.Entries.Select(e => e.TargetTriangleCount).ToArray();
                inputs.Meshes[0].Measure(3);
                Refresh();
                Assert.AreEqual(5, row.Q<SliderInt>().value);
                UndoRedoInfo undone = default;
                Undo.UndoRedoEventCallback capture = (in UndoRedoInfo info) => undone = info;
                Undo.undoRedoEvent += capture;
                try { Undo.PerformUndo(); }
                finally { Undo.undoRedoEvent -= capture; }
                Refresh();
                Assert.AreEqual(6, component.Entries[0].TargetTriangleCount);
                Assert.IsTrue(component.Entries.All(e => e.TargetTriangleCount == 6), "Restore automatic redistribution too.");
                Assert.AreEqual(8, row.Q<SliderInt>().value);
                Assert.IsTrue(row.Q<SliderInt>().enabledSelf, "Undo of only an allocation must retain measured inputs. " +
                    undone.undoGroup + ":" + undone.undoName + "; groups=" + string.Join(",", (System.Collections.Generic.Dictionary<int, string>)
                        type.GetField("allocationUndoGroups", Stat).GetValue(null)) + "; revisions=" + inputs.InputRevision + "/" + type.GetField("meshInputRevision", Stat).GetValue(null));
                Undo.PerformRedo();
                Refresh();
                Assert.AreEqual(3, component.Entries[0].TargetTriangleCount);
                CollectionAssert.AreEqual(edited, component.Entries.Select(e => e.TargetTriangleCount).ToArray());
                Assert.AreEqual(5, row.Q<SliderInt>().value, "Redo must show the restored output, not the last full build.");
                Assert.AreEqual(5, row.Q<IntegerField>().value);
                Assert.IsTrue(row.Q<SliderInt>().enabledSelf);
            });
        }

        [Test]
        public void SharedRowRefreshSeesNewMeasurementsAndProtectionChanges()
        {
            WithInspector((component, inspector) =>
            {
                component.AutoAdjustEnabled = false;
                Seed(inspector, 32, x => x + 2);
                var type = inspector.GetType();
                var inputs = (MeasuredMeshSet)type.GetField("measuredMeshes", Inst).GetValue(inspector);
                var root = new VisualElement();
                var rows = Enumerable.Range(0, component.Entries.Count).Select(index =>
                {
                    var row = new TemplateContainer { userData = index };
                    row.Add(new SliderInt(0, 12) { name = "TargetTriangleCountSlider" });
                    row.Add(new IntegerField { name = "TargetTriangleCountField" });
                    root.Add(row);
                    return row;
                }).ToArray();
                void Refresh()
                {
                    var snapshot = type.GetMethod("CaptureAllocations", Inst).Invoke(inspector, null);
                    type.GetMethod("RefreshAllocationRows", Inst).Invoke(inspector, new[] { root, snapshot });
                }
                Refresh();
                Assert.IsTrue(rows.All(row => row.Q<SliderInt>().value == 8 && row.Q<SliderInt>().enabledSelf));
                for (var index = 0; index < rows.Length; index++)
                {
                    type.GetMethod("SetManualAllocation", Inst).Invoke(inspector, new object[] { index, index + 1 });
                    inputs.Meshes[index].Measure(index + 1);
                }
                Refresh();
                for (var index = 0; index < rows.Length; index++)
                {
                    Assert.AreEqual(index + 3, rows[index].Q<SliderInt>().value);
                    Assert.AreEqual(index + 3, rows[index].Q<IntegerField>().value);
                    Assert.IsTrue(rows[index].Q<SliderInt>().enabledSelf);
                }
                component.Entries[0].Options.PreserveBorderEdges = !component.Entries[0].Options.PreserveBorderEdges;
                Refresh();
                Assert.IsTrue(rows.All(row => !row.Q<SliderInt>().enabledSelf),
                    "A shared snapshot must not survive into the next refresh after protection changes.");
            });
        }

        [Test]
        public async Task UndoRedoCancelsAnInFlightSliderEditEvenWhenRedoRestoresItsStartingCount()
        {
            await WithInspectorAsync(async (component, inspector) =>
            {
                component.AutoAdjustEnabled = false;
                Seed(inspector, 32, x => x + 2);
                var type = inspector.GetType();
                var inputs = (MeasuredMeshSet)type.GetField("measuredMeshes", Inst).GetValue(inspector);
                var completion = new TaskCompletionSource<int>();
                inputs.Meshes[0] = new MeasuredMeshResponse(0, 12, 6, 8, x => x + 2,
                    evaluateAsync: _ => completion.Task);
                type.GetMethod("SetManualAllocation", Inst).Invoke(inspector, new object[] { 0, 3 });
                inputs.Meshes[0].Measure(3);
                type.GetField("pendingOutputIndex", Inst).SetValue(inspector, 0);
                type.GetField("pendingOutputCount", Inst).SetValue(inspector, 7);
                var serial = (int)type.GetField("outputEditSerial", Inst).GetValue(inspector);
                var pending = (Task)type.GetMethod("ApplyOutputEditAsync", Inst).Invoke(inspector,
                    new object[] { new VisualElement(), 0, 7, serial });
                Assert.IsFalse(pending.IsCompleted);
                Undo.PerformUndo();
                Assert.AreEqual(-1, type.GetField("pendingOutputIndex", Inst).GetValue(inspector));
                Assert.AreEqual(6, component.Entries[0].TargetTriangleCount);
                Undo.PerformRedo();
                Assert.AreEqual(3, component.Entries[0].TargetTriangleCount);
                completion.SetResult(7);
                await pending;
                Assert.AreEqual(3, component.Entries[0].TargetTriangleCount, "An obsolete request must not overwrite Redo.");
                Assert.IsFalse((bool)type.GetField("estimateRunning", Inst).GetValue(inspector));
            });
        }

        [Test]
        public void UndoOfProtectionOrGeometryStillInvalidatesMeasuredInputs()
        {
            WithInspector((component, inspector) =>
            {
                component.AutoAdjustEnabled = false;
                Seed(inspector, 32, x => x + 2);
                var type = inspector.GetType();
                type.GetMethod("SetManualAllocation", Inst).Invoke(inspector, new object[] { 0, 3 });
                Undo.RecordObject(component, "Change protection");
                component.Entries[0].Options.PreserveBorderEdges = !component.Entries[0].Options.PreserveBorderEdges;
                Undo.FlushUndoRecordObjects();
                Seed(inspector, 32, x => x + 2);
                var revision = (int)type.GetField("meshInputRevision", Stat).GetValue(null);
                Undo.PerformUndo();
                Assert.Greater((int)type.GetField("meshInputRevision", Stat).GetValue(null), revision);
            });
        }

        [Test]
        public void OutputEditPlateauKeepsOriginalRequest()
        {
            using var mesh = new MeasuredMeshResponse(0, 200, 60, 100, _ => 100,
                evaluateAsync: _ => Task.FromResult(100));
            Assert.AreEqual(60, MeasuredMeshBudget.FindOutputTargetAsync(mesh, 60, 80, () => true).GetAwaiter().GetResult());
            Assert.AreEqual(60, MeasuredMeshBudget.FindOutputTargetAsync(mesh, 60, 120, () => true).GetAwaiter().GetResult());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OutputEditAppliesMeasuredRequestAndReportsPlateaus(bool plateau)
        {
            WithInspector((component, inspector) =>
            {
                component.AutoAdjustEnabled = false;
                Seed(inspector, 32, _ => 8);
                var inputs = (MeasuredMeshSet)inspector.GetType().GetField("measuredMeshes", Inst).GetValue(inspector);
                inputs.Meshes[0] = new MeasuredMeshResponse(0, 12, 6, 8, x => plateau ? 8 : x + 2,
                    evaluateAsync: x => Task.FromResult(plateau ? 8 : x + 2));
                var apply = inspector.GetType().GetMethod("ApplyOutputEditAsync", Inst);
                var task = (Task)apply.Invoke(inspector, new object[] { new VisualElement(), 0, 5, 0 });
                task.GetAwaiter().GetResult();
                Assert.AreEqual(plateau ? 6 : 3, component.Entries[0].TargetTriangleCount);
                Assert.IsTrue(component.Entries.Skip(1).All(x => x.TargetTriangleCount == 6));
                if (plateau)
                {
                    Assert.AreEqual(0, inspector.GetType().GetField("outputFeedbackIndex", Inst).GetValue(inspector));
                    StringAssert.Contains("No output change", (string)inspector.GetType().GetField("outputFeedback", Inst).GetValue(inspector));
                }
                else
                {
                    Assert.AreEqual(5, inputs.Meshes[0].Outputs[3]);
                    Undo.PerformUndo();
                    Assert.AreEqual(6, component.Entries[0].TargetTriangleCount);
                }
            });
        }

        [UnityTest]
        public System.Collections.IEnumerator SupersededOutputEditsNeverChangeAllocations()
        {
            // The first evaluation yields; a newer edit supersedes it before completion.
            Task pending = null;
            var completion = new TaskCompletionSource<int>();
            WithInspector((component, inspector) =>
            {
                Seed(inspector, 32, _ => 8);
                var type = inspector.GetType();
                var inputs = (MeasuredMeshSet)type.GetField("measuredMeshes", Inst).GetValue(inspector);
                inputs.Meshes[0] = new MeasuredMeshResponse(0, 12, 6, 8, _ => 5,
                    evaluateAsync: _ => completion.Task);
                pending = (Task)type.GetMethod("ApplyOutputEditAsync", Inst).Invoke(inspector,
                    new object[] { new VisualElement(), 0, 5, 0 });
                Assert.IsFalse(pending.IsCompleted);
                type.GetField("outputEditSerial", Inst).SetValue(inspector, 1);
                completion.SetResult(5);
                // Completed tasks resume on a later editor update; destroying the inspector
                // also exercises ownership/cancellation without modifying the avatar.
                Assert.IsTrue(component.Entries.All(x => x.TargetTriangleCount == 6));
            });
            while (!pending.IsCompleted) yield return null;
            Assert.IsFalse(pending.IsFaulted);
        }

        [Test]
        public void UnmeasuredOutputIsUnknownInsteadOfAnAllocation()
        {
            WithInspector((component, inspector) =>
            {
                var row = new TemplateContainer { userData = 0 };
                row.Add(new SliderInt { name = "TargetTriangleCountSlider" });
                row.Add(new IntegerField { name = "TargetTriangleCountField" });
                row.Add(new TextField { name = "UnknownOutputField", value = "—" });
                row.Add(new Label { name = "MeasuredOutputHint" });
                inspector.GetType().GetMethod("RefreshAllocationFields", Inst).Invoke(inspector, new object[] { row });
                Assert.AreEqual(DisplayStyle.None, row.Q<IntegerField>("TargetTriangleCountField").style.display.value);
                Assert.AreEqual(DisplayStyle.Flex, row.Q<TextField>("UnknownOutputField").style.display.value);
                Assert.IsFalse(row.Q<SliderInt>("TargetTriangleCountSlider").enabledSelf);
                Assert.AreEqual("?", row.Q<Label>("MeasuredOutputHint").text);
            });
        }

        private const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;

        [TestCase("match", "", false)]
        [TestCase("rounding", "", false)]
        [TestCase("different", "", false)]
        [TestCase("unchanged", "", false)]
        [TestCase("saving", "", false)]
        [TestCase("pending", "…", true)]
        [TestCase("stale", "?", true)]
        [TestCase("excluded", "", false)]
        public void OutputHintOnlyShowsUsefulCurrentMeasurements(string scenario, string expected, bool visible)
        {
            WithInspector((component, inspector) =>
            {
                var type = inspector.GetType();
                Seed(inspector, 24, x => scenario == "match" || scenario == "saving" ? x : scenario == "rounding" ? x + 1 : 12);
                var inputs = (MeasuredMeshSet)type.GetField("measuredMeshes", Inst).GetValue(inspector);
                if (scenario == "unchanged" || scenario == "pending" || scenario == "saving") component.Entries[0].TargetTriangleCount = 3;
                if (scenario == "unchanged" || scenario == "saving") inputs.Meshes[0].Measure(3);
                if (scenario == "excluded") component.Entries[0].Enabled = false;
                if (scenario == "stale")
                {
                    type.GetMethod("InvalidateMeshInputs", Stat).Invoke(null, null);
                    type.GetMethod("InvalidateTriangleAnalysis", Stat).Invoke(null, null);
                }
                var row = new TemplateContainer { userData = 0 };
                row.Add(new SliderInt { name = "TargetTriangleCountSlider" });
                row.Add(new IntegerField { name = "TargetTriangleCountField" });
                row.Add(new Label { name = "MeasuredOutputHint" });
                row.Add(new Label { name = "TriangleCountDivider", text = "/" });
                var refresh = type.GetMethod("RefreshAllocationFields", Inst);
                refresh.Invoke(inspector, new object[] { row });
                var hint = row.Q<Label>("MeasuredOutputHint");
                Assert.AreEqual(expected, hint.text);
                Assert.AreEqual(visible ? DisplayStyle.None : DisplayStyle.Flex, row.Q<Label>("TriangleCountDivider").style.display.value);
                Assert.AreEqual(visible, hint.style.display.value == DisplayStyle.Flex);
                if (scenario == "different" || scenario == "unchanged" || scenario == "pending" || scenario == "stale")
                    Assert.AreEqual(12, row.Q<IntegerField>("TargetTriangleCountField").value, "Show actual or last measured output, never the internal request.");
                if (scenario == "pending") StringAssert.Contains("Measuring", hint.tooltip);
                if (scenario == "stale") StringAssert.Contains("out of date", hint.tooltip);
                if (scenario == "stale") Assert.IsFalse(row.Q<IntegerField>("TargetTriangleCountField").enabledSelf);
                if (scenario == "saving")
                {
                    Assert.AreEqual(3, row.Q<IntegerField>("TargetTriangleCountField").value);
                    StringAssert.Contains("6 → 3", hint.tooltip);
                }
                if (scenario == "stale")
                    Assert.AreEqual(hint.style.color.value.r, hint.style.color.value.b, "Stale results are neutral.");
                if (scenario == "different")
                {
                    LocalizationProvider.CurrentLocale = "ja";
                    refresh.Invoke(inspector, new object[] { row });
                    StringAssert.Contains("出力", hint.tooltip);
                }
                component.Entries[0].Enabled = false;
                refresh.Invoke(inspector, new object[] { row });
                Assert.AreEqual(DisplayStyle.None, hint.style.display.value, "Clear hints when a recycled row becomes excluded.");
            });
        }

        [Test]
        public void BudgetMarginExplainsToleranceWithoutAnotherVisibleLine()
        {
            WithInspector((component, inspector) =>
            {
                component.TargetTriangleCount = 70000;
                Seed(inspector, 69956, x => x);
                var root = inspector.CreateInspectorGUI();
                inspector.GetType().GetMethod("RefreshBudgetGuidance", Inst).Invoke(inspector, new object[] { root });
                var summary = root.Q<Label>("BudgetSummary");
                Assert.AreEqual("Within budget · 44 below limit", summary.text);
                StringAssert.Contains("70 triangles below", summary.tooltip);
                Assert.AreEqual("Auto Adjust", root.Q<Toggle>("AutoAdjustEnabledToggle").label);
            });
        }

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
                var changed = (bool)type.GetMethod("ApplyAnalyzedBudgetCorrection", Inst).Invoke(inspector, new object[] { 30, null });
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

        internal static void Seed(UnityEditor.Editor inspector, int total, Func<int, int> response)
        {
            var type = inspector.GetType();
            var component = (MeshiaCascadingAvatarMeshSimplifier)inspector.target;
            Undo.FlushUndoRecordObjects();
            var snapshot = type.GetMethod("CaptureAllocations", Inst).Invoke(inspector, null);
            snapshot.GetType().GetField("Outputs").SetValue(snapshot, component.Entries.Select(e => response(e.TargetTriangleCount)).ToArray());
            var set = new MeasuredMeshSet
            {
                Settings = (string)snapshot.GetType().GetField("Settings").GetValue(snapshot),
                InputRevision = (int)type.GetField("meshInputRevision", Stat).GetValue(null)
            };
            for (var i = 0; i < component.Entries.Count; i++)
            {
                var count = component.Entries[i].TargetTriangleCount;
                set.Meshes[i] = new MeasuredMeshResponse(i, 12, count, response(count), response,
                    evaluateAsync: n => Task.FromResult(response(n)));
            }
            ((MeasuredMeshSet)type.GetField("measuredMeshes", Inst).GetValue(inspector))?.Dispose();
            type.GetField("measuredMeshes", Inst).SetValue(inspector, set);
            var revision = (int)type.GetProperty("CurrentAnalysisRevision", Stat).GetValue(null);
            var result = Activator.CreateInstance(type.GetNestedType("BuildAnalysisResult", BindingFlags.NonPublic),
                Inst, null, new[] { (object)total, 24, revision, null, snapshot }, null);
            type.GetMethod("StoreBuildAnalysisResult", Stat).Invoke(null, new object[] { component, result });
        }

        private static void WithInspector(Action<MeshiaCascadingAvatarMeshSimplifier, UnityEditor.Editor> run)
            => WithInspectorAsync((component, inspector) =>
            {
                run(component, inspector);
                return Task.CompletedTask;
            }).GetAwaiter().GetResult();

        private static async Task WithInspectorAsync(Func<MeshiaCascadingAvatarMeshSimplifier, UnityEditor.Editor, Task> run)
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
            try { await run(component, inspector); }
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
