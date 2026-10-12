#nullable enable
using System;
using System.Linq;
using System.Reflection;
using static Meshia.MeshSimplification.Editor.Localization.LocalizationProvider;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Meshia.MeshSimplification.Ndmf.Editor
{
    [CustomPropertyDrawer(typeof(DecimationMask))]
    internal sealed class DecimationMaskDrawer : PropertyDrawer
    {
        private const float Row = 21;
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => Row * 7 + 6;
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => new IMGUIContainer(() =>
        {
            try
            {
                if (property.serializedObject.targetObject == null)
                    return;
                OnGUI(EditorGUILayout.GetControlRect(false, GetPropertyHeight(property, GUIContent.none)), property, GUIContent.none);
            }
            catch (ObjectDisposedException)
            {
            } // The virtualized row was removed during this repaint.
        });
        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(rect, label, property);
            var enabled = property.FindPropertyRelative("Enabled");
            var slot = property.FindPropertyRelative("MaterialSlot");
            var texture = property.FindPropertyRelative("Texture");
            var renderer = FindRenderer(property);
            var line = new Rect(rect.x, rect.y, rect.width, Row - 2);
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(line, enabled, new GUIContent(Tr("Protect painted area")));
            if (EditorGUI.EndChangeCheck())
            {
                // IMGUIContainer does not apply SerializedProperty edits for us.
                // Commit the toggle independently of the texture/slot fields below.
                property.serializedObject.ApplyModifiedProperties();
                DecimationMasks.Clear();
            }
            line.y += Row;
            EditorGUI.BeginChangeCheck();
            if (renderer!= null && renderer.sharedMaterials.Length > 0)
            {
                var names = renderer.sharedMaterials.Select((m, i) => $"{i}: {(m == null ? "Missing material" : m.name)}").ToArray();
                slot.intValue = EditorGUI.Popup(line, Tr("Material slot"), slot.intValue, names);
            }
            else
                EditorGUI.PropertyField(line, slot, new GUIContent(Tr("Material slot")));
            line.y += Row;
            EditorGUI.PropertyField(line, texture, new GUIContent(Tr("Mask texture")));
            line.y += Row;
            if (EditorGUI.EndChangeCheck())
            {
                property.FindPropertyRelative("SourceMesh").objectReferenceValue = renderer == null ? null : RendererUtility.GetMesh(renderer);
                property.serializedObject.ApplyModifiedProperties();
                DecimationMasks.Clear();
            }

            var painter = Type.GetType("net.nekobako.MaskTextureEditor.Editor.Window, net.nekobako.MaskTextureEditor.Editor");
            using (new EditorGUI.DisabledScope(renderer == null || property.serializedObject.isEditingMultipleObjects))
            {
                var third = (line.width - 8) / 3;
                using (new EditorGUI.DisabledScope(painter == null))
                {
                    if (GUI.Button(new Rect(line.x, line.y, third, line.height), Tr("Create")))
                    {
                        var utility = Type.GetType("net.nekobako.MaskTextureEditor.Editor.Utility, net.nekobako.MaskTextureEditor.Editor");
                        var created = utility?.GetMethod("CreateTexture", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[]{new Vector2Int(1024, 1024), Color.black}) as Texture2D;
                        if (created != null)
                        {
                            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(created)) as TextureImporter;
                            if (importer != null)
                            {
                                importer.sRGBTexture = false;
                                importer.mipmapEnabled = false;
                                importer.textureCompression = TextureImporterCompression.Uncompressed;
                                importer.wrapMode = TextureWrapMode.Clamp;
                                importer.SaveAndReimport();
                            }

                            texture.objectReferenceValue = created;
                            enabled.boolValue = true;
                            property.FindPropertyRelative("SourceMesh").objectReferenceValue = RendererUtility.GetMesh(renderer!);
                            property.serializedObject.ApplyModifiedProperties();
                            DecimationMasks.Clear();
                            Open(painter!, created, renderer!, slot.intValue);
                        }
                    }

                    using (new EditorGUI.DisabledScope(texture.objectReferenceValue == null))
                        if (GUI.Button(new Rect(line.x + third + 4, line.y, third, line.height), Tr("Edit")))
                            Open(painter!, (Texture2D)texture.objectReferenceValue, renderer!, slot.intValue);
                }

                if (GUI.Button(new Rect(line.x + 2 * (third + 4), line.y, third, line.height), Tr("Show area")))
                    DecimationMaskOverlay.Toggle(renderer!);
            }

            line.y += Row;
            EditorGUI.LabelField(line, Tr("White: keep unchanged. Black: allow reduction."));
            line.y += Row;
            var message = enabled.boolValue && texture.objectReferenceValue == null ? "Assign a mask or remove this entry. This mesh is kept unchanged." : painter == null ? "Install MaskTextureEditor for painting, or assign a texture." : "Save the mask in the painter to update Meshia.";
            EditorGUI.LabelField(line, new GUIContent(Tr(message), Tr(message)), EditorStyles.miniLabel);
            line.y += Row;
            EditorGUI.LabelField(line, new GUIContent(Tr("FA-QEM only. Other algorithms keep this mesh unchanged.")), EditorStyles.miniLabel);
            EditorGUI.EndProperty();
        }

        private static void Open(Type painter, Texture2D texture, Renderer renderer, int slot)
        {
            var method = painter.GetMethod("TryOpen", new[]{typeof(Texture2D), typeof(Renderer), typeof(int? ), typeof(string)});
            if (method == null)
                throw new InvalidOperationException("This MaskTextureEditor version does not expose the supported painting API.");
            method.Invoke(null, new object? []{texture, renderer, (int? )slot, "meshia.protected-areas"});
        }

        private static Renderer? FindRenderer(SerializedProperty property)
        {
            if (property.serializedObject.targetObject is MeshiaMeshSimplifier single)
                return single.GetComponent<Renderer>();
#if ENABLE_MODULAR_AVATAR
            if(property.serializedObject.targetObject is MeshiaCascadingAvatarMeshSimplifier cascade)
            {
                var match=System.Text.RegularExpressions.Regex.Match(property.propertyPath,@"Entries\.Array\.data\[(\d+)\]");
                if(match.Success && int.TryParse(match.Groups[1].Value,out var index) && index<cascade.Entries.Count)
                    return cascade.Entries[index].GetTargetRenderer(cascade);
            }
#endif
            return null;
        }
    }

    [InitializeOnLoad]
    internal static class DecimationMaskOverlay
    {
        private static Renderer? renderer;
        static DecimationMaskOverlay()
        {
            SceneView.duringSceneGui += Draw;
        }

        internal static void Toggle(Renderer target)
        {
            renderer = renderer == target ? null : target;
            SceneView.RepaintAll();
        }

        private static void Draw(SceneView view)
        {
            if (renderer == null || Event.current.type != EventType.Repaint)
                return;
            var source = RendererUtility.GetMesh(renderer);
            if (source == null)
                return;
            var selection = DecimationMasks.Resolve(renderer, source);
            if (selection == null)
                return;
            Mesh? baked = null;
            var previousColor = Handles.color;
            var previousMatrix = Handles.matrix;
            try
            {
                var mesh = source;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    baked = new Mesh();
                    skin.BakeMesh(baked);
                    mesh = baked;
                }

                var vertices = mesh.vertices;
                var triangles = source.triangles;
                Handles.matrix = renderer.localToWorldMatrix;
                Handles.color = new Color(1f, .25f, .6f, .35f);
                var points = new Vector3[3];
                for (var i = 0; i < triangles.Length; i += 3)
                {
                    var a = triangles[i];
                    var b = triangles[i + 1];
                    var c = triangles[i + 2];
                    if (!selection[a] || !selection[b] || !selection[c])
                        continue;
                    points[0] = vertices[a];
                    points[1] = vertices[b];
                    points[2] = vertices[c];
                    Handles.DrawAAConvexPolygon(points);
                }
            }
            finally
            {
                Handles.color = previousColor;
                Handles.matrix = previousMatrix;
                if (baked != null)
                    UnityEngine.Object.DestroyImmediate(baked);
            }
        }
    }
}
