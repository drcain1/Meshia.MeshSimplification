#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Meshia.MeshSimplification.Editor;

namespace Meshia.MeshSimplification.Ndmf.Editor
{
    [CustomEditor(typeof(MeshiaMeshSimplifier))]
    [CanEditMultipleObjects]
    public class MeshiaMeshSimplifierEditor : UnityEditor.Editor
    {
        [SerializeField]
        VisualTreeAsset visualTreeAsset = null!;
        
        public override VisualElement CreateInspectorGUI()
        {
            visualTreeAsset ??= AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                AssetDatabase.GUIDToAssetPath("8132ade07e7e2b14dba6ea4ee4ef0867"));
            if (visualTreeAsset == null)
            {
                return new HelpBox("Meshia inspector UI asset could not be loaded.", HelpBoxMessageType.Error);
            }

            VisualElement root = new();
            visualTreeAsset.CloneTree(root);
            root.Bind(serializedObject);

            var ndmfNotImportedWarning = root.Q<HelpBox>("NdmfNotImportedWarning");
            DisplayStyle warningDisplayStyle;
#if ENABLE_NDMF
            warningDisplayStyle = DisplayStyle.None;
#else
            warningDisplayStyle = DisplayStyle.Flex;
#endif
            ndmfNotImportedWarning.style.display = warningDisplayStyle;

            var bakeMeshButtonContainer = root.Q<IMGUIContainer>("BakeMeshButtonContainer");
            var previewUvsButton = root.Q<Button>("PreviewUvsButton");
            previewUvsButton.SetEnabled(targets.Length == 1 && TryGetTargetMesh((MeshiaMeshSimplifier)target, out _));
            previewUvsButton.clicked += () =>
            {
                if (targets.Length != 1)
                {
                    return;
                }

                var component = (MeshiaMeshSimplifier)target;
                if (!TryGetTargetMesh(component, out var sourceMesh))
                {
                    return;
                }

                serializedObject.ApplyModifiedProperties();
                var simplifiedMesh = new Mesh { name = $"{sourceMesh.name}-UV-Preview" };
                try
                {
                    MeshSimplifier.Simplify(sourceMesh, component.target, component.options, simplifiedMesh);
                    MeshUvPreviewWindow.ShowComparison(sourceMesh, simplifiedMesh);
                }
                catch
                {
                    DestroyImmediate(simplifiedMesh);
                    throw;
                }
            };
            bakeMeshButtonContainer.onGUIHandler = () =>
            {
                // TODO: Replace this with non-IMGUI implementation
                // But how could we register callback for whether target mesh is currently available?
                if (targets.Length == 1)
                {
                    var ndmfMeshSimplifier = (MeshiaMeshSimplifier)target;
                    if (TryGetTargetMesh(ndmfMeshSimplifier, out var targetMesh))
                    {
                        if (GUILayout.Button("Bake mesh"))
                        {
                            var absolutePath = EditorUtility.SaveFilePanel(
                                        title: "Save baked mesh",
                                        directory: "",
                                        defaultName: $"{targetMesh.name}-Simplified.asset",
                                        extension: "asset");

                            if (!string.IsNullOrEmpty(absolutePath))
                            {
                                Mesh simplifiedMesh = new();

                                MeshSimplifier.Simplify(targetMesh, ndmfMeshSimplifier.target, ndmfMeshSimplifier.options, simplifiedMesh);

                                AssetDatabase.CreateAsset(simplifiedMesh, Path.Join("Assets/", Path.GetRelativePath(Application.dataPath, absolutePath)));
                            }
                        }
                    }

                }
            };
            
            return root;
        }

        private static bool TryGetTargetMesh(MeshiaMeshSimplifier ndmfMeshSimplifier, [NotNullWhen(true)] out Mesh? targetMesh)
        {
            targetMesh = null;
            if (ndmfMeshSimplifier.TryGetComponent<MeshFilter>(out var meshFilter))
            {
                targetMesh = meshFilter.sharedMesh;
                if (targetMesh != null) 
                {
                    return true;
                }
            }
            if (ndmfMeshSimplifier.TryGetComponent<SkinnedMeshRenderer>(out var skinnedMeshRenderer))
            {
                targetMesh = skinnedMeshRenderer.sharedMesh; 
                if (targetMesh != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
