#nullable enable
using System;
using System.Linq;
using Meshia.MeshSimplification.Ndmf.Editor;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Meshia.MeshSimplification.Ndmf.Tests
{
    public class DecimationMaskTests
    {
        [Test]
        public void ShouldKeepSavedPrefabMaskSettingsAndRefreshReimportedPaint()
        {
            var folder = "Assets/MeshiaMaskValidation_" + Guid.NewGuid().ToString("N");
            // Round-trip serialized settings in a private scene without switching
            // the user's working scene or prompting to save it.
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            UnityEditor.AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            var pixels = new Texture2D(16, 16, TextureFormat.RGBA32, false, true);
            try
            {
                var root = new GameObject("Saved mask avatar", typeof(nadena.dev.ndmf.runtime.components.NDMFAvatarRoot));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                var child = new GameObject("Mesh", typeof(MeshFilter), typeof(MeshRenderer));
                child.transform.SetParent(root.transform);
                var mesh = CutMeshPreparationTests.Grid();
                UnityEditor.AssetDatabase.CreateAsset(mesh, folder + "/mesh.asset");
                child.GetComponent<MeshFilter>().sharedMesh = mesh;
                var path = folder + "/mask.png";
                pixels.SetPixels(Enumerable.Range(0, 256).Select(i => i % 16 < 4 ? Color.white : Color.black).ToArray());
                pixels.Apply();
                System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG());
                UnityEditor.AssetDatabase.ImportAsset(path);
                var importer = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
                importer.sRGBTexture = false;
                importer.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
                var texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var settings = new GameObject("Settings"); settings.transform.SetParent(root.transform);
                var component = settings.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
                component.AutoAdjustEnabled = false;
                component.Entries.Add(new MeshiaCascadingAvatarMeshSimplifierRendererEntry(child.GetComponent<Renderer>())
                {
                    Enabled = true, TargetTriangleCount = 180,
                    ProtectedAreas = new System.Collections.Generic.List<DecimationMask>
                    {
                        new DecimationMask { Enabled = true, MaterialSlot = 0, Texture = texture, SourceMesh = mesh }
                    }
                });
                var before = DecimationMasks.Resolve(child.GetComponent<Renderer>(), mesh);
                Assert.Greater(before.Count, 0);
                Assert.Less(before.Count, mesh.vertexCount);
                var prefabPath = folder + "/test.prefab";
                Assert.IsNotNull(UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefabPath));
                Object.DestroyImmediate(root);
                UnityEditor.AssetDatabase.ImportAsset(prefabPath, UnityEditor.ImportAssetOptions.ForceUpdate);
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                var reloadedRoot = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, scene);
                var restored = reloadedRoot.GetComponentInChildren<MeshiaCascadingAvatarMeshSimplifier>();
                var entry = restored.Entries.Single();
                Assert.IsFalse(restored.AutoAdjustEnabled);
                Assert.AreEqual(180, entry.TargetTriangleCount);
                var mask = entry.ProtectedAreas.Single();
                Assert.IsTrue(mask.Enabled);
                Assert.AreEqual(0, mask.MaterialSlot);
                Assert.AreEqual(path, UnityEditor.AssetDatabase.GetAssetPath(mask.Texture));
                Assert.AreSame(mesh, mask.SourceMesh);
                var renderer = entry.RendererObjectReference.Get(restored).GetComponent<Renderer>();
                Assert.NotNull(renderer);
                Assert.AreEqual(before, DecimationMasks.Resolve(renderer, mesh));

                pixels.SetPixels(Enumerable.Repeat(Color.black, 256).ToArray()); pixels.Apply();
                System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG());
                UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);
                Assert.AreEqual(0, DecimationMasks.Resolve(renderer, mesh).Count,
                    "Saving edited paint must invalidate the cached selection.");
            }
            finally
            {
                Object.DestroyImmediate(pixels);
                if (scene.IsValid() && scene.isLoaded) UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                UnityEditor.AssetDatabase.DeleteAsset(folder);
                DecimationMasks.Clear();
            }
        }

        public class MaskDrawerWindow : UnityEditor.EditorWindow
        {
            internal UnityEditor.SerializedObject Serialized;
            internal Vector2 TogglePosition;
            private readonly DecimationMaskDrawer drawer = new DecimationMaskDrawer();

            private void OnGUI()
            {
                if (Serialized == null) return;
                Serialized.Update();
                UnityEditor.EditorGUIUtility.labelWidth = 180;
                // SendEvent coordinates include the window's title bar.
                TogglePosition = GUIUtility.GUIToScreenPoint(new Vector2(198, 19)) - position.position;
                var property = Serialized.FindProperty("ProtectedAreas").GetArrayElementAtIndex(0);
                // Like the UI Toolkit IMGUIContainer, the host does not apply edits.
                drawer.OnGUI(new Rect(10, 10, 440, 160), property, GUIContent.none);
            }
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator ShouldPersistMaskCheckboxClicksAndUndo()
        {
            var go = new GameObject("Mask checkbox test", typeof(MeshRenderer), typeof(MeshiaMeshSimplifier));
            var component = go.GetComponent<MeshiaMeshSimplifier>();
            component.enabled = false; // This UI-only fixture has no mesh to preview.
            component.ProtectedAreas.Add(new DecimationMask { Enabled = false });
            var window = ScriptableObject.CreateInstance<MaskDrawerWindow>();
            var serialized = new UnityEditor.SerializedObject(component);
            try
            {
                window.Serialized = serialized;
                window.position = new Rect(50, 50, 460, 200);
                window.Show();
                for (var i = 0; i < 5; i++) yield return null;
                window.SendEvent(new Event { type = EventType.Repaint });
                Assert.AreNotEqual(Vector2.zero, window.TogglePosition, "The test window must draw before sending clicks.");
                foreach (var expected in new[] { true, false })
                {
                    UnityEditor.Undo.IncrementCurrentGroup();
                    window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = window.TogglePosition });
                    window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = window.TogglePosition });
                    for (var i = 0; i < 3; i++) yield return null;
                    Assert.AreEqual(expected, component.ProtectedAreas[0].Enabled, "Checkbox changes must survive the next serialized refresh.");
                }
                UnityEditor.Undo.FlushUndoRecordObjects();
                UnityEditor.Undo.PerformUndo();
                for (var i = 0; i < 3; i++) yield return null;
                Assert.IsTrue(component.ProtectedAreas[0].Enabled, "Undo must restore enabled protection.");
            }
            finally
            {
                window.Serialized = null;
                window.Close();
                serialized.Dispose();
                UnityEditor.Undo.ClearUndo(component);
                Object.DestroyImmediate(go);
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ShouldUseTheSameMaskInNdmfBuildAndDirectReduction(bool cascading, bool inactive)
        {
            var root = new GameObject("Masked avatar", typeof(nadena.dev.ndmf.runtime.components.NDMFAvatarRoot));
            var child = new GameObject("Masked mesh", typeof(MeshFilter), typeof(MeshRenderer));
            child.transform.SetParent(root.transform);
            var source = CutMeshPreparationTests.Grid();
            child.GetComponent<MeshFilter>().sharedMesh = source;
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false, true);
            var expected = new Mesh();
            Mesh output = null;
            try
            {
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.SetPixels(Enumerable.Range(0, 256).Select(i => i % 16 < 4 ? Color.white : Color.black).ToArray());
                texture.Apply();
                var mask = new DecimationMask { Texture = texture, SourceMesh = source };
                var options = MeshSimplifierOptions.Default.WithoutProtections();
                var target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 180 };
                if (cascading)
                {
                    var settings = new GameObject("Settings"); settings.transform.SetParent(root.transform);
                    var component = settings.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
                    component.Entries.Add(new MeshiaCascadingAvatarMeshSimplifierRendererEntry(child.GetComponent<Renderer>())
                    {
                        Enabled = true, TargetTriangleCount = 180, Options = options,
                        ProtectedAreas = new System.Collections.Generic.List<DecimationMask> { mask }
                    });
                }
                else
                {
                    var component = child.AddComponent<MeshiaMeshSimplifier>();
                    component.target = target; component.options = options; component.ProtectedAreas.Add(mask);
                }
                var protection = DecimationMasks.Select(source, new[] { mask });
                root.SetActive(!inactive);
                MeshSimplifier.SimplifyWithReport(source, target, options, null, expected, protection);
                if (expected.triangles.Length / 3 > 181)
                    UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("retained.*triangles"));
                var context = new nadena.dev.ndmf.BuildContext(root, null, false);
                typeof(NdmfPlugin).GetMethod("Simplify", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                    .Invoke(null, new object[] { context });
                output = child.GetComponent<MeshFilter>().sharedMesh;
                CollectionAssert.AreEqual(expected.vertices, output.vertices);
                CollectionAssert.AreEqual(expected.triangles, output.triangles);
                CollectionAssert.AreEqual(expected.uv, output.uv);
                Assert.Less(output.triangles.Length, source.triangles.Length);
                Assert.AreEqual(source.blendShapeCount, output.blendShapeCount);
            }
            finally
            {
                Object.DestroyImmediate(root); Object.DestroyImmediate(texture); Object.DestroyImmediate(expected);
                if (output != null && output != source) Object.DestroyImmediate(output);
                Object.DestroyImmediate(source); DecimationMasks.Clear();
            }
        }

        [TestCase(TextureWrapMode.Clamp, 0f)]
        [TestCase(TextureWrapMode.Repeat, 1f)]
        [TestCase(TextureWrapMode.Mirror, 1f)]
        public void ShouldProtectPaintInsideTriangleWithUnpaintedCorners(TextureWrapMode wrap, float offset)
        {
            var mesh = new Mesh{vertices = new[]{Vector3.zero, Vector3.right, Vector3.up}, triangles = new[]{0, 1, 2}, uv = new[]{new Vector2(.05f + offset, .05f), new Vector2(.95f + offset, .05f), new Vector2(.5f + offset, .95f)}};
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false, true);
            texture.wrapMode = wrap;
            try
            {
                texture.SetPixels(Enumerable.Repeat(Color.black, 256).ToArray());
                texture.SetPixel(8, 8, Color.white);
                texture.Apply();
                var selection = DecimationMasks.Select(mesh, new[]{new DecimationMask{Texture = texture}});
                Assert.AreEqual(3, selection.Count);
                texture.SetPixel(8, 8, Color.black);
                texture.Apply();
                Assert.AreEqual(0, DecimationMasks.Select(mesh, new[]{new DecimationMask{Texture = texture}}).Count);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void ShouldResolveSiblingSettingsAndInvalidatePaintedTexture()
        {
            var root = new GameObject("Avatar", typeof(nadena.dev.ndmf.runtime.components.NDMFAvatarRoot));
            var rendererObject = new GameObject("Mesh", typeof(MeshRenderer), typeof(MeshFilter));
            rendererObject.transform.SetParent(root.transform);
            var settings = new GameObject("Settings");
            settings.transform.SetParent(root.transform);
            var source = CutMeshPreparationTests.Grid();
            var texture = new Texture2D(1, 1);
            try
            {
                rendererObject.GetComponent<MeshFilter>().sharedMesh = source;
                var component = settings.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
                component.RefreshEntries();
                component.Entries[0].Enabled = true;
                component.Entries[0].ProtectedAreas.Add(new DecimationMask{Texture = texture});
                texture.SetPixel(0, 0, Color.black);
                texture.Apply();
                var renderer = rendererObject.GetComponent<Renderer>();
                Assert.AreEqual(0, DecimationMasks.Resolve(renderer, source)!.Count);
                texture.SetPixel(0, 0, Color.white);
                texture.Apply();
                Assert.AreEqual(source.vertexCount, DecimationMasks.Resolve(renderer, source)!.Count);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(texture);
                DecimationMasks.Clear();
            }
        }

        [Test]
        public void ShouldKeepMeshWhenUpstreamUvLayoutChanged()
        {
            var go = new GameObject("Changed UV test", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshiaMeshSimplifier));
            var source = CutMeshPreparationTests.Grid();
            var prepared = Object.Instantiate(source);
            var texture = new Texture2D(1, 1);
            try
            {
                go.GetComponent<MeshFilter>().sharedMesh = source;
                texture.SetPixel(0, 0, Color.black); texture.Apply();
                prepared.uv = source.uv.Select(uv => uv * .5f).ToArray();
                go.GetComponent<MeshiaMeshSimplifier>().ProtectedAreas.Add(new DecimationMask { Texture = texture, SourceMesh = source });
                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("kept unchanged.*changed this mesh"));
                Assert.AreEqual(prepared.vertexCount, DecimationMasks.Resolve(go.GetComponent<Renderer>(), prepared).Count);
            }
            finally
            {
                Object.DestroyImmediate(go); Object.DestroyImmediate(source); Object.DestroyImmediate(prepared);
                Object.DestroyImmediate(texture); DecimationMasks.Clear();
            }
        }

        [Test]
        public void ShouldMatchReadableAndUnreadableMaskThresholds()
        {
            var source = CutMeshPreparationTests.Grid();
            var readable = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            var unreadable = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            try
            {
                readable.SetPixel(0, 0, new Color(.3f, .3f, .3f, 1));
                readable.Apply();
                unreadable.SetPixel(0, 0, new Color(.3f, .3f, .3f, 1));
                unreadable.Apply(false, true);
                Assert.AreEqual(DecimationMasks.Select(source, new[]{new DecimationMask{Texture = readable}}).Count, DecimationMasks.Select(source, new[]{new DecimationMask{Texture = unreadable}}).Count);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(readable);
                Object.DestroyImmediate(unreadable);
            }
        }

        [Test]
        public void ShouldRespectMaterialSlotAndKeepSeamDuplicates()
        {
            var mesh = new Mesh{vertices = new[]{Vector3.zero, Vector3.right, Vector3.up, Vector3.zero, Vector3.left, Vector3.down}, uv = new[]{Vector2.zero, Vector2.right, Vector2.up, Vector2.zero, Vector2.right, Vector2.up}, subMeshCount = 2};
            mesh.SetTriangles(new[]{0, 1, 2}, 0);
            mesh.SetTriangles(new[]{3, 4, 5}, 1);
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            try
            {
                var selected = DecimationMasks.Select(mesh, new[]{new DecimationMask{Texture = texture, MaterialSlot = 0}});
                Assert.AreEqual(4, selected.Count);
                Assert.IsTrue(selected[3]);
                Assert.IsFalse(selected[4]);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void ShouldFailClosedForMissingMaskAndChangedSourceLayout()
        {
            var go = new GameObject("Mask test", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshiaMeshSimplifier));
            var source = CutMeshPreparationTests.Grid();
            var texture = new Texture2D(1, 1);
            try
            {
                go.GetComponent<MeshFilter>().sharedMesh = source;
                var component = go.GetComponent<MeshiaMeshSimplifier>();
                component.ProtectedAreas.Add(new DecimationMask());
                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("kept unchanged.*Assign a mask"));
                Assert.AreEqual(source.vertexCount, DecimationMasks.Resolve(go.GetComponent<Renderer>(), source)!.Count);
                texture.SetPixel(0, 0, Color.black);
                texture.Apply();
                component.ProtectedAreas[0].Texture = texture;
                Assert.AreEqual(0, DecimationMasks.Resolve(go.GetComponent<Renderer>(), source)!.Count);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(texture);
                DecimationMasks.Clear();
            }
        }
    }
}
