#nullable enable
#if ENABLE_MODULAR_AVATAR

using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Meshia.MeshSimplification.Editor;
using Meshia.MeshSimplification.Editor.Localization;
using static Meshia.MeshSimplification.Editor.Localization.LocalizationProvider;
using Meshia.MeshSimplification.Ndmf.Editor.Preview;
using nadena.dev.ndmf;
using nadena.dev.ndmf.platform;
using nadena.dev.ndmf.preview;
using nadena.dev.ndmf.runtime;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace Meshia.MeshSimplification.Ndmf.Editor
{
    [CustomEditor(typeof(MeshiaCascadingAvatarMeshSimplifier))]
    internal class MeshiaCascadingAvatarMeshSimplifierEditor : UnityEditor.Editor
    {
        [Serializable]
        private sealed class AllocationSnapshot
        {
            public int[] Counts = Array.Empty<int>();
            public int[] Outputs = Array.Empty<int>();
            public string Settings = string.Empty;
        }

        private readonly struct BuildAnalysisResult
        {
            internal readonly int TriangleCount;
            internal readonly int EstimatedBeforeDownstreamTriangleCount;
            internal readonly int Revision;
            internal readonly string? Error;
            internal readonly AllocationSnapshot? Allocations;

            internal BuildAnalysisResult(
                int triangleCount,
                int estimatedBeforeDownstreamTriangleCount,
                int revision,
                string? error)
            {
                TriangleCount = triangleCount;
                EstimatedBeforeDownstreamTriangleCount = estimatedBeforeDownstreamTriangleCount;
                Revision = revision;
                Error = error;
                Allocations = null;
            }

            internal BuildAnalysisResult(int triangleCount, int estimate, int revision, string? error,
                AllocationSnapshot? allocations) : this(triangleCount, estimate, revision, error)
            {
                Allocations = allocations;
            }
        }

        [Serializable]
        private sealed class SerializedBuildAnalysisResult
        {
            public int TriangleCount;
            public int EstimatedBeforeDownstreamTriangleCount;
            public int Revision;
            public string? Error;
            public AllocationSnapshot? Allocations;
        }

        private const string AnalysisRevisionSessionKey =
            "Meshia.MeshSimplification.CascadingTriangleAnalysis.Revision";
        private const string AnalysisResultSessionKeyPrefix =
            "Meshia.MeshSimplification.CascadingTriangleAnalysis.Result.";

        private static readonly Dictionary<string, BuildAnalysisResult> BuildAnalysisCache = new();
        private MeasuredMeshSet? measuredMeshes;
        private MeasuredMeshSet? retainedStartingMeshes;
        private static int meshInputRevision;
        private bool estimateScheduled;
        private bool estimateRunning;
        private string failedEstimate = string.Empty;
        private static bool s_analysisInProgress;
        private static int CurrentAnalysisRevision => SessionState.GetInt(AnalysisRevisionSessionKey, 0);
        private int pendingBudgetEntry = -1;
        private int lastAnalysisReduction;
        private string lastAnalysisRunMessage = string.Empty;
        private int lastAnalysisRunRevision = -1;

        [SerializeField] VisualTreeAsset editorVisualTreeAsset = null!;
        [SerializeField] VisualTreeAsset entryEditorVisualTreeAsset = null!;
        private MeshiaCascadingAvatarMeshSimplifier Target => (MeshiaCascadingAvatarMeshSimplifier)target;

        private SerializedProperty AutoAdjustEnabledProperty => serializedObject.FindProperty(nameof(MeshiaCascadingAvatarMeshSimplifier.AutoAdjustEnabled));
        private SerializedProperty TargetTriangleCountProperty => serializedObject.FindProperty(nameof(MeshiaCascadingAvatarMeshSimplifier.TargetTriangleCount));
        private SerializedProperty EntriesProperty => serializedObject.FindProperty(nameof(MeshiaCascadingAvatarMeshSimplifier.Entries));

        [InitializeOnLoadMethod]
        private static void InitializeTriangleAnalysisInvalidation()
        {
            Undo.postprocessModifications -= OnPostprocessModifications;
            Undo.postprocessModifications += OnPostprocessModifications;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.projectChanged -= InvalidateMeshInputs;
            EditorApplication.projectChanged += InvalidateMeshInputs;
        }

        private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            if (!s_analysisInProgress && modifications.Any(m =>
                m.currentValue.target is not MeshiaCascadingAvatarMeshSimplifier ||
                !(m.currentValue.propertyPath.EndsWith("TargetTriangleCount", StringComparison.Ordinal) ||
                  m.currentValue.propertyPath == "BuildTriangleReserve" || m.currentValue.propertyPath == "AutoAdjustEnabled")))
                meshInputRevision++;
            InvalidateTriangleAnalysis();
            return modifications;
        }

        private static void InvalidateMeshInputs() { if (!s_analysisInProgress) meshInputRevision++; }
        private static void OnUndoRedo() { InvalidateMeshInputs(); InvalidateTriangleAnalysis(); }
        private void OnDisable()
        {
            measuredMeshes?.Dispose();
            if (retainedStartingMeshes != measuredMeshes) retainedStartingMeshes?.Dispose();
            measuredMeshes = retainedStartingMeshes = null;
        }
        private bool HasMeasuredInputs(string settings) => measuredMeshes != null &&
            measuredMeshes.InputRevision == meshInputRevision && measuredMeshes.Settings == settings;

        private static void InvalidateTriangleAnalysis()
        {
            if (s_analysisInProgress)
            {
                return;
            }

            SessionState.SetInt(AnalysisRevisionSessionKey, CurrentAnalysisRevision + 1);

            DownstreamTriangleEstimator.Invalidate();
        }


        [MenuItem("GameObject/Meshia Mesh Simplification/Meshia Cascading Avatar Mesh Simplifier", false, 0)]
        static void AddCascadingAvatarMeshSimplifier()
        {
            var go = new GameObject("Meshia Cascading Avatar Mesh Simplifier");
            go.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
            go.transform.parent = Selection.activeGameObject.transform;
            Undo.RegisterCreatedObjectUndo(go, Tr("Create Meshia Cascading Avatar Mesh Simplifier"));
        }
        private void OnEnable()
        {
            if (target is MeshiaCascadingAvatarMeshSimplifier)
            {
                RefreshEntries();
            }
        }

        private void RefreshEntries()
        {
            if(Target.transform.parent == null)
            {
                return;
            }
            Undo.RecordObject(Target, Tr("Get entries"));
            try
            {
                Target.RefreshEntries();
            }
            catch (InvalidOperationException e)
            {
                Debug.LogException(e, target);
                return;
            }

            serializedObject.Update();


        }

        public override VisualElement CreateInspectorGUI()
        {
            editorVisualTreeAsset ??= AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                AssetDatabase.GUIDToAssetPath("3152adf210475e149955bf3e826b403d"));
            entryEditorVisualTreeAsset ??= AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                AssetDatabase.GUIDToAssetPath("89f639b7d364db64283afa25c01d1ae3"));
            if (editorVisualTreeAsset == null || entryEditorVisualTreeAsset == null)
            {
                return new HelpBox(Tr("Meshia cascading inspector UI assets could not be loaded."), HelpBoxMessageType.Error);
            }

            VisualElement root = new();
            editorVisualTreeAsset.CloneTree(root);
            root.RegisterCallback<DetachFromPanelEvent>(_ => estimateScheduled = false);

            serializedObject.Update();

            root.Bind(serializedObject);
            var attachedToRootWarning = root.Q<HelpBox>("AttachedToRootWarning");
            var mainElement = root.Q<VisualElement>("MainElement");
            var targetTriangleCountField = root.Q<IntegerField>("TargetTriangleCountField");
            targetTriangleCountField.isDelayed = true;
            var targetTriangleCountPresetDropdownField = root.Q<DropdownField>("TargetTriangleCountPresetDropdownField");
            var adjustButton = root.Q<Button>("AdjustButton");
            var autoAdjustEnabledToggle = root.Q<Toggle>("AutoAdjustEnabledToggle");
            var analyzeNdmfBuildButton = root.Q<Button>("AnalyzeNdmfBuildButton");

            root.Q<Button>("ClearBuildReserveButton").clicked += () =>
            {
                serializedObject.Update();
                serializedObject.FindProperty(nameof(MeshiaCascadingAvatarMeshSimplifier.BuildTriangleReserve)).intValue = 0;
                serializedObject.ApplyModifiedProperties();
                lastAnalysisReduction = 0;
                InvalidateTriangleAnalysis();
                RefreshBudgetGuidance(root);
            };

            var removeInvalidEntriesButton = root.Q<Button>("RemoveInvalidEntriesButton");
            var resetButton = root.Q<Button>("ResetButton");
            var entriesListView = root.Q<ListView>("EntriesListView");
            var ndmfPreviewToggle = root.Q<Toggle>("NdmfPreviewToggle");
            root.Q<Button>("FindBudgetReductionsButton").clicked += () =>
            {
                root.Q<Foldout>("BudgetBreakdown").value = true;
                RefreshBudgetGuidance(root);
            };
            root.Q<Foldout>("BudgetBreakdown").RegisterValueChangedCallback(_ => RefreshBudgetGuidance(root));
            root.Q<Foldout>("CalculationDetails").RegisterValueChangedCallback(_ => RefreshBudgetGuidance(root));
            root.Q<Button>("AaoRemovalGuideButton").clicked += () => Application.OpenURL(
                "https://vpm.anatawa12.com/avatar-optimizer/" + (CurrentLocale == "ja" ? "ja" : "en") + "/docs/reference/remove-mesh-by-blendshape/");
            root.Q<Button>("ShapeChangerGuideButton").clicked += () => Application.OpenURL(
                "https://modular-avatar.nadena.dev/" + (CurrentLocale == "ja" ? "ja/" : "") + "docs/reference/reaction/shape-changer");
            root.schedule.Execute(() =>
            {
                if (target == null) return;
                RefreshPreviewShortfalls(root);
                RefreshBudgetGuidance(root);
            }).Every(500);

            void ApplyProtectionPreset(bool aggressive)
            {
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(Target, aggressive ? Tr("Apply Aggressive Preset") : Tr("Apply Conservative Defaults to All Meshes"));
                AvatarProtectionPreset.Apply(Target, aggressive);
                EditorUtility.SetDirty(Target);
                serializedObject.Update();
                entriesListView.Rebuild();
            }
            root.Q<Button>("ConservativeDefaultsButton").clicked += () => ApplyProtectionPreset(false);
            root.Q<Button>("AggressiveDefaultsButton").clicked += () => ApplyProtectionPreset(true);

            var allMeshesAlgorithmField = root.Q<DropdownField>("AllMeshesAlgorithmField");
            var algorithms = (MeshiaCascadingSimplificationAlgorithm[])Enum.GetValues(typeof(MeshiaCascadingSimplificationAlgorithm));
            var algorithmNames = algorithms.Select(algorithm => ObjectNames.NicifyVariableName(algorithm.ToString())).ToList();
            allMeshesAlgorithmField.choices = algorithmNames;

            void RefreshAllMeshesAlgorithm()
            {
                var entries = Target.Entries;
                allMeshesAlgorithmField.SetEnabled(entries.Count > 0);
                var algorithmIndex = entries.Count > 0 ? Array.IndexOf(algorithms, entries[0].Algorithm) : -1;
                var sameAlgorithm = algorithmIndex >= 0 && entries.All(entry => entry.Algorithm == algorithms[algorithmIndex]);
                allMeshesAlgorithmField.SetValueWithoutNotify(entries.Count == 0
                    ? "No meshes"
                    : sameAlgorithm ? algorithmNames[algorithmIndex] : "Mixed");
            }

            allMeshesAlgorithmField.RegisterValueChangedCallback(changeEvent =>
            {
                var algorithmIndex = algorithmNames.IndexOf(changeEvent.newValue);
                if (algorithmIndex < 0)
                {
                    return;
                }

                serializedObject.Update();
                Undo.SetCurrentGroupName(Tr("Change algorithm for all meshes"));
                for (var index = 0; index < EntriesProperty.arraySize; index++)
                {
                    EntriesProperty.GetArrayElementAtIndex(index)
                        .FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.Algorithm))
                        .intValue = (int)algorithms[algorithmIndex];
                }
                serializedObject.ApplyModifiedProperties();
                InvalidateTriangleAnalysis();
                entriesListView.RefreshItems();
                RefreshAllMeshesAlgorithm();
            });
            root.TrackSerializedObjectValue(serializedObject, _ => RefreshAllMeshesAlgorithm());
            RefreshAllMeshesAlgorithm();

            attachedToRootWarning.style.display = Target.transform.parent == null ? DisplayStyle.Flex : DisplayStyle.None;

            root.RegisterCallback<SerializedPropertyChangeEvent>(changeEvent =>
            {
                if (changeEvent.changedProperty.propertyPath !=
                    nameof(MeshiaCascadingAvatarMeshSimplifier.AutoAdjustEnabled))
                {
                    InvalidateTriangleAnalysis();
                }
            });


            var observedBudget = Target.TargetTriangleCount;
            targetTriangleCountField.RegisterValueChangedCallback(changeEvent =>
            {
                // Calibration at another budget is not valid here. Commit the field
                // once (Enter/focus loss), rather than redistributing while typing.
                if (changeEvent.newValue != observedBudget)
                {
                    observedBudget = changeEvent.newValue;
                    TargetTriangleCountProperty.intValue = changeEvent.newValue;
                    serializedObject.FindProperty(nameof(MeshiaCascadingAvatarMeshSimplifier.BuildTriangleReserve)).intValue = 0;
                    serializedObject.ApplyModifiedProperties();
                    failedEstimate = string.Empty;
                }
                if (!TargetTriangleCountPresetValueToName.TryGetValue(changeEvent.newValue, out var name))
                {
                    name = "Custom";
                }
                targetTriangleCountPresetDropdownField.SetValueWithoutNotify(name);
                if (AutoAdjustEnabledProperty.boolValue)
                {
                    AdjustQuality();
                    serializedObject.ApplyModifiedProperties();
                }
            });

            targetTriangleCountPresetDropdownField.choices = TargetTriangleCountPresetNameToValue.Keys.ToList();
            targetTriangleCountPresetDropdownField.RegisterValueChangedCallback(changeEvent =>
            {
                if(TargetTriangleCountPresetNameToValue.TryGetValue(changeEvent.newValue, out var value))
                {
                    TargetTriangleCountProperty.intValue = value;
                    serializedObject.ApplyModifiedProperties();
                }

            });

            adjustButton.clicked += () =>
            {
                AdjustQuality();
                serializedObject.ApplyModifiedProperties();
            };

            autoAdjustEnabledToggle.RegisterValueChangedCallback(changeEvent =>
            {
                var autoAdjustEnabled = AutoAdjustEnabledProperty.boolValue;

                if (autoAdjustEnabled)
                {
                    AdjustQuality();
                    serializedObject.ApplyModifiedProperties();
                }
            });


            analyzeNdmfBuildButton.clicked += () =>
            {
                AnalyzeNdmfBuild(analyzeNdmfBuildButton);
                RefreshPreviewShortfalls(root);
                RefreshBudgetGuidance(root);
            };
            removeInvalidEntriesButton.clicked += () =>
            {
                var target = Target;
                var entries = target.Entries;

                Undo.RecordObject(target, Tr("Remove Invalid Entries"));
                for (int i = 0; i < entries.Count;)
                {
                    var entry = entries[i];
                    if(entry.IsValid(target))
                    {
                        i++;
                    }
                    else
                    {
                        entries.RemoveAt(i);
                    }

                }
                serializedObject.Update();
            };
            resetButton.clicked += () =>
            {
                serializedObject.FindProperty(nameof(MeshiaCascadingAvatarMeshSimplifier.BuildTriangleReserve)).intValue = 0;
                lastAnalysisReduction = 0;
                var originalTriangleCount = GetTotalOriginalTriangleCount();
                var resetTargetTriangleCount = TargetTriangleCountProperty.intValue;
                var excludedTriangleCount = 0;
                foreach (var entry in Target.Entries)
                {
                    var renderer = entry.GetTargetRenderer(Target);
                    if (renderer != null && entry.IsValid(Target) &&
                        !MeshiaCascadingAvatarMeshSimplifierRendererEntry.IsEnabledByDefault(renderer) &&
                        TryGetOriginalTriangleCount(entry, false, out var triangles))
                        excludedTriangleCount += triangles;
                }
                var adjustableTriangleCount = originalTriangleCount - excludedTriangleCount;
                var quality = adjustableTriangleCount > 0
                    ? Mathf.Clamp01((resetTargetTriangleCount - excludedTriangleCount) / (float)adjustableTriangleCount)
                    : 1f;

                var entriesProperty = EntriesProperty;
                var arraySize = entriesProperty.arraySize;
                for (int i = 0; i < arraySize; i++)
                {
                    var entryProperty = entriesProperty.GetArrayElementAtIndex(i);
                    entryProperty.FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.Enabled)).boolValue =
                        MeshiaCascadingAvatarMeshSimplifierRendererEntry.IsEnabledByDefault(Target.Entries[i].GetTargetRenderer(Target));
                    entryProperty.FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.Fixed)).boolValue = false;
                }

                // SetQualityAll reads entry.Fixed; commit the reset flags first.
                serializedObject.ApplyModifiedProperties();
                SetQualityAll(quality);
                serializedObject.ApplyModifiedProperties();
            };
            entriesListView.bindItem = (itemElement, index) =>
            {
                var entry = Target.Entries[index];
                var entryProperty = EntriesProperty.GetArrayElementAtIndex(index);
                var itemRoot = (TemplateContainer)itemElement;
                var algorithmField = itemRoot.Q<DropdownField>("AlgorithmField");
                var targetObjectField = itemRoot.Q<ObjectField>("TargetObjectField");
                var targetPathField = itemRoot.Q<TextField>("TargetPathField");
                var targetTriangleCountSlider = itemRoot.Q<SliderInt>("TargetTriangleCountSlider");
                var targetTriangleCountField = itemRoot.Q<IntegerField>("TargetTriangleCountField");
                var originalTriangleCountField = itemRoot.Q<IntegerField>("OriginalTriangleCountField");
                var unknownOriginalTriangleCountField = itemRoot.Q<TextField>("UnknownOriginalTriangleCountField");
                var preserveBorderEdgesBonesFoldout = itemRoot.Q<Foldout>("PreserveBorderEdgesBonesFoldout");
                var previewUvsButton = itemRoot.Q<Button>("PreviewUvsButton");
                itemRoot.BindProperty(entryProperty);
                LocalizationProvider.BindEnum(algorithmField,
                    entryProperty.FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.Algorithm)));
                itemRoot.userData = index;
                RefreshAllocationFields(itemRoot);
                if (pendingBudgetEntry == index)
                {
                    pendingBudgetEntry = -1;
                    itemRoot.Q<Toggle>("OptionsToggle").value = true;
                }
                UpdateAlgorithmOptionAvailability(itemRoot);
                var targetRenderer = entry.GetTargetRenderer(Target);
                if (targetRenderer != null)
                {
                    targetObjectField.style.display = DisplayStyle.Flex;
                    targetObjectField.value = targetRenderer;
                    targetObjectField.EnableInClassList("editor-only", MeshiaCascadingAvatarMeshSimplifierRendererEntry.IsEditorOnlyInHierarchy(targetRenderer.gameObject));

                    targetPathField.style.display = DisplayStyle.None;
                    previewUvsButton.SetEnabled(entry.Enabled && RendererUtility.GetMesh(targetRenderer) != null);
                }
                else
                {
                    targetPathField.style.display = DisplayStyle.Flex;
                    targetPathField.value = entry.RendererObjectReference.referencePath;
                    targetObjectField.style.display = DisplayStyle.None;
                    previewUvsButton.SetEnabled(false);
                }


                if(TryGetOriginalTriangleCount(entry, true, out var originalTriangleCount))
                {
                    targetTriangleCountSlider.highValue = originalTriangleCount;

                    originalTriangleCountField.style.display = DisplayStyle.Flex;
                    originalTriangleCountField.value = originalTriangleCount;

                    unknownOriginalTriangleCountField.style.display = DisplayStyle.None;
                }
                else
                {
                    targetTriangleCountSlider.visible = false;

                    unknownOriginalTriangleCountField.style.display = DisplayStyle.Flex;


                    originalTriangleCountField.style.display = DisplayStyle.None;

                }

                RefreshJointBoneSelection(itemRoot);
                var humanBodyBoneIndex = 0;
                var preserveBorderEdgesBonesProperty = EntriesProperty.GetArrayElementAtIndex(index).FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.PreserveBorderEdgesBones));
                var preserveBorderEdgesBones = preserveBorderEdgesBonesProperty.ulongValue;
                foreach (var preserveBorderEdgesBoneToggle in preserveBorderEdgesBonesFoldout.Children().OfType<Toggle>())
                {
                    preserveBorderEdgesBoneToggle.value = (preserveBorderEdgesBones & (1ul << humanBodyBoneIndex)) != 0ul;

                    humanBodyBoneIndex++;
                }
            };


            entriesListView.makeItem = () =>
            {
                var itemRoot = entryEditorVisualTreeAsset.CloneTree();
                var enabledToggle = itemRoot.Q<Toggle>("EnabledToggle");
                var targetObjectField = itemRoot.Q<ObjectField>("TargetObjectField");
                var targetTriangleCountSlider = itemRoot.Q<SliderInt>("TargetTriangleCountSlider");
                var targetTriangleCountField = itemRoot.Q<IntegerField>("TargetTriangleCountField");
                var triangleCountDivider = itemRoot.Q<Label>("TriangleCountDivider");
                var optionsToggle = itemRoot.Q<Toggle>("OptionsToggle");
                var deformationToggle = itemRoot.Q<Toggle>("DeformationProtectionToggle");
                deformationToggle.Q("unity-checkmark").style.backgroundImage =
                    new StyleBackground((Texture2D)EditorGUIUtility.IconContent("Avatar Icon").image);
                deformationToggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target != deformationToggle || itemRoot.userData is not int itemIndex ||
                        itemIndex < 0 || itemIndex >= Target.Entries.Count) return;
                    serializedObject.Update();
                    var options = EntriesProperty.GetArrayElementAtIndex(itemIndex)
                        .FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.Options));
                    var protection = options.FindPropertyRelative(nameof(MeshSimplifierOptions.SkinningProtection));
                    // A partial state is an invitation to enable both options, not to disable them.
                    var enable = evt.newValue || deformationToggle.ClassListContains("partial-protection");
                    protection.FindPropertyRelative(nameof(SkinningProtectionOptions.Policy)).intValue =
                        (int)(enable ? SkinningProtectionPolicy.On : SkinningProtectionPolicy.Off);
                    protection.FindPropertyRelative(nameof(SkinningProtectionOptions.Enabled)).boolValue = enable;
                    protection.FindPropertyRelative(nameof(SkinningProtectionOptions.PreserveJointTransitions)).boolValue = enable;
                    serializedObject.ApplyModifiedProperties();
                    RefreshDeformationProtection(itemRoot);
                });
                var algorithmField = itemRoot.Q<DropdownField>("AlgorithmField");
                var optionsField = itemRoot.Q<PropertyField>("OptionsField");
                var preserveBorderEdgesBonesFoldout = itemRoot.Q<Foldout>("PreserveBorderEdgesBonesFoldout");
                var previewUvsButton = itemRoot.Q<Button>("PreviewUvsButton");
                HelpBox blenderOptionsHelpBox = new(
                    "Blender Decimate supports skinning protection. Meshia-specific geometry options are not used.",
                    HelpBoxMessageType.Info)
                {
                    name = "BlenderOptionsHelpBox",
                };
                blenderOptionsHelpBox.style.display = DisplayStyle.None;
                optionsField.parent.Insert(optionsField.parent.IndexOf(optionsField), blenderOptionsHelpBox);
                HelpBox uvLoopDissolveHelpBox = new(
                    "Reconstructs conservative quad loops, protects UV seams and boundaries, then uses Blender Decimate to reach the remaining target. Meshia options are not used.",
                    HelpBoxMessageType.Info)
                {
                    name = "UvLoopDissolveHelpBox",
                };
                uvLoopDissolveHelpBox.style.display = DisplayStyle.None;
                optionsField.parent.Insert(optionsField.parent.IndexOf(optionsField), uvLoopDissolveHelpBox);
                HelpBox faQemHelpBox = new(
                    "FA-QEM simplifies geometry while retaining the existing UV layout, materials, and textures.",
                    HelpBoxMessageType.Info)
                {
                    name = "FaQemHelpBox",
                };
                faQemHelpBox.style.display = DisplayStyle.None;
                optionsField.parent.Insert(optionsField.parent.IndexOf(optionsField), faQemHelpBox);
                enabledToggle.RegisterValueChangedCallback(changeEvent =>
                {
                    var enabled = changeEvent.newValue;

                    targetTriangleCountSlider.visible = enabled;
                    targetTriangleCountField.visible = enabled;
                    triangleCountDivider.visible = enabled;
                    var canPreviewUvs = enabled && itemRoot.userData is int itemIndex &&
                        itemIndex >= 0 && itemIndex < Target.Entries.Count &&
                        Target.Entries[itemIndex].GetTargetRenderer(Target) is { } renderer &&
                        RendererUtility.GetMesh(renderer) != null;
                    previewUvsButton.SetEnabled(canPreviewUvs);


                    if (AutoAdjustEnabledProperty.boolValue)
                    {
                        AdjustQuality();
                        serializedObject.ApplyModifiedProperties();
                    }
                });

                targetObjectField.SetEnabled(false);

                void ChangeAllocation(int value)
                {
                    if (itemRoot.userData is not int itemIndex || itemIndex < 0 ||
                        itemIndex >= Target.Entries.Count) return;
                    SetManualAllocation(itemIndex, value);
                    root.Query<TemplateContainer>().ForEach(RefreshAllocationFields);
                    RefreshBudgetGuidance(root);
                }
                targetTriangleCountSlider.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target == targetTriangleCountSlider) ChangeAllocation(evt.newValue);
                });
                targetTriangleCountField.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target == targetTriangleCountField) ChangeAllocation(evt.newValue);
                });

                optionsToggle.RegisterValueChangedCallback(changeEvent =>
                {
                    algorithmField.style.display = previewUvsButton.style.display = optionsField.style.display = preserveBorderEdgesBonesFoldout.style.display =
                        changeEvent.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                    UpdateAlgorithmOptionAvailability(itemRoot);
                });

                previewUvsButton.clicked += () => PreviewUvs(itemRoot);

                algorithmField.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target != algorithmField) return;
                    itemRoot.schedule.Execute(() => UpdateAlgorithmOptionAvailability(itemRoot));
                });



                for (HumanBodyBones bone = 0; bone < HumanBodyBones.LastBone; bone++)
                {
                    var humanBodyBoneIndex = (int)bone;
                    Toggle preserveBorderEdgesBoneToggle = new(bone.ToString());
                    preserveBorderEdgesBoneToggle.RegisterValueChangedCallback(changeEvent =>
                    {
                        if(itemRoot.userData is int itemIndex)
                        {
                            var preserveBorderEdgesBonesProperty = EntriesProperty.GetArrayElementAtIndex(itemIndex).FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.PreserveBorderEdgesBones));
                            serializedObject.Update();
                            var currentMask = preserveBorderEdgesBonesProperty.ulongValue;
                            if (changeEvent.newValue)
                            {
                                currentMask |= (1ul << humanBodyBoneIndex);
                            }
                            else
                            {
                                currentMask &= ~(1ul << humanBodyBoneIndex);
                            }
                            preserveBorderEdgesBonesProperty.ulongValue = currentMask;

                            serializedObject.ApplyModifiedProperties();
                        }

                    });
                    preserveBorderEdgesBonesFoldout.Add(preserveBorderEdgesBoneToggle);
                }

                var jointFoldout = itemRoot.Q<Foldout>("PreserveJointTransitionsBonesFoldout");
                for (var i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    var bit = 1ul << i;
                    var toggle = new Toggle(((HumanBodyBones)i).ToString());
                    toggle.RegisterValueChangedCallback(evt =>
                    {
                        if (evt.target != toggle || itemRoot.userData is not int itemIndex) return;
                        serializedObject.Update();
                        var property = EntriesProperty.GetArrayElementAtIndex(itemIndex)
                            .FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.PreserveJointTransitionsBones));
                        property.ulongValue = evt.newValue ? property.ulongValue | bit : property.ulongValue & ~bit;
                        serializedObject.ApplyModifiedProperties();
                    });
                    jointFoldout.Add(toggle);
                }
                LocalizationProvider.Bind(itemRoot, () => UpdateAlgorithmOptionAvailability(itemRoot));
                return itemRoot;
            };

            ndmfPreviewToggle.SetValueWithoutNotify(MeshiaCascadingAvatarMeshSimplifierPreview.PreviewControlNode.IsEnabled.Value);
            ndmfPreviewToggle.RegisterValueChangedCallback(changeEvent =>
            {
                MeshiaCascadingAvatarMeshSimplifierPreview.PreviewControlNode.IsEnabled.Value = changeEvent.newValue;
            });

            Action<bool> onNdmfPreviewEnabledChanged = (newValue) =>
            {
                ndmfPreviewToggle.SetValueWithoutNotify(newValue);
            };
            MeshiaCascadingAvatarMeshSimplifierPreview.PreviewControlNode.IsEnabled.OnChange += onNdmfPreviewEnabledChanged;
            ndmfPreviewToggle.RegisterCallback<DetachFromPanelEvent>(detachFromPanelEvent =>
            {
                MeshiaCascadingAvatarMeshSimplifierPreview.PreviewControlNode.IsEnabled.OnChange -= onNdmfPreviewEnabledChanged;
            });

            void RefreshJointBoneSelections()
            {
                root.Query<TemplateContainer>().ForEach(RefreshJointBoneSelection);
                root.Query<TemplateContainer>().ForEach(UpdateAlgorithmOptionAvailability);
                root.Query<TemplateContainer>().ForEach(RefreshAllocationFields);
            }
            root.RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += RefreshJointBoneSelections);
            root.RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= RefreshJointBoneSelections);

            IVisualElementScheduledItem? scheduledUvPreviewRefresh = null;
            root.TrackSerializedObjectValue(serializedObject, _ =>
            {
                RefreshJointBoneSelections();
                RefreshBudgetGuidance(root);
                scheduledUvPreviewRefresh?.Pause();
                scheduledUvPreviewRefresh = root.schedule.Execute(RefreshOpenUvPreview).StartingIn(150);
            });


            LocalizationProvider.Bind(root, () =>
            {
                RefreshPreviewShortfalls(root);
                RefreshBudgetGuidance(root);
                RefreshAllMeshesAlgorithm();
                targetTriangleCountPresetDropdownField.SetValueWithoutNotify(
                    TargetTriangleCountPresetValueToName.TryGetValue(TargetTriangleCountProperty.intValue, out var preset)
                        ? preset : "Custom");
                Repaint();
            });
            return root;
        }

        private void RefreshPreviewShortfalls(VisualElement root)
        {
            var panel = root.Q<VisualElement>("PreviewShortfalls");
            // A complete, current build supersedes per-mesh preview estimates, even
            // when the final avatar is over budget. The analyzed count reports that.
            if (TryGetBuildAnalysisResult(Target, out var analysis) &&
                analysis.Revision == CurrentAnalysisRevision && string.IsNullOrEmpty(analysis.Error))
            {
                panel.style.display = DisplayStyle.None;
                return;
            }

            var details = new List<string>();
            foreach (var entry in Target.Entries)
            {
                if (!entry.Enabled || !entry.IsValid(Target) || entry.GetTargetRenderer(Target) is not { } renderer ||
                    !MeshiaCascadingAvatarMeshSimplifierPreview.TriangleCountCache.TryGetValue(renderer, out var count) ||
                    count.simplified <= entry.TargetTriangleCount + 1) continue;
                details.Add(Format("{0}: {1:N0} triangles in last preview; requested {2:N0}.",
                    renderer.name, count.simplified, entry.TargetTriangleCount));
            }

            panel.style.display = details.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (details.Count == 0) return;
            root.Q<HelpBox>("PreviewShortfallSummary").text = Format(
                "{0} meshes exceeded their allocations in the last preview. This does not confirm the final avatar is over budget. Analyze NDMF Build to check the current total.", details.Count);
        }

        private bool HasCompletePreviewCounts()
        {
            var entries = Target.Entries.Where(e => e.Enabled && e.IsValid(Target)).ToList();
            return entries.Count > 0 && entries.All(e => e.GetTargetRenderer(Target) is { } renderer &&
                MeshiaCascadingAvatarMeshSimplifierPreview.TriangleCountCache.ContainsKey(renderer));
        }

        private void RefreshBudgetGuidance(VisualElement root)
        {
            var budget = Target.TargetTriangleCount;
            var runSummary = root.Q<Label>("AnalysisRunSummary");
            runSummary.text = lastAnalysisRunRevision == CurrentAnalysisRevision ? lastAnalysisRunMessage : string.Empty;
            runSummary.style.display = string.IsNullOrEmpty(runSummary.text) ? DisplayStyle.None : DisplayStyle.Flex;
            root.Q<Label>("AllocationSummary").text = Tr("Estimated output: analyze to update");
            var reserve = Target.BuildTriangleReserve;
            root.Q<VisualElement>("BuildReserveRow").style.display = reserve != 0 ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q<Button>("ClearBuildReserveButton").style.display = reserve != 0 ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q<Label>("BuildReserveSummary").text = reserve < 0
                ? Format("Build allocation boost: {0:N0} triangles", -(long)reserve)
                : Format("Allocation allowance: {0:N0} triangles", reserve);
            var result = root.Q<Label>("BuildResultSummary");
            result.style.display = DisplayStyle.Flex;
            var summary = root.Q<Label>("BudgetSummary");
            var warning = false;
            var failed = false;
            var outOfDate = false;
            root.Q<Label>("AllocationSummary").tooltip = Tr("Uses measured changes from individual meshes and the last complete build. Later build steps may respond differently. Analyze Build verifies the final result.");
            if (TryGetBuildAnalysisResult(Target, out var analysis))
            {
                failed = !string.IsNullOrEmpty(analysis.Error);
                outOfDate = analysis.Revision != CurrentAnalysisRevision;
                // Upgrade a still-current session result without requiring a build.
                // A stale legacy result has no trustworthy allocation baseline.
                if (!failed && !outOfDate && analysis.Allocations == null)
                {
                    analysis = new BuildAnalysisResult(analysis.TriangleCount,
                        analysis.EstimatedBeforeDownstreamTriangleCount, analysis.Revision, analysis.Error, CaptureAllocations());
                    StoreBuildAnalysisResult(Target, analysis);
                }
                if (!failed) RefreshAllocationProjection(root, analysis);
                if (failed)
                {
                    result.text = Tr("Build result: unavailable");
                    summary.text = Tr("Analysis failed or was incomplete. See calculation details and try again.");
                }
                else if (outOfDate)
                {
                    result.text = Format("Last build: {0:N0} - Out of date", analysis.TriangleCount);
                    summary.text = lastAnalysisReduction > 0
                        ? Format("Auto Adjust lowered allocations by {0:N0} triangles. Analyze again to verify.", lastAnalysisReduction)
                        : lastAnalysisReduction < 0
                            ? Format("Auto Adjust raised allocations by {0:N0} triangles. Analyze again to verify.", -(long)lastAnalysisReduction)
                            : Tr("Settings changed. Analyze again to update the result.");
                }
                else
                {
                    result.text = Format("Build result: {0:N0}", analysis.TriangleCount);
                    result.style.display = DisplayStyle.None;
                    warning = analysis.TriangleCount > budget;
                    summary.text = warning
                        ? Format("{0:N0} triangles over budget", analysis.TriangleCount - budget)
                        : Format("Within budget - {0:N0} triangles remaining", budget - analysis.TriangleCount);
                }
            }
            else
            {
                // This cache has no freshness marker. Never present it as a measured
                // current build, and don't fill missing previews with target guesses.
                var hasPreview = HasCompletePreviewCounts();
                if (hasPreview)
                {
                    var estimate = GetTotalEstimatedFinalTriangleCount(true);
                    result.text = Format("Last preview estimate: {0:N0}", estimate);
                    warning = estimate > budget;
                    summary.text = warning
                        ? Format("Estimated {0:N0} over budget. Analyze to verify.", estimate - budget)
                        : Tr("Unverified estimate. Analyze to check the final count.");
                }
                else
                {
                    result.text = Tr("Build result: not analyzed");
                    summary.text = Tr("Analyze to check the final triangle count.");
                }
            }
            summary.EnableInClassList("budget-warning", warning);
            summary.EnableInClassList("budget-error", failed);
            result.EnableInClassList("budget-out-of-date", outOfDate);
            result.style.opacity = outOfDate ? .65f : 1f;
            summary.style.color = failed
                ? new StyleColor(EditorGUIUtility.isProSkin ? new Color(1f, .45f, .4f) : new Color(.7f, .12f, .08f))
                : warning
                    ? new StyleColor(EditorGUIUtility.isProSkin ? new Color(1f, .75f, .3f) : new Color(.55f, .32f, .02f))
                    : new StyleColor(StyleKeyword.Null);

            if (root.Q<Foldout>("CalculationDetails").value)
            {
                var details = new List<string> { Format("Allocated: {0:N0} / {1:N0}", GetTotalSimplifiedTriangleCount(false), budget),
                    Format("Original triangles: {0:N0}", GetTotalOriginalTriangleCount()),
                    HasCompletePreviewCounts()
                        ? Format("Meshia output (last preview): {0:N0}", GetTotalSimplifiedTriangleCount(true))
                        : Tr("Preview counts are not available for every mesh.") };
                var estimatedFinal = GetTotalEstimatedFinalTriangleCount(true);
                if (DownstreamTriangleEstimator.IsAaoAvailable && HasCompletePreviewCounts())
                    details.Add(Format("AAO estimate: {0:N0}", estimatedFinal));
                if (HasCompletePreviewCounts() && TryGetAnalyzedCalibration(Target, out var calibration, out var calibrationStale))
                {
                    var calibratedFinal = DownstreamTriangleEstimator.ApplyAnalyzedDelta(estimatedFinal,
                        calibration.EstimatedBeforeDownstreamTriangleCount, calibration.TriangleCount);
                    details.Add(Format("Calibrated estimate: {0:N0}", calibratedFinal) +
                        (calibrationStale ? Tr(" - Out of date") : string.Empty));
                }
                if (failed) details.Add(Format("NDMF analysis failed: {0}", Tr(analysis.Error!)));
                root.Q<Label>("TriangleCountLabel").text = string.Join("\n", details);
            }

            if (!root.Q<Foldout>("BudgetBreakdown").value) return;
            var rows = new List<(int index, Renderer renderer, int group, int count, int excess, string text)>();
            for (var index = 0; index < Target.Entries.Count; index++)
            {
                var entry = Target.Entries[index];
                if (!entry.IsValid(Target) || entry.GetTargetRenderer(Target) is not { } renderer ||
                    RendererUtility.GetMesh(renderer) is not { } mesh) continue;
                var hasPreview = MeshiaCascadingAvatarMeshSimplifierPreview.TriangleCountCache.TryGetValue(renderer, out var preview);
                var original = mesh.GetTriangleCount();
                var count = entry.Enabled && hasPreview ? preview.simplified : original;
                var excess = entry.Enabled && hasPreview ? Math.Max(0, count - entry.TargetTriangleCount) : 0;
                var group = !entry.Enabled ? 1 : excess > 1 ? 0 : 2;
                var text = !entry.Enabled
                    ? Format("{0}: not being simplified; {1:N0} source triangles.", renderer.name, original)
                    : hasPreview
                        ? Format("{0}: last preview {1:N0}; allocation {2:N0}; {3:N0} above allocation.", renderer.name, count, entry.TargetTriangleCount, excess)
                        : Format("{0}: {1:N0} source triangles; allocation {2:N0}; no preview measurement yet.", renderer.name, original, entry.TargetTriangleCount);
                if (entry.Fixed) text += Tr(" Fixed allocation.");
                rows.Add((index, renderer, group, count, excess, text));
            }

            var ordered = rows.OrderBy(row => row.group).ThenByDescending(row => row.group == 0 ? row.excess : row.count).ThenBy(row => row.index).ToList();
            var container = root.Q<VisualElement>("BudgetMeshRows");
            // Preserve focus and scroll position on repaints when the data has not changed.
            var signature = CurrentLocale + "|" + string.Join("|", ordered.Select(row => row.index + ":" + row.renderer.GetInstanceID() + ":" + row.group + ":" + row.text));
            if (container.userData is string previous && previous == signature) return;
            container.userData = signature;
            container.Clear();
            if (ordered.Count == 0) container.Add(new Label(Tr("No eligible meshes in this component.")));
            var lastGroup = -1;
            foreach (var row in ordered)
            {
                if (row.group != lastGroup)
                {
                    lastGroup = row.group;
                    container.Add(new Label(Tr(row.group == 0 ? "Above allocation (last preview)" : row.group == 1 ? "Not being simplified" : "Other meshes"))
                        { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
                }
                var item = new VisualElement { name = "BudgetMeshRow", userData = row.index };
                item.Add(new Label(row.text) { style = { whiteSpace = WhiteSpace.Normal } });
                var entry = Target.Entries[row.index];
                item.Add(new Button(() => OpenBudgetMeshSettings(root, entry)) { text = Tr("Open mesh settings") });
                container.Add(item);
            }
        }

        private void OpenBudgetMeshSettings(VisualElement root, MeshiaCascadingAvatarMeshSimplifierRendererEntry entry)
        {
            var index = Target.Entries.IndexOf(entry);
            if (index < 0) return;
            if (entry.GetTargetRenderer(Target) is { } renderer) EditorGUIUtility.PingObject(renderer);
            var list = root.Q<ListView>("EntriesListView");
            pendingBudgetEntry = index;
            list.ScrollToItem(index);
            var item = list.Query<TemplateContainer>().ToList().FirstOrDefault(element => element.userData is int boundIndex && boundIndex == index);
            if (item != null)
            {
                pendingBudgetEntry = -1;
                item.Q<Toggle>("OptionsToggle").value = true;
                item.Q<IntegerField>("TargetTriangleCountField").Focus();
            }
            // The list may be inside the inspector's own scroll view.
            root.schedule.Execute(() =>
            {
                var scroll = list.parent;
                while (scroll != null && scroll is not ScrollView) scroll = scroll.parent;
                (scroll as ScrollView)?.ScrollTo(list);
            });
        }

        private void RefreshJointBoneSelection(TemplateContainer itemRoot)
        {
            if (target == null || itemRoot.userData is not int index || index < 0 || index >= Target.Entries.Count) return;
            var foldout = itemRoot.Q<Foldout>("PreserveJointTransitionsBonesFoldout");
            if (foldout == null) return;
            var mask = Target.Entries[index].PreserveJointTransitionsBones;
            var bone = 0;
            foreach (var toggle in foldout.Children().OfType<Toggle>())
                toggle.SetValueWithoutNotify((mask & (1ul << bone++)) != 0);
        }

        private void RefreshDeformationProtection(VisualElement itemRoot)
        {
            var toggle = itemRoot.Q<Toggle>("DeformationProtectionToggle");
            if (toggle == null || target == null || itemRoot.userData is not int index ||
                index < 0 || index >= Target.Entries.Count) return;
            var entry = Target.Entries[index];
            var renderer = entry.GetTargetRenderer(Target);
            var supportsDeformation = entry.Algorithm != MeshiaCascadingSimplificationAlgorithm.Meshia;
            toggle.style.display = supportsDeformation ? DisplayStyle.Flex : DisplayStyle.None;
            var applicable = supportsDeformation && renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null;
            toggle.SetEnabled(applicable && entry.Enabled);
            var options = entry.Options;
            // This is a settings control, not an estimate of protection on the built mesh.
            // In particular, Auto is enabled regardless of the source mesh's current rig.
            var automatic = options.SkinningProtection.Policy is SkinningProtectionPolicy.Auto or SkinningProtectionPolicy.AutoDeforming;
            var weightProtection = automatic || options.SkinningProtection.Policy == SkinningProtectionPolicy.On ||
                (options.SkinningProtection.Policy == SkinningProtectionPolicy.Legacy && options.SkinningProtection.Enabled);
            var usesJointProtection = entry.Algorithm == MeshiaCascadingSimplificationAlgorithm.FaQem;
            var jointProtection = usesJointProtection && options.SkinningProtection.PreserveJointTransitions;
            var partial = usesJointProtection && weightProtection != jointProtection;
            toggle.SetValueWithoutNotify(weightProtection || jointProtection);
            toggle.EnableInClassList("partial-protection", partial);
            var status = !applicable ? Tr("Deformation protection: not applicable") :
                !entry.Enabled ? Tr("Deformation protection: mesh excluded") :
                partial ? Tr("Deformation protection: partial") :
                weightProtection || jointProtection ? Tr("Deformation protection: on") : Tr("Deformation protection: off");
            toggle.tooltip = status;
            if (applicable && entry.Enabled)
            {
                toggle.tooltip += "\n" + (partial || !toggle.value
                    ? Tr("Click to enable deformation protection.")
                    : Tr("Click to disable deformation protection."));
                if (automatic) toggle.tooltip += "\n" + Tr("Automatic bone-weight selection is enabled.");
                toggle.tooltip += "\n" + Tr("Shows your settings. Individual options are in the cog menu.");
            }
        }

        private void PreviewUvs(VisualElement itemRoot)
        {
            if (itemRoot.userData is not int itemIndex || itemIndex < 0 || itemIndex >= EntriesProperty.arraySize)
            {
                return;
            }

            serializedObject.ApplyModifiedProperties();
            var entry = Target.Entries[itemIndex];
            if (!entry.Enabled || entry.GetTargetRenderer(Target) is not { } targetRenderer ||
                RendererUtility.GetMesh(targetRenderer) is not { } sourceMesh)
            {
                return;
            }

            var simplifiedMesh = CreateUvPreviewMesh(entry, sourceMesh, out var report);
            MeshUvPreviewWindow.ShowComparison(sourceMesh, simplifiedMesh);
            MeshUvPreviewWindow.ShowFallbackNotification(sourceMesh, report);
        }

        private void RefreshOpenUvPreview()
        {
            serializedObject.ApplyModifiedProperties();
            foreach (var entry in Target.Entries)
            {
                if (!entry.Enabled || entry.GetTargetRenderer(Target) is not { } targetRenderer ||
                    RendererUtility.GetMesh(targetRenderer) is not { } sourceMesh ||
                    !MeshUvPreviewWindow.IsShowingComparison(sourceMesh))
                {
                    continue;
                }

                var simplifiedMesh = CreateUvPreviewMesh(entry, sourceMesh, out var report);
                if (!MeshUvPreviewWindow.UpdateComparison(sourceMesh, simplifiedMesh))
                {
                    DestroyImmediate(simplifiedMesh);
                }
                else
                {
                    MeshUvPreviewWindow.ShowFallbackNotification(sourceMesh, report);
                }
                return;
            }
        }

        private Mesh CreateUvPreviewMesh(
            MeshiaCascadingAvatarMeshSimplifierRendererEntry entry,
            Mesh sourceMesh,
            out MeshSimplificationReport report)
        {
            var simplifiedMesh = new Mesh { name = $"{sourceMesh.name}-UV-Preview" };
            try
            {
                var simplificationTarget = entry.CreateTarget(sourceMesh.GetTriangleCount());
                var avatarRoot = Target.transform.parent != null ? Target.transform.parent.gameObject : Target.gameObject;
                var preserveBorderEdgesBoneIndices = MeshiaCascadingAvatarMeshSimplifier.GetPreserveBorderEdgesBoneIndices(
                    avatarRoot,
                    Target,
                    entry);
                report = MeshSimplifier.SimplifyWithReport(
                    sourceMesh,
                    simplificationTarget,
                    NdmfPlugin.ResolveOptions(avatarRoot, entry.GetTargetRenderer(Target)!,
                        MeshiaCascadingAvatarMeshSimplifier.GetJointProtectionOptions(avatarRoot, Target, entry)),
                    preserveBorderEdgesBoneIndices,
                    simplifiedMesh);
                return simplifiedMesh;
            }
            catch
            {
                DestroyImmediate(simplifiedMesh);
                throw;
            }
        }

        private void UpdateAlgorithmOptionAvailability(VisualElement itemRoot)
        {
            RefreshDeformationProtection(itemRoot);
            if (itemRoot.userData is not int itemIndex || itemIndex < 0 || itemIndex >= EntriesProperty.arraySize)
            {
                return;
            }

            var entryProperty = EntriesProperty.GetArrayElementAtIndex(itemIndex);
            var algorithmProperty = entryProperty.FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.Algorithm));
            var usesBlenderDecimate = algorithmProperty.enumValueIndex ==
                (int)MeshiaCascadingSimplificationAlgorithm.BlenderDecimate;
            var usesUvLoopDissolve = algorithmProperty.enumValueIndex ==
                (int)MeshiaCascadingSimplificationAlgorithm.UvLoopDissolve;
            var usesFaQem = algorithmProperty.enumValueIndex ==
                (int)MeshiaCascadingSimplificationAlgorithm.FaQem;
            var supportsSelectedBorderBones = !usesBlenderDecimate && !usesUvLoopDissolve;

            var optionsField = itemRoot.Q<PropertyField>("OptionsField");
            var preserveBorderEdgesBonesFoldout = itemRoot.Q<Foldout>("PreserveBorderEdgesBonesFoldout");
            var blenderOptionsHelpBox = itemRoot.Q<HelpBox>("BlenderOptionsHelpBox");
            var uvLoopDissolveHelpBox = itemRoot.Q<HelpBox>("UvLoopDissolveHelpBox");
            var faQemHelpBox = itemRoot.Q<HelpBox>("FaQemHelpBox");
            var optionsToggle = itemRoot.Q<Toggle>("OptionsToggle");
            MeshSimplifierOptionsDrawer.SetAlgorithmVisibility(optionsField,
                usesFaQem ? MeshSimplificationTargetKind.FaQemTriangleCount :
                usesBlenderDecimate ? MeshSimplificationTargetKind.BlenderDecimateRatio :
                usesUvLoopDissolve ? MeshSimplificationTargetKind.UvLoopDissolveTriangleCount :
                MeshSimplificationTargetKind.AbsoluteTriangleCount);

            itemRoot.Q<Foldout>("PreserveJointTransitionsBonesFoldout").style.display = usesFaQem && optionsToggle.value
                ? DisplayStyle.Flex : DisplayStyle.None;
            optionsField.SetEnabled(true);
            preserveBorderEdgesBonesFoldout.SetEnabled(supportsSelectedBorderBones);
            preserveBorderEdgesBonesFoldout.style.display = supportsSelectedBorderBones && optionsToggle.value
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            blenderOptionsHelpBox.style.display = usesBlenderDecimate && optionsToggle.value
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            uvLoopDissolveHelpBox.style.display = usesUvLoopDissolve && optionsToggle.value
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            faQemHelpBox.style.display = usesFaQem && optionsToggle.value
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        static Dictionary<string, int> TargetTriangleCountPresetNameToValue { get; } = new()
        {
            ["PC-Poor-Medium-Good"] = 70000,
            ["PC-Excellent"] = 32000,
            ["Mobile-Poor"] = 20000,
            ["Mobile-Medium"] = 15000,
            ["Mobile-Good"] = 10000,
            ["Mobile-Excellent"] = 7500,
        };

        static Dictionary<int, string> TargetTriangleCountPresetValueToName { get; } = TargetTriangleCountPresetNameToValue.ToDictionary(keyValue => keyValue.Value, keyValue => keyValue.Key);


        private AllocationSnapshot CaptureAllocations()
        {
            // Compare allocation edits separately from algorithm/protection/exclusion
            // edits. The latter cannot be estimated from a simple triangle delta.
            var settings = EditorJsonUtility.ToJson(Target);
            settings = System.Text.RegularExpressions.Regex.Replace(settings,
                "\"(?:TargetTriangleCount|BuildTriangleReserve)\":-?\\d+", "\"allocation\":0");
            settings = System.Text.RegularExpressions.Regex.Replace(settings,
                "\"AutoAdjustEnabled\":(?:true|false)", "\"AutoAdjustEnabled\":false");
            return new AllocationSnapshot
            {
                Counts = Target.Entries.Select(entry => TryGetSimplifiedTriangleCount(entry, false, out var count) ? count : -1).ToArray(),
                Settings = settings,
            };
        }

        private void RefreshAllocationProjection(VisualElement root, BuildAnalysisResult analysis)
        {
            var label = root.Q<Label>("AllocationSummary");
            if (analysis.Allocations is not { } baseline) return;
            var current = CaptureAllocations();
            if (current.Settings != baseline.Settings || current.Counts.Length != baseline.Counts.Length) return;
            var changed = Enumerable.Range(0, current.Counts.Length)
                .Where(i => current.Counts[i] != baseline.Counts[i]).ToArray();
            if (changed.Length == 0)
            {
                if (analysis.Revision == CurrentAnalysisRevision)
                    label.text = Format("Measured output: {0:N0} / {1:N0}", analysis.TriangleCount, Target.TargetTriangleCount);
                return;
            }
            if (!HasMeasuredInputs(current.Settings) || baseline.Outputs.Length != current.Counts.Length ||
                changed.Any(i => baseline.Outputs[i] < 0 || !measuredMeshes!.Meshes.ContainsKey(i))) return;
            var missing = changed.Where(i => !measuredMeshes!.Meshes[i].Outputs.ContainsKey(current.Counts[i])).ToArray();
            if (missing.Length > 0)
            {
                var key = string.Join(",", current.Counts);
                if (failedEstimate == key) return;
                label.text = Tr("Estimated output: measuring changed meshes...");
                if (!estimateScheduled && !estimateRunning && !s_analysisInProgress)
                {
                    estimateScheduled = true;
                    root.schedule.Execute(() =>
                    {
                        estimateScheduled = false;
                        if (this == null || target == null || s_analysisInProgress ||
                            !HasMeasuredInputs(current.Settings) || !CaptureAllocations().Counts.SequenceEqual(current.Counts)) return;
                        _ = MeasureAllocationProjectionAsync(root, current, missing, key);
                    }).StartingIn(250);
                }
                return;
            }
            var projected = Math.Max(0L, analysis.TriangleCount + changed.Sum(i =>
                (long)measuredMeshes!.Meshes[i].Outputs[current.Counts[i]] - baseline.Outputs[i]));
            label.text = projected > Target.TargetTriangleCount
                ? Format("Estimated output: ~{0:N0} / {1:N0} ({2:N0} over)", projected, Target.TargetTriangleCount, projected - Target.TargetTriangleCount)
                : Format("Estimated output: ~{0:N0} / {1:N0} ({2:N0} spare)", projected, Target.TargetTriangleCount, Target.TargetTriangleCount - projected);
            label.tooltip = Tr("Uses measured changes from individual meshes and the last complete build. Later build steps may respond differently. Analyze Build verifies the final result.");
        }

        private async System.Threading.Tasks.Task MeasureAllocationProjectionAsync(
            VisualElement root, AllocationSnapshot current, int[] missing, string key)
        {
            if (estimateRunning || measuredMeshes == null) return;
            var inputs = measuredMeshes;
            estimateRunning = true;
            bool StillCurrent() => this != null && target != null && !s_analysisInProgress &&
                inputs == measuredMeshes && HasMeasuredInputs(current.Settings) &&
                CaptureAllocations().Settings == current.Settings &&
                CaptureAllocations().Counts.SequenceEqual(current.Counts);
            try
            {
                foreach (var i in missing)
                {
                    // Coalesce edits: finish the running job, then skip any remaining
                    // outdated requests. The next refresh schedules the latest values.
                    if (!StillCurrent()) break;
                    await inputs.Meshes[i].MeasureAsync(current.Counts[i]);
                }
            }
            catch (Exception exception)
            {
                if (StillCurrent())
                {
                    failedEstimate = key;
                    Debug.LogException(exception, Target);
                }
            }
            finally
            {
                estimateRunning = false;
                if (this != null && target != null && root.panel != null && !s_analysisInProgress)
                    RefreshBudgetGuidance(root);
            }
        }

        private int GetTotalSimplifiedTriangleCount(bool usePreview)
        {
            var totalCount = 0;
            var target = Target;
            foreach (var entry in target.Entries)
            {
                if (entry.IsValid(target))
                {
                    totalCount += TryGetSimplifiedTriangleCount(entry, usePreview, out var triangleCount) ? triangleCount : 0;
                }
            }
            return totalCount;
        }

        private int GetTotalOriginalTriangleCount()
        {
            var totalCount = 0;
            var target = Target;
            foreach (var entry in target.Entries)
            {
                if (entry.IsValid(target))
                {
                    totalCount += TryGetOriginalTriangleCount(entry, false, out var triangleCount) ? triangleCount : 0;
                }
            }
            return totalCount;
        }

        private int GetTotalEstimatedFinalTriangleCount(bool usePreview)
        {
            var totalCount = 0;
            var target = Target;
            foreach (var entry in target.Entries)
            {
                if (entry.IsValid(target) && TryGetEstimatedFinalTriangleCount(entry, usePreview, out var triangleCount))
                {
                    totalCount += triangleCount;
                }
            }
            return totalCount;
        }

        private bool TryGetEstimatedFinalTriangleCount(
            MeshiaCascadingAvatarMeshSimplifierRendererEntry entry,
            bool preferPreview,
            out int triangleCount)
        {
            if (!TryGetSimplifiedTriangleCount(entry, preferPreview, out triangleCount) ||
                entry.GetTargetRenderer(Target) is not { } renderer)
            {
                return false;
            }

            triangleCount = DownstreamTriangleEstimator.EstimateFinalTriangleCount(renderer, triangleCount);
            return true;
        }
        private bool TryGetSimplifiedTriangleCount(MeshiaCascadingAvatarMeshSimplifierRendererEntry entry, bool preferPreview, out int triangleCount)
        {

            if (!entry.Enabled)
            {
                return TryGetOriginalTriangleCount(entry, preferPreview, out triangleCount);
            }
            if(entry.GetTargetRenderer(Target) is not { } targetRenderer)
            {
                triangleCount = -1;
                return false;
            }
            if (preferPreview && MeshiaCascadingAvatarMeshSimplifierPreview.TriangleCountCache.TryGetValue(targetRenderer, out var triCount))
            {
                triangleCount = triCount.simplified;
                return true;
            }
            else
            {

                if (RendererUtility.GetMesh(targetRenderer) is { } mesh)
                {
                    triangleCount = Math.Min(mesh.GetTriangleCount(), entry.TargetTriangleCount);
                    return true;
                }
                else
                {
                    triangleCount = -1;
                    return false;
                }
            }
        }
        private bool TryGetOriginalTriangleCount(MeshiaCascadingAvatarMeshSimplifierRendererEntry entry, bool preferPreview, out int triangleCount)
        {
            if (entry.GetTargetRenderer(Target) is not { } targetRenderer)
            {
                triangleCount = -1;
                return false;
            }
            if (preferPreview && MeshiaCascadingAvatarMeshSimplifierPreview.TriangleCountCache.TryGetValue(targetRenderer, out var triCount))
            {
                triangleCount = triCount.proxy;
                return true;
            }
            else
            {
                if (RendererUtility.GetMesh(targetRenderer) is { } mesh)
                {

                    triangleCount = mesh.GetTriangleCount();

                    return true;
                }
                else
                {
                    triangleCount = -1;
                    return false;
                }
            }
        }

        private void RefreshAllocationFields(TemplateContainer itemRoot)
        {
            if (itemRoot.userData is not int index || index < 0 || index >= Target.Entries.Count) return;
            var count = Target.Entries[index].TargetTriangleCount;
            itemRoot.Q<SliderInt>("TargetTriangleCountSlider")?.SetValueWithoutNotify(count);
            itemRoot.Q<IntegerField>("TargetTriangleCountField")?.SetValueWithoutNotify(count);
            var tooltip = Tr("Requested triangles. Geometry protection may keep the actual output above this value.");
            if (TryGetBuildAnalysisResult(Target, out var result) && result.Allocations is { } baseline &&
                baseline.Settings == CaptureAllocations().Settings && index < baseline.Outputs.Length && baseline.Outputs[index] >= 0)
            {
                var output = baseline.Outputs[index];
                if (HasMeasuredInputs(baseline.Settings) && measuredMeshes!.Meshes.TryGetValue(index, out var mesh) &&
                    mesh.Outputs.TryGetValue(count, out var measured)) output = measured;
                tooltip = Format("Requested: {0:N0}. Last measured Meshia output: {1:N0}, before later build steps.", count, output);
            }
            itemRoot.Q<SliderInt>("TargetTriangleCountSlider").tooltip = tooltip;
            itemRoot.Q<IntegerField>("TargetTriangleCountField").tooltip = tooltip;
        }

        private void SetManualAllocation(int index, int value)
        {
            serializedObject.Update();
            var entry = Target.Entries[index];
            if (!entry.Enabled || !entry.IsValid(Target) ||
                !TryGetOriginalTriangleCount(entry, false, out var maximum)) return;
            value = Mathf.Clamp(value, 0, maximum);
            var previous = entry.TargetTriangleCount;
            if (value == previous) return;

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();

            // Handle both controls explicitly: serialized binding refreshes must never
            // masquerade as another user edit and recursively redistribute the budget.
            EntriesProperty.GetArrayElementAtIndex(index)
                .FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.TargetTriangleCount))
                .intValue = value;
            serializedObject.ApplyModifiedProperties();
            if (Target.AutoAdjustEnabled)
            {
                AdjustQuality(index);
            }
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(undoGroup);
            InvalidateTriangleAnalysis();
        }

        private void AdjustQuality(int fixedIndex = -1)
        {
            serializedObject.ApplyModifiedProperties();
            // Only successful full builds calibrate allocations in either direction.
            // The requested final budget remains unchanged; preview estimates cannot change it.
            var targetTotalCount = (int)Math.Min(int.MaxValue, Math.Max(0L,
                (long)TargetTriangleCountProperty.intValue - Target.BuildTriangleReserve));

            var target = Target;
            var entries = target.Entries;
            var entriesProperty = EntriesProperty;

            Undo.RecordObject(target, Tr("Adjust Quality"));

            // 比例配分で差分を分配（目標値に到達するまでループ）
            for (int iteration = 0; iteration < 5; iteration++)
            {
                var currentTotal = 0;
                var adjustableTotal = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];

                    if (!entry.IsValid(target))
                    {
                        continue;
                    }
                    var entryProperty = entriesProperty.GetArrayElementAtIndex(i);

                    if (!TryGetSimplifiedTriangleCount(entry, false, out var triangleCount)) continue;

                    currentTotal += triangleCount;

                    if (entry.Enabled && !entry.Fixed && i != fixedIndex)
                    {
                        adjustableTotal += triangleCount;
                    }
                }

                // If a previous edit used the whole budget, allow zeroed peers to
                // recover when it is returned, using their source sizes as weights.
                var useSourceWeights = adjustableTotal == 0;
                var allocationWeight = adjustableTotal;
                if (useSourceWeights)
                {
                    for (var i = 0; i < entries.Count; i++)
                    {
                        var entry = entries[i];
                        if (i != fixedIndex && entry.Enabled && !entry.Fixed && entry.IsValid(target) &&
                            TryGetOriginalTriangleCount(entry, false, out var sourceCount))
                            allocationWeight += sourceCount;
                    }
                }
                if (allocationWeight <= 0) break;

                // Excluded/locked meshes can consume the whole budget. Remaining
                // allocations may reach zero; the simplifier still retains its guards.
                var adjustableTargetCount = Math.Max(0, targetTotalCount - (currentTotal - adjustableTotal));

                // 比例配分で調整
                var proportion = (float)adjustableTargetCount / allocationWeight;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (i == fixedIndex) continue;

                    var entry = entries[i];
                    if (!entry.IsValid(target))
                    {
                        continue;
                    }
                    var entryProperty = entriesProperty.GetArrayElementAtIndex(i);

                    if (entry.Enabled && !entry.Fixed)
                    {

                        TryGetSimplifiedTriangleCount(entry, false, out var currentValue);
                        TryGetOriginalTriangleCount(entry, false, out var maxTriangleCount);

                        var weight = useSourceWeights ? maxTriangleCount : currentValue;
                        var newValue = Mathf.Clamp((int)(weight * proportion), 0, maxTriangleCount);
                        entry.TargetTriangleCount = newValue;
                    }
                }
            }
            EditorUtility.SetDirty(target);
            serializedObject.Update();
        }

        private void SetQualityAll(float ratio)
        {
            var target = Target;
            var entries = target.Entries;
            var entriesProperty = EntriesProperty;
            for (int i = 0; i < entries.Count; i++)
            {

                var entry = entries[i];
                if (!entry.IsValid(target))
                {
                    continue;
                }

                if (!entry.Fixed)
                {
                    var entryProperty = entriesProperty.GetArrayElementAtIndex(i);

                    TryGetOriginalTriangleCount(entry, false, out var originalTriangleCount);
                    var targetTriangleCountProperty = entryProperty.FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.TargetTriangleCount));


                    targetTriangleCountProperty.intValue = Mathf.Clamp(
                        (int)(originalTriangleCount * ratio),
                        0,
                        originalTriangleCount);
                }
            }
        }

        internal enum BuildFitStop
        {
            Measured, WithinBudget, Cancelled, Failed, NoProgress, NoCapacity, Limit
        }

        // A fitting run commits only a verified target-range result. All other
        // exits roll back every trial, not merely the last unsuccessful step.
        internal static BuildFitStop RunBoundedBuildFit(bool autoAdjust, int budget,
            Func<int, bool> cancelBeforeBuild, Func<int?> measure, Func<bool> correct, Action? rollback = null)
        {
            var maxBuilds = autoAdjust ? 4 : 1;
            long previousDistance = long.MaxValue;
            var attemptedCorrection = false;
            BuildFitStop Finish(BuildFitStop stop)
            {
                if (attemptedCorrection && stop != BuildFitStop.WithinBudget && stop != BuildFitStop.Measured)
                    rollback?.Invoke();
                return stop;
            }
            try
            {
                if (cancelBeforeBuild(0)) return Finish(BuildFitStop.Cancelled);
                for (var pass = 0; pass < maxBuilds; pass++)
                {
                    var measured = measure();
                    if (!measured.HasValue) return Finish(BuildFitStop.Failed);
                    if (!autoAdjust) return Finish(BuildFitStop.Measured);
                    var excess = (long)measured.Value - budget;
                    var distance = excess > 0 ? excess : Math.Max(0, -excess - Math.Max(1, budget / 1000));
                    if (distance == 0) return Finish(BuildFitStop.WithinBudget);
                    if (distance >= previousDistance) return Finish(BuildFitStop.NoProgress);
                    if (pass + 1 == maxBuilds) return Finish(BuildFitStop.Limit);
                    if (cancelBeforeBuild(pass + 1)) return Finish(BuildFitStop.Cancelled);
                    attemptedCorrection = true;
                    if (!correct()) return Finish(BuildFitStop.NoCapacity);
                    previousDistance = distance;
                }
                return Finish(BuildFitStop.Limit);
            }
            catch
            {
                if (attemptedCorrection) rollback?.Invoke();
                throw;
            }
        }

        private void AnalyzeNdmfBuild(Button button)
        {
            if (s_analysisInProgress) return;
            lastAnalysisReduction = 0;
            lastAnalysisRunMessage = string.Empty;
            var avatarRoot = RuntimeUtil.FindAvatarInParents(Target.transform);
            if (avatarRoot == null)
            {
                StoreBuildAnalysisResult(Target, new BuildAnalysisResult(
                    0, 0, CurrentAnalysisRevision, "Could not find the avatar root."));
                return;
            }

            var originalButtonText = button.text;
            var previousDisablePreviewDepth = NDMFPreview.DisablePreviewDepth;
            var autoAdjust = Target.AutoAdjustEnabled;
            var maxBuilds = autoAdjust ? 4 : 1;
            var completed = 0;
            var allocatedBeforeBuild = 0;
            var startingAllocations = Target.Entries.Select(entry => entry.TargetTriangleCount).ToArray();
            var startingReserve = Target.BuildTriangleReserve;
            BuildAnalysisResult? startingAnalysis = null;
            retainedStartingMeshes = null;
            void RestoreStartingAllocations()
            {
                if (Target.BuildTriangleReserve == startingReserve &&
                    Target.Entries.Select(entry => entry.TargetTriangleCount).SequenceEqual(startingAllocations)) return;
                Undo.RecordObject(Target, Tr("Correct allocations from build"));
                for (var i = 0; i < startingAllocations.Length; i++)
                    Target.Entries[i].TargetTriangleCount = startingAllocations[i];
                Target.BuildTriangleReserve = startingReserve;
                if (measuredMeshes != retainedStartingMeshes) measuredMeshes?.Dispose();
                measuredMeshes = retainedStartingMeshes;
                EditorUtility.SetDirty(Target);
                serializedObject.Update();
                SessionState.SetInt(AnalysisRevisionSessionKey, CurrentAnalysisRevision + 1);
                DownstreamTriangleEstimator.Invalidate();
                if (startingAnalysis is { } baseline)
                    StoreBuildAnalysisResult(Target, new BuildAnalysisResult(baseline.TriangleCount,
                        baseline.EstimatedBeforeDownstreamTriangleCount, CurrentAnalysisRevision, baseline.Error, baseline.Allocations));
            }
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            s_analysisInProgress = true;
            NDMFPreview.DisablePreviewDepth = previousDisablePreviewDepth + 1;
            button.SetEnabled(false);
            button.text = Tr("Analyzing...");
            try
            {
                var stop = RunBoundedBuildFit(autoAdjust, Target.TargetTriangleCount,
                    pass => EditorUtility.DisplayCancelableProgressBar("Meshia",
                        Format("Analyzing build {0} of {1}. Cancel stops before the next build.", pass + 1, maxBuilds),
                        (float)pass / maxBuilds),
                    () =>
                    {
                        lastAnalysisReduction = 0;
                        allocatedBeforeBuild = GetTotalSimplifiedTriangleCount(false);
                        var analysis = MeasureNdmfBuild(avatarRoot.gameObject);
                        StoreBuildAnalysisResult(Target, analysis);
                        if (completed == 0 && string.IsNullOrEmpty(analysis.Error))
                        {
                            startingAnalysis = analysis;
                            retainedStartingMeshes = measuredMeshes;
                        }
                        completed++;
                        return string.IsNullOrEmpty(analysis.Error) ? (int?)analysis.TriangleCount : null;
                    },
                    () => ApplyAnalyzedBudgetCorrection(allocatedBeforeBuild),
                    RestoreStartingAllocations);
                if (stop != BuildFitStop.WithinBudget && stop != BuildFitStop.Measured)
                    failedEstimate = string.Empty;
                var reason = stop switch
                {
                    BuildFitStop.WithinBudget => Tr("Target range reached."),
                    BuildFitStop.Cancelled => Tr("Cancelled. Kept the allocations from before this run."),
                    BuildFitStop.Failed => Tr("A trial build failed. Kept the starting allocations; any displayed baseline result belongs to those settings."),
                    BuildFitStop.NoProgress => Tr("Fitting stopped improving. Restored all allocations and the allowance from before this run."),
                    BuildFitStop.NoCapacity => Tr("Cannot safely fit this budget. Kept the starting allocations; review exclusions, locks, or protections."),
                    BuildFitStop.Limit => Tr("Reached the four-build limit without fitting. Restored the starting allocations and their measured result."),
                    _ => string.Empty,
                };
                lastAnalysisRunMessage = Format("Builds analyzed: {0}.", completed) + " " + reason;
                lastAnalysisRunRevision = CurrentAnalysisRevision;
            }
            catch (OperationCanceledException)
            {
                RestoreStartingAllocations();
                lastAnalysisRunMessage = Tr("Cancelled. Kept the allocations from before this run.");
                lastAnalysisRunRevision = CurrentAnalysisRevision;
            }
            catch (Exception exception)
            {
                RestoreStartingAllocations();
                Debug.LogException(exception, Target);
                lastAnalysisRunMessage = Tr("Analysis interrupted. Restored the starting allocations; analyze again if their result is out of date.");
                lastAnalysisRunRevision = CurrentAnalysisRevision;
                failedEstimate = string.Empty;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                NDMFPreview.DisablePreviewDepth = previousDisablePreviewDepth;
                button.text = originalButtonText;
                button.SetEnabled(true);
                if (retainedStartingMeshes != measuredMeshes) retainedStartingMeshes?.Dispose();
                retainedStartingMeshes = null;
                try
                {
                    // Drain generated Undo records while the analysis guard is active.
                    Undo.FlushUndoRecordObjects();
                    Undo.CollapseUndoOperations(undoGroup);
                }
                finally
                {
                    EditorApplication.delayCall += CompleteTriangleAnalysis;
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                }
            }
        }

        private BuildAnalysisResult MeasureNdmfBuild(GameObject avatarRoot)
        {
            var allocations = CaptureAllocations();
            allocations.Outputs = Enumerable.Repeat(-1, Target.Entries.Count).ToArray();
            var captured = new MeasuredMeshSet { Settings = allocations.Settings, InputRevision = meshInputRevision };
            GameObject? clone = null;
            MeshiaCascadingAvatarMeshSimplifier? cloneComponent = null;
            void CaptureMesh(GameObject root, Renderer renderer, Mesh source, MeshSimplificationTarget targetValue,
                MeshSimplifierOptions options, System.Collections.BitArray? preserve, Mesh output)
            {
                if (root != clone || cloneComponent == null) return;
                var index = cloneComponent.Entries.FindIndex(entry => entry.Enabled && entry.GetTargetRenderer(cloneComponent) == renderer);
                if (index < 0) return;
                var sourceCopy = Instantiate(source);
                sourceCopy.hideFlags = HideFlags.HideAndDontSave;
                var sourceCount = sourceCopy.GetTriangleCount();
                var bones = preserve == null ? null : (System.Collections.BitArray)preserve.Clone();
                var requested = Target.Entries[index].TargetTriangleCount;
                var produced = output.GetTriangleCount();
                allocations.Outputs[index] = produced;
                if (captured.Meshes.TryGetValue(index, out var old)) old.Dispose();
                captured.Meshes[index] = new MeasuredMeshResponse(index, sourceCount, requested, produced, count =>
                {
                    var destination = new Mesh();
                    try
                    {
                        var trial = targetValue;
                        trial.Value = trial.Kind == MeshSimplificationTargetKind.BlenderDecimateRatio
                            ? Mathf.Clamp01(count / (float)Math.Max(1, sourceCount)) : count;
                        MeshSimplifier.SimplifyWithReport(sourceCopy, trial, options, bones, destination);
                        return destination.GetTriangleCount();
                    }
                    finally { DestroyImmediate(destination); }
                }, sourceCopy, async count =>
                {
                    var destination = new Mesh();
                    try
                    {
                        var trial = targetValue;
                        trial.Value = trial.Kind == MeshSimplificationTargetKind.BlenderDecimateRatio
                            ? Mathf.Clamp01(count / (float)Math.Max(1, sourceCount)) : count;
                        await MeshSimplifier.SimplifyAsync(sourceCopy, trial, options, bones, destination);
                        return destination.GetTriangleCount();
                    }
                    finally { DestroyImmediate(destination); }
                });
            }
            var finalTriangleCount = 0;
            var estimate = GetTotalEstimatedFinalTriangleCount(true);
            string? error = null;
            try
            {
                clone = Instantiate(avatarRoot);
                clone.name = $"{avatarRoot.name} (Meshia Triangle Analysis)";
                clone.SetActive(true);
                var componentPath = AnimationUtility.CalculateTransformPath(Target.transform, avatarRoot.transform);
                var cloneTransform = string.IsNullOrEmpty(componentPath) ? clone.transform : clone.transform.Find(componentPath);
                var componentIndex = Array.IndexOf(Target.GetComponents<MeshiaCascadingAvatarMeshSimplifier>(), Target);
                cloneComponent = cloneTransform == null ? null : cloneTransform.GetComponents<MeshiaCascadingAvatarMeshSimplifier>().ElementAtOrDefault(componentIndex);
                NdmfPlugin.MeshMeasured += CaptureMesh;
                var buildContext = AvatarProcessor.ProcessAvatar(clone, AmbientPlatform.CurrentPlatform);
                if (!buildContext.Successful) error = "Build reported errors; count may be incomplete.";
                foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer is MeshRenderer or SkinnedMeshRenderer && RendererUtility.GetMesh(renderer) is { } mesh)
                        finalTriangleCount += mesh.GetTriangleCount();
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, Target);
                error = exception.Message;
            }
            finally
            {
                NdmfPlugin.MeshMeasured -= CaptureMesh;
                if (clone != null) DestroyImmediate(clone);
                try
                {
                    AvatarProcessor.CleanTemporaryAssets();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, Target);
                    error ??= exception.Message;
                }
                // Build plugin Undo records must not invalidate the next sample.
                Undo.FlushUndoRecordObjects();
            }
            if (string.IsNullOrEmpty(error))
            {
                if (measuredMeshes != retainedStartingMeshes) measuredMeshes?.Dispose();
                measuredMeshes = captured;
                failedEstimate = string.Empty;
            }
            else captured.Dispose();
            return new BuildAnalysisResult(finalTriangleCount, estimate, CurrentAnalysisRevision, error, allocations);
        }

        private bool ApplyAnalyzedBudgetCorrection(int analyzedAllocation)
        {
            if (!Target.AutoAdjustEnabled || !TryGetBuildAnalysisResult(Target, out var analysis) ||
                analysis.Revision != CurrentAnalysisRevision || !string.IsNullOrEmpty(analysis.Error) ||
                GetTotalSimplifiedTriangleCount(false) != analyzedAllocation ||
                !HasMeasuredInputs(CaptureAllocations().Settings)) return false;
            var tolerance = Math.Max(1, Target.TargetTriangleCount / 1000);
            var difference = (long)Target.TargetTriangleCount - analysis.TriangleCount;
            if (difference >= 0 && difference <= tolerance) return false;
            if (difference > 0) difference -= tolerance / 2;
            var candidates = measuredMeshes!.Meshes.Values.Where(mesh =>
                mesh.Index < Target.Entries.Count && Target.Entries[mesh.Index].Enabled && !Target.Entries[mesh.Index].Fixed).ToArray();
            var targets = Target.Entries.Select(entry => entry.TargetTriangleCount).ToArray();
            if (candidates.Length == 0) return false;
            var plan = MeasuredMeshBudget.Plan(candidates, targets, difference,
                () => EditorUtility.DisplayCancelableProgressBar("Meshia", Tr("Measuring mesh responses..."), .5f));
            if (plan.Count == 0) return false;
            Undo.RecordObject(Target, Tr("Correct allocations from build"));
            foreach (var item in plan) Target.Entries[item.Key].TargetTriangleCount = item.Value;
            var allocated = GetTotalSimplifiedTriangleCount(false);
            Target.BuildTriangleReserve = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, (long)Target.TargetTriangleCount - allocated));
            EditorUtility.SetDirty(Target);
            serializedObject.Update();
            lastAnalysisReduction = analyzedAllocation - allocated;
            Undo.FlushUndoRecordObjects();
            SessionState.SetInt(AnalysisRevisionSessionKey, CurrentAnalysisRevision + 1);
            DownstreamTriangleEstimator.Invalidate();
            return true;
        }

        private static void CompleteTriangleAnalysis()
        {
            try
            {
                // Build plugins can record Undo changes on generated materials and
                // animation assets. Unity may deliver those records after delayCall;
                // drain them while invalidation is still suppressed for this build.
                Undo.FlushUndoRecordObjects();
            }
            finally
            {
                s_analysisInProgress = false;
            }
        }

        private static bool TryGetBuildAnalysisResult(
            MeshiaCascadingAvatarMeshSimplifier target,
            out BuildAnalysisResult result)
        {
            var key = GetBuildAnalysisResultKey(target);
            if (BuildAnalysisCache.TryGetValue(key, out result))
            {
                return true;
            }

            var json = SessionState.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(json) || JsonUtility.FromJson<SerializedBuildAnalysisResult>(json) is not { } stored)
            {
                result = default;
                return false;
            }

            result = new BuildAnalysisResult(
                stored.TriangleCount,
                stored.EstimatedBeforeDownstreamTriangleCount,
                stored.Revision,
                string.IsNullOrEmpty(stored.Error) ? null : stored.Error, stored.Allocations);
            BuildAnalysisCache[key] = result;
            return true;
        }

        private static void StoreBuildAnalysisResult(
            MeshiaCascadingAvatarMeshSimplifier target,
            BuildAnalysisResult result)
        {
            var key = GetBuildAnalysisResultKey(target);
            BuildAnalysisCache[key] = result;
            SessionState.SetString(key, JsonUtility.ToJson(new SerializedBuildAnalysisResult
            {
                TriangleCount = result.TriangleCount,
                EstimatedBeforeDownstreamTriangleCount = result.EstimatedBeforeDownstreamTriangleCount,
                Revision = result.Revision,
                Error = result.Error,
                Allocations = result.Allocations,
            }));
        }

        private static string GetBuildAnalysisResultKey(MeshiaCascadingAvatarMeshSimplifier target)
        {
            return AnalysisResultSessionKeyPrefix + GlobalObjectId.GetGlobalObjectIdSlow(target);
        }

        private static bool TryGetAnalyzedCalibration(
            MeshiaCascadingAvatarMeshSimplifier target,
            out BuildAnalysisResult calibration,
            out bool stale)
        {
            if (TryGetBuildAnalysisResult(target, out calibration) &&
                calibration.TriangleCount > 0 &&
                calibration.EstimatedBeforeDownstreamTriangleCount > 0 &&
                string.IsNullOrEmpty(calibration.Error))
            {
                stale = calibration.Revision != CurrentAnalysisRevision;
                return true;
            }

            stale = false;
            return false;
        }

    }

    internal static class GUIStyleHelper
    {
        private static GUIStyle? m_iconButtonStyle;
        public static GUIStyle IconButtonStyle
        {
            get
            {
                if (m_iconButtonStyle == null) m_iconButtonStyle = InitIconButtonStyle();
                return m_iconButtonStyle;
            }
        }
        static GUIStyle InitIconButtonStyle()
        {
            var style = new GUIStyle();
            return style;
        }

        private static GUIStyle? m_redStyle;
        public static GUIStyle RedStyle
        {
            get
            {
                if (m_redStyle == null) m_redStyle = InitRedStyle();
                return m_redStyle;
            }
        }
        static GUIStyle InitRedStyle()
        {
            var style = new GUIStyle();
            style.normal = new GUIStyleState() { textColor = Color.red };
            return style;
        }
    }
}

#endif
