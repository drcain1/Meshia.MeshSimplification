using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Meshia.MeshSimplification.Ndmf.Editor;
using nadena.dev.ndmf.preview;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Meshia.MeshSimplification.Ndmf.Tests
{
    public class CutMeshPreparationTests
    {
        private GameObject owner;
        private Mesh source;
        private SkinnedMeshRenderer renderer;
        private Type cutterType;
        private static MeshSimplificationTarget Target => new() { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 1 };

        [SetUp]
        public void SetUp()
        {
            cutterType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Anatawa12.AvatarOptimizer.RemoveMeshByBlendShape")).FirstOrDefault(t => t != null);
            if (cutterType == null) Assert.Ignore("Optional AAO integration is not installed.");
            owner = new GameObject("Cut preparation test");
            renderer = owner.AddComponent<SkinnedMeshRenderer>();
            source = Grid(); renderer.sharedMesh = source;
            // Ensure the optional integration is registered even if InitializeOnLoad
            // has not run yet in the test runner's fresh domain.
            var adapter = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Meshia.MeshSimplification.Ndmf.Editor.Aao.AaoCutPreparation")).FirstOrDefault(t => t != null);
            if (adapter == null) Assert.Ignore("AAO version does not support cut preparation.");
            adapter.GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (owner != null) Object.DestroyImmediate(owner);
            if (source != null) Object.DestroyImmediate(source);
            CutMeshPreparation.Invalidate();
        }

        private Component Cutter(string shape = "Cut", bool inverted = false)
        {
            var component = owner.AddComponent(cutterType);
            cutterType.GetMethod("Initialize").Invoke(component, new object[] { 1 });
            ((ICollection<string>)cutterType.GetProperty("ShapeKeys").GetValue(component)).Add(shape);
            cutterType.GetProperty("Tolerance").SetValue(component, .001);
            cutterType.GetProperty("InvertSelection").SetValue(component, inverted);
            return component;
        }

        [Test]
        public void ShouldCutOnlyCopiesAndPreserveAttributesAndFrames()
        {
            var cutter = Cutter();
            var settings = UnityEditor.EditorJsonUtility.ToJson(cutter);
            var indices = source.triangles;
            using (var prepared = CutMeshPreparation.Prepare(renderer, source, Target, MeshSimplifierOptions.Default))
            {
                Assert.IsTrue(prepared.Changed);
                Assert.Less(prepared.Mesh.triangles.Length, indices.Length);
                CollectionAssert.AreEqual(source.vertices, prepared.Mesh.vertices);
                CollectionAssert.AreEqual(source.uv, prepared.Mesh.uv);
                CollectionAssert.AreEqual(source.normals, prepared.Mesh.normals);
                Assert.AreEqual(source.blendShapeCount, prepared.Mesh.blendShapeCount);
                for (var frame = 0; frame < source.GetBlendShapeFrameCount(0); frame++)
                {
                    var a = new Vector3[source.vertexCount]; var b = new Vector3[source.vertexCount];
                    source.GetBlendShapeFrameVertices(0, frame, a, null, null);
                    prepared.Mesh.GetBlendShapeFrameVertices(0, frame, b, null, null);
                    CollectionAssert.AreEqual(a, b);
                }
            }
            Assert.AreSame(source, renderer.sharedMesh);
            CollectionAssert.AreEqual(indices, source.triangles);
            Assert.AreEqual(settings, UnityEditor.EditorJsonUtility.ToJson(cutter));
            Assert.IsFalse(Resources.FindObjectsOfTypeAll<GameObject>().Any(g => g.name == "Meshia cut prediction"));
        }

        [Test]
        public void ShouldApplyPaintedProtectionAfterAaoBlendshapeCuts()
        {
            Cutter();
            var component = owner.AddComponent<MeshiaMeshSimplifier>();
            var texture = new Texture2D(16, 16);
            var output = new Mesh();
            try
            {
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.SetPixels(Enumerable.Range(0, 256).Select(i => i % 16 < 4 ? Color.white : Color.black).ToArray());
                texture.Apply();
                component.ProtectedAreas.Add(new DecimationMask { Texture = texture, SourceMesh = source });
                using var prepared = CutMeshPreparation.Prepare(renderer, source, Target, MeshSimplifierOptions.Default);
                Assert.IsTrue(prepared.Changed);
                var selection = DecimationMasks.Resolve(renderer, prepared.Mesh);
                Assert.Greater(selection.Count, 0);
                Assert.Less(selection.Count, source.vertexCount, "Supported cuts must not force whole-mesh protection.");
                var history = MeshSimplifier.SimplifyWithHistory(prepared.Mesh, Target,
                    MeshSimplifierOptions.Default, null, output, selection);
                Assert.Less(output.triangles.Length, prepared.Mesh.triangles.Length);
                var inputPositions = prepared.Mesh.vertices;
                var outputPositions = output.vertices;
                var used = new HashSet<int>(prepared.Mesh.triangles);
                foreach (var index in used.Where(i => selection[i]))
                {
                    var outputIndex = Array.IndexOf(history.OutputVertexToSourceVertex, index);
                    Assert.GreaterOrEqual(outputIndex, 0);
                    Assert.AreEqual(inputPositions[index], outputPositions[outputIndex]);
                }
                using var repeated = CutMeshPreparation.Prepare(renderer, output, Target, MeshSimplifierOptions.Default);
                Assert.IsFalse(repeated.Changed, "The downstream AAO pass must not enlarge the cut.");
            }
            finally
            {
                Object.DestroyImmediate(texture); Object.DestroyImmediate(output); DecimationMasks.Clear();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShouldLeaveUnsupportedSelectionsAndAbsentShapesUnchanged(bool inverted)
        {
            Cutter(inverted ? "Cut" : "Missing", inverted);
            using var prepared = CutMeshPreparation.Prepare(renderer, source, Target, MeshSimplifierOptions.Default);
            Assert.IsFalse(prepared.Changed);
            Assert.AreSame(source, prepared.Mesh);
        }

        [Test]
        public void ShouldUseUpstreamProxyIndicesInsteadOfOriginalIndices()
        {
            Cutter();
            var proxy = Object.Instantiate(source);
            try
            {
                // An upstream filter removed every vertex selected by the cutter.
                // The original mesh still has them, so predicting against it is wrong.
                proxy.ClearBlendShapes();
                proxy.AddBlendShapeFrame("Cut", 100, new Vector3[proxy.vertexCount], null, null);
                using var prepared = CutMeshPreparation.Prepare(renderer, proxy, Target, MeshSimplifierOptions.Default, ComputeContext.NullContext);
                Assert.IsFalse(prepared.Changed);
                Assert.AreSame(proxy, prepared.Mesh);
            }
            finally { Object.DestroyImmediate(proxy); }
        }

        [UnityTest]
        public IEnumerator ShouldKeepCutRimInSyncAndAsyncSimplification()
        {
            Cutter();
            using var prepared = CutMeshPreparation.Prepare(renderer, source, Target, MeshSimplifierOptions.Default);
            var rim = BoundaryPositions(prepared.Mesh);
            var sync = new Mesh(); var asyncMesh = new Mesh();
            try
            {
                MeshSimplifier.Simplify(prepared.Mesh, Target, MeshSimplifierOptions.Default, sync);
                var task = MeshSimplifier.SimplifyAsync(prepared.Mesh, Target, MeshSimplifierOptions.Default, asyncMesh);
                while (!task.IsCompleted) yield return null;
                task.GetAwaiter().GetResult();
                foreach (var output in new[] { sync, asyncMesh })
                {
                    Assert.Less(output.triangles.Length, prepared.Mesh.triangles.Length);
                    CollectionAssert.AreEquivalent(rim, BoundaryPositions(output));
                    using var repeated = CutMeshPreparation.Prepare(renderer, output, Target, MeshSimplifierOptions.Default);
                    Assert.IsFalse(repeated.Changed, "AAO must not enlarge the cut when it runs later.");
                }
                CollectionAssert.AreEqual(sync.triangles, asyncMesh.triangles);
                CollectionAssert.AreEqual(sync.vertices, asyncMesh.vertices);
            }
            finally { Object.DestroyImmediate(sync); Object.DestroyImmediate(asyncMesh); }
        }

        [Test]
        public void ShouldNotApplyCutRatioTwiceToPreparedCounts()
        {
            Cutter();
            using var prepared = CutMeshPreparation.Prepare(renderer, source, Target, MeshSimplifierOptions.Default);
            var count = prepared.Mesh.triangles.Length / 3;
            DownstreamTriangleEstimator.Invalidate();
            Assert.AreEqual(count, DownstreamTriangleEstimator.EstimateFinalTriangleCount(renderer, count, count));
        }

        [Test]
        public void ShouldHandleACompletelyRemovedMesh()
        {
            source.ClearBlendShapes();
            source.AddBlendShapeFrame("Cut", 100,
                Enumerable.Repeat(Vector3.forward * .02f, source.vertexCount).ToArray(), null, null);
            Cutter();
            var output = new Mesh();
            try
            {
                using var prepared = CutMeshPreparation.Prepare(renderer, source, Target, MeshSimplifierOptions.Default);
                Assert.IsTrue(prepared.Changed);
                Assert.AreEqual(0, prepared.Mesh.triangles.Length);
                MeshSimplifier.Simplify(prepared.Mesh, Target, MeshSimplifierOptions.Default, output);
                Assert.AreEqual(0, output.triangles.Length);
                Assert.AreEqual(0, DownstreamTriangleEstimator.EstimateFinalTriangleCount(renderer, 0, 0));
                Assert.Greater(source.triangles.Length, 0);
            }
            finally { Object.DestroyImmediate(output); }
        }

        private sealed class CountWindow : EditorWindow { }

        [UnityTest]
        public IEnumerator ShouldRefreshOriginalAndSliderRangeWhenAaoSelectionChangesAndIsUndone()
        {
            source.AddBlendShapeFrame("All", 100,
                Enumerable.Repeat(Vector3.forward * .02f, source.vertexCount).ToArray(), null, null);
            var cutter = Cutter("Missing");
            var avatar = new GameObject("AAO slider test", typeof(nadena.dev.ndmf.runtime.components.NDMFAvatarRoot));
            owner.transform.SetParent(avatar.transform);
            var settingsObject = new GameObject("Meshia settings");
            settingsObject.transform.SetParent(avatar.transform);
            var component = settingsObject.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            component.AutoAdjustEnabled = false;
            component.RefreshEntries();
            var entry = component.Entries.Single();
            entry.Algorithm = MeshiaCascadingSimplificationAlgorithm.FaQem;
            entry.TargetTriangleCount = (source.triangles.Length / 3);
            var inspector = UnityEditor.Editor.CreateEditor(component);
            var window = ScriptableObject.CreateInstance<CountWindow>();
            try
            {
                var root = inspector.CreateInspectorGUI();
                window.rootVisualElement.Add(root); window.Show();
                for (var i = 0; i < 10; i++) yield return null;
                var row = root.Q<ListView>("EntriesListView").Query<TemplateContainer>().ToList()
                    .Single(r => r.userData is int index && index == 0);
                var slider = row.Q<SliderInt>("TargetTriangleCountSlider");
                var original = row.Q<IntegerField>("OriginalTriangleCountField");
                var refresh = inspector.GetType().GetMethod("RefreshBudgetGuidance", BindingFlags.Instance | BindingFlags.NonPublic);
                var before = EditorJsonUtility.ToJson(component);
                var changes = 0;
                slider.RegisterValueChangedCallback(_ => changes++);
                Assert.AreEqual((source.triangles.Length / 3), original.value);
                slider.SetValueWithoutNotify((source.triangles.Length / 3));

                void AssertRange(int expected)
                {
                    refresh.Invoke(inspector, new object[] { root });
                    Assert.AreEqual(expected, original.value, "Original must reflect the current supported cut.");
                    Assert.AreEqual(expected, slider.highValue, "The slider must end at the available input count.");
                    Assert.LessOrEqual(slider.value, slider.highValue);
                    Assert.AreEqual(0, changes, "Changing a range must not dispatch a slider edit.");
                    Assert.AreEqual(before, EditorJsonUtility.ToJson(component), "Refreshing cuts must not rewrite allocations or protections.");
                }
                void SelectShape(string name)
                {
                    Undo.IncrementCurrentGroup();
                    Undo.RecordObject(cutter, "Change AAO selection");
                    var shapes = (ICollection<string>)cutterType.GetProperty("ShapeKeys").GetValue(cutter);
                    shapes.Clear(); shapes.Add(name);
                    EditorUtility.SetDirty(cutter);
                    Undo.FlushUndoRecordObjects();
                    Undo.IncrementCurrentGroup();
                }

                SelectShape("Cut");
                yield return null;
                int cutCount;
                using (var cut = CutMeshPreparation.Prepare(renderer, source, Target, entry.Options))
                    cutCount = (cut.Mesh.triangles.Length / 3);
                Assert.Less(cutCount, (source.triangles.Length / 3));
                Assert.Greater(cutCount, 0);
                AssertRange(cutCount);
                Assert.AreEqual(cutCount, slider.value, "A full-detail slider must reach the new end without an edit.");

                Undo.PerformUndo(); yield return null;
                AssertRange((source.triangles.Length / 3));
                Undo.PerformRedo(); yield return null;
                AssertRange(cutCount);
                SelectShape("All"); yield return null;
                AssertRange(0);
                SelectShape("Missing"); yield return null;
                AssertRange((source.triangles.Length / 3));
            }
            finally
            {
                Undo.ClearUndo(cutter); Undo.ClearUndo(component);
                window.Close(); Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(avatar);
            }
        }

        internal static HashSet<Vector3> BoundaryPositions(Mesh mesh)
        {
            var edges = new Dictionary<(int, int), int>(); var t = mesh.triangles; var v = mesh.vertices;
            for (var i = 0; i < t.Length; i += 3)
                for (var j = 0; j < 3; j++)
                {
                    int a = t[i + j], b = t[i + (j + 1) % 3];
                    var edge = (Math.Min(a, b), Math.Max(a, b));
                    edges.TryGetValue(edge, out var n); edges[edge] = n + 1;
                }
            return edges.Where(e => e.Value == 1).SelectMany(e => new[] { v[e.Key.Item1], v[e.Key.Item2] }).ToHashSet();
        }

        internal static Mesh Grid()
        {
            const int n = 14;
            var vertices = new Vector3[n * n]; var normals = new Vector3[n * n];
            var uv = new Vector2[n * n]; var delta = new Vector3[n * n]; var triangles = new List<int>();
            for (var y = 0; y < n; y++) for (var x = 0; x < n; x++)
            {
                var i = y * n + x; vertices[i] = new Vector3(x * .01f, y * .01f, 0);
                normals[i] = Vector3.forward; uv[i] = new Vector2(x / (float)n, y / (float)n);
                if (y == 6 || y == 7) delta[i] = Vector3.forward * .02f;
                if (x + 1 < n && y + 1 < n) triangles.AddRange(new[] { i, i + 1, i + n, i + 1, i + n + 1, i + n });
            }
            var mesh = new Mesh { vertices = vertices, normals = normals, uv = uv, triangles = triangles.ToArray() };
            mesh.AddBlendShapeFrame("Cut", 50, delta, null, null);
            mesh.AddBlendShapeFrame("Cut", 100, delta, null, null);
            return mesh;
        }
    }
}
