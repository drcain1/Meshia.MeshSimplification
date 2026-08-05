#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Meshia.MeshSimplification.Editor
{
    /// <summary>
    /// Displays mesh UV channels and compares source and simplified topology.
    /// </summary>
    public sealed class MeshUvPreviewWindow : EditorWindow
    {
        enum ViewMode
        {
            Original,
            Simplified,
            Overlay,
            SideBySide,
        }

        [SerializeField] Mesh? originalMesh;
        [SerializeField] Mesh? simplifiedMesh;
        [SerializeField] int uvChannel;
        [SerializeField] ViewMode viewMode = ViewMode.Overlay;
        [SerializeField] float zoom = 0.9f;
        [SerializeField] Vector2 pan;

        Mesh? ownedSimplifiedMesh;
        readonly List<Vector4> uvs = new();

        [MenuItem("Window/Meshia/UV Preview")]
        static void OpenFromSelection()
        {
            var window = GetWindow<MeshUvPreviewWindow>("Meshia UV Preview");
            window.ReleaseOwnedMesh();
            window.originalMesh = GetSelectedMesh();
            window.simplifiedMesh = null;
            window.viewMode = ViewMode.Original;
            window.Show();
        }

        /// <summary>
        /// Opens the preview with an original mesh and an owned temporary simplified mesh.
        /// </summary>
        /// <param name="original">The source mesh.</param>
        /// <param name="simplified">The temporary simplified mesh, destroyed when the window releases it.</param>
        public static void ShowComparison(Mesh original, Mesh simplified)
        {
            var window = GetWindow<MeshUvPreviewWindow>("Meshia UV Preview");
            window.ReleaseOwnedMesh();
            window.originalMesh = original;
            window.simplifiedMesh = simplified;
            window.ownedSimplifiedMesh = simplified;
            simplified.hideFlags = HideFlags.HideAndDontSave;
            window.viewMode = ViewMode.Overlay;
            window.zoom = 0.9f;
            window.pan = Vector2.zero;
            window.Show();
            window.Repaint();
        }

        void OnDisable()
        {
            ReleaseOwnedMesh();
        }

        void ReleaseOwnedMesh()
        {
            if (ownedSimplifiedMesh != null)
            {
                DestroyImmediate(ownedSimplifiedMesh);
                if (simplifiedMesh == ownedSimplifiedMesh)
                {
                    simplifiedMesh = null;
                }
                ownedSimplifiedMesh = null;
            }
        }

        void OnGUI()
        {
            DrawToolbar();
            DrawStats();

            var canvas = GUILayoutUtility.GetRect(1f, 100000f, 1f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(canvas, new Color(0.075f, 0.075f, 0.075f));
            HandleNavigation(canvas);

            switch (viewMode)
            {
                case ViewMode.Original:
                    DrawUvCanvas(canvas, originalMesh, new Color(0.2f, 0.8f, 1f), "Original");
                    break;
                case ViewMode.Simplified:
                    DrawUvCanvas(canvas, simplifiedMesh, new Color(1f, 0.55f, 0.15f), "Simplified");
                    break;
                case ViewMode.Overlay:
                    DrawGrid(canvas, "Original + Simplified");
                    DrawMeshUvs(canvas, originalMesh, new Color(0.2f, 0.8f, 1f, 0.65f));
                    DrawMeshUvs(canvas, simplifiedMesh, new Color(1f, 0.55f, 0.15f, 0.9f));
                    break;
                case ViewMode.SideBySide:
                    var gap = 8f;
                    var width = (canvas.width - gap) * 0.5f;
                    DrawUvCanvas(new Rect(canvas.x, canvas.y, width, canvas.height), originalMesh, new Color(0.2f, 0.8f, 1f), "Original");
                    DrawUvCanvas(new Rect(canvas.x + width + gap, canvas.y, width, canvas.height), simplifiedMesh, new Color(1f, 0.55f, 0.15f), "Simplified");
                    break;
            }
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var newOriginal = (Mesh?)EditorGUILayout.ObjectField(originalMesh, typeof(Mesh), false, GUILayout.MinWidth(120));
                if (newOriginal != originalMesh)
                {
                    originalMesh = newOriginal;
                }

                var newSimplified = (Mesh?)EditorGUILayout.ObjectField(simplifiedMesh, typeof(Mesh), false, GUILayout.MinWidth(120));
                if (newSimplified != simplifiedMesh)
                {
                    ReleaseOwnedMesh();
                    simplifiedMesh = newSimplified;
                }

                uvChannel = EditorGUILayout.IntPopup(uvChannel, new[] { "UV0", "UV1", "UV2", "UV3", "UV4", "UV5", "UV6", "UV7" }, new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, GUILayout.Width(65));
                viewMode = (ViewMode)EditorGUILayout.EnumPopup(viewMode, EditorStyles.toolbarPopup, GUILayout.Width(95));
                if (GUILayout.Button("Reset View", EditorStyles.toolbarButton, GUILayout.Width(75)))
                {
                    zoom = 0.9f;
                    pan = Vector2.zero;
                }
            }
        }

        void DrawStats()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(FormatStats("Original", originalMesh), GUILayout.ExpandWidth(true));
                GUILayout.Label(FormatStats("Simplified", simplifiedMesh), GUILayout.ExpandWidth(true));
                GUILayout.Label("Cyan: original   Orange: simplified", GUILayout.ExpandWidth(false));
            }
        }

        static string FormatStats(string label, Mesh? mesh)
        {
            return mesh == null ? $"{label}: none" : $"{label}: {mesh.vertexCount:N0} verts, {GetTriangleCount(mesh):N0} tris";
        }

        static long GetTriangleCount(Mesh mesh)
        {
            long count = 0;
            for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (mesh.GetTopology(subMesh) == MeshTopology.Triangles)
                {
                    count += (long)mesh.GetIndexCount(subMesh) / 3;
                }
            }
            return count;
        }

        void HandleNavigation(Rect canvas)
        {
            var current = Event.current;
            if (!canvas.Contains(current.mousePosition))
            {
                return;
            }

            if (current.type == EventType.ScrollWheel)
            {
                zoom = Mathf.Clamp(zoom * Mathf.Pow(1.1f, -current.delta.y), 0.05f, 50f);
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseDrag && (current.button == 1 || current.button == 2))
            {
                pan += current.delta;
                current.Use();
                Repaint();
            }
        }

        void DrawUvCanvas(Rect rect, Mesh? mesh, Color color, string label)
        {
            DrawGrid(rect, label);
            DrawMeshUvs(rect, mesh, color);
        }

        void DrawGrid(Rect rect, string label)
        {
            GUI.BeginClip(rect);
            var local = new Rect(0, 0, rect.width, rect.height);
            EditorGUI.DrawRect(local, new Color(0.075f, 0.075f, 0.075f));
            Handles.BeginGUI();
            Handles.color = new Color(1f, 1f, 1f, 0.08f);
            for (var i = 0; i <= 10; i++)
            {
                var t = i / 10f;
                Handles.DrawLine(ToScreen(local, new Vector2(t, 0)), ToScreen(local, new Vector2(t, 1)));
                Handles.DrawLine(ToScreen(local, new Vector2(0, t)), ToScreen(local, new Vector2(1, t)));
            }
            Handles.color = new Color(1f, 1f, 1f, 0.35f);
            Handles.DrawAAPolyLine(2f, ToScreen(local, Vector2.zero), ToScreen(local, Vector2.right), ToScreen(local, Vector2.one), ToScreen(local, Vector2.up), ToScreen(local, Vector2.zero));
            Handles.EndGUI();
            GUI.Label(new Rect(8, 6, local.width - 16, 20), label, EditorStyles.boldLabel);
            GUI.EndClip();
        }

        void DrawMeshUvs(Rect rect, Mesh? mesh, Color color)
        {
            if (mesh == null)
            {
                return;
            }

            uvs.Clear();
            mesh.GetUVs(uvChannel, uvs);
            if (uvs.Count == 0)
            {
                GUI.Label(new Rect(rect.x + 8, rect.y + 28, rect.width - 16, 20), $"No UV{uvChannel} data", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            GUI.BeginClip(rect);
            var local = new Rect(0, 0, rect.width, rect.height);
            Handles.BeginGUI();
            Handles.color = color;
            for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (mesh.GetTopology(subMesh) != MeshTopology.Triangles)
                {
                    continue;
                }

                var indices = mesh.GetIndices(subMesh);
                for (var i = 0; i + 2 < indices.Length; i += 3)
                {
                    var a = indices[i];
                    var b = indices[i + 1];
                    var c = indices[i + 2];
                    if ((uint)a >= (uint)uvs.Count || (uint)b >= (uint)uvs.Count || (uint)c >= (uint)uvs.Count)
                    {
                        continue;
                    }
                    Handles.DrawAAPolyLine(1.25f, ToScreen(local, uvs[a]), ToScreen(local, uvs[b]), ToScreen(local, uvs[c]), ToScreen(local, uvs[a]));
                }
            }
            Handles.EndGUI();
            GUI.EndClip();
        }

        Vector3 ToScreen(Rect rect, Vector2 uv)
        {
            var size = Mathf.Min(rect.width, rect.height) * zoom;
            var center = rect.center + pan;
            return new Vector3(center.x + (uv.x - 0.5f) * size, center.y - (uv.y - 0.5f) * size);
        }

        static Mesh? GetSelectedMesh()
        {
            if (Selection.activeObject is Mesh mesh)
            {
                return mesh;
            }
            if (Selection.activeGameObject != null)
            {
                if (Selection.activeGameObject.TryGetComponent<MeshFilter>(out var filter))
                {
                    return filter.sharedMesh;
                }
                if (Selection.activeGameObject.TryGetComponent<SkinnedMeshRenderer>(out var skinned))
                {
                    return skinned.sharedMesh;
                }
            }
            return null;
        }
    }
}
