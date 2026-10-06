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
        private int[]? fitStartingTargets;
        private int[]? fitStartingOutputs;
        private static int meshInputRevision;
        private bool estimateScheduled;
        private bool estimateRunning;
        private readonly Dictionary<int, double> finalResponseScales = new();
        private string finalResponseSettings = string.Empty;
        private int finalResponseInputRevision = -1;
        private int outputEditSerial;
        private int pendingOutputIndex = -1;
        private int pendingOutputCount;
        private int outputFeedbackIndex = -1;
        private int outputFeedbackValue;
        private double outputFeedbackUntil;
        private string outputFeedback = string.Empty;
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
            outputEditSerial++;
            pendingOutputIndex = -1;
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
            root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                estimateScheduled = false;
                outputEditSerial++;
                pendingOutputIndex = -1;
            });

            serializedObject.Update();

            root.Bind(serializedObject);
            AddBackgroundCalculationIndicator(root);
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

            void ApplyProtectionPreset(AvatarProtectionLevel level)
            {
                serializedObject.ApplyModifiedProperties();
                var undoLabel = level switch
                {
                    AvatarProtectionLevel.Extreme => Tr("Apply Extreme Preset"),
                    AvatarProtectionLevel.Aggressive => Tr("Apply Aggressive Preset"),
                    _ => Tr("Apply Conservative Defaults to All Meshes"),
                };
                Undo.RecordObject(Target, undoLabel);
                AvatarProtectionPreset.Apply(Target, level);
                EditorUtility.SetDirty(Target);
                serializedObject.Update();
                entriesListView.Rebuild();
            }
            root.Q<Button>("ConservativeDefaultsButton").clicked += () => ApplyProtectionPreset(AvatarProtectionLevel.Conservative);
            root.Q<Button>("AggressiveDefaultsButton").clicked += () => ApplyProtectionPreset(AvatarProtectionLevel.Aggressive);
            root.Q<Button>("ExtremeDefaultsButton").clicked += () => ApplyProtectionPreset(AvatarProtectionLevel.Extreme);

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

            var observedSettings = EditorJsonUtility.ToJson(Target);
            root.RegisterCallback<SerializedPropertyChangeEvent>(changeEvent =>
            {
                // PropertyField forwards label changes from nested drawers as property
                // notifications too. Only changed data makes a measured build stale.
                var currentSettings = EditorJsonUtility.ToJson(Target);
                if (currentSettings == observedSettings) return;
                observedSettings = currentSettings;
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


                // Output is measured during the build. A preview proxy can already
                // have triangles removed by other tools, so it is not the original.
                originalTriangleCountField.tooltip = Tr("Source mesh triangles before preview or build processing.");
                if(TryGetOriginalTriangleCount(entry, false, out var originalTriangleCount))
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
                targetTriangleCountField.isDelayed = true;
                var triangleCountDivider = itemRoot.Q<Label>("TriangleCountDivider");
                var outputSlot = itemRoot.Q<VisualElement>("OutputStatusSlot");
                outputSlot.RegisterCallback<ClickEvent>(evt =>
                {
                    if (itemRoot.userData is not int i || !Target.Entries[i].Enabled) return;
                    UnityEditor.PopupWindow.Show(outputSlot.worldBound, new MeshOutputPopup(this, i));
                    evt.StopPropagation();
                });
                var optionsToggle = itemRoot.Q<Toggle>("OptionsToggle");
                var deformationToggle = itemRoot.Q<Toggle>("DeformationProtectionToggle");
                deformationToggle.Q("unity-checkmark").style.backgroundImage =
                    new StyleBackground((Texture2D)EditorGUIUtility.IconContent("Avatar Icon").image);
                deformationToggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target != deformationToggle || itemRoot.userData is not int itemIndex ||
                        itemIndex < 0 || itemIndex >= Target.Entries.Count) return;
                    CycleMeshProtection(itemIndex);
                    UpdateAlgorithmOptionAvailability(itemRoot);
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
                    QueueOutputEdit(root, itemIndex, value);
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

        private void AddBackgroundCalculationIndicator(VisualElement root)
        {
            var header = root.Q<Foldout>("EstimatesAndBuildDetails").Q<Toggle>();
            var indicator = new VisualElement { name = "BackgroundCalculationIndicator", pickingMode = PickingMode.Ignore };
            indicator.style.flexDirection = FlexDirection.Row;
            indicator.style.alignItems = Align.Center;
            indicator.style.marginLeft = 8;
            indicator.style.flexShrink = 0;
            indicator.style.visibility = Visibility.Hidden;
            var spinner = new Image { name = "BackgroundCalculationSpinner", pickingMode = PickingMode.Ignore };
            spinner.style.width = spinner.style.height = 12;
            spinner.style.marginRight = 3;
            indicator.Add(spinner);
            var label = new Label { name = "BackgroundCalculationLabel", pickingMode = PickingMode.Ignore };
            label.style.fontSize = 11;
            indicator.Add(label);
            // Keep the badge in the foldout header, including when the body is collapsed.
            // Hidden (rather than removed) reserves its width so the heading never jumps.
            header.Add(indicator);
            root.schedule.Execute(() => RefreshBackgroundCalculationIndicator(root)).Every(100);
            RefreshBackgroundCalculationIndicator(root);
        }

        private void RefreshBackgroundCalculationIndicator(VisualElement root)
        {
            var indicator = root.Q<VisualElement>("BackgroundCalculationIndicator");
            if (indicator == null) return;
            var busy = this != null && target != null && !s_analysisInProgress &&
                (pendingOutputIndex >= 0 || estimateScheduled || estimateRunning);
            indicator.style.visibility = busy ? Visibility.Visible : Visibility.Hidden;
            if (!busy) return;
            root.Q<Label>("BackgroundCalculationLabel").text = Tr("Calculating...");
            var frame = (int)(EditorApplication.timeSinceStartup * 10) % 12;
            root.Q<Image>("BackgroundCalculationSpinner").image = EditorGUIUtility.IconContent($"WaitSpin{frame:00}").image;
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
            summary.tooltip = Format("Auto Adjust accepts results up to {0:N0} triangles below this limit to avoid repeated builds for tiny differences.", Math.Max(1, budget / 1000));
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
                        : Format("Within budget · {0:N0} below limit", budget - analysis.TriangleCount);
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
            RefreshBackgroundCalculationIndicator(root);
            summary.EnableInClassList("budget-warning", warning);
            summary.EnableInClassList("budget-error", failed);
            result.EnableInClassList("budget-out-of-date", outOfDate);
            result.style.opacity = outOfDate ? .65f : 1f;
            summary.style.color = failed
                ? new StyleColor(EditorGUIUtility.isProSkin ? new Color(1f, .45f, .4f) : new Color(.7f, .12f, .08f))
                : warning
                    ? new StyleColor(EditorGUIUtility.isProSkin ? new Color(1f, .75f, .3f) : new Color(.55f, .32f, .02f))
                    : new StyleColor(StyleKeyword.Null);

            root.Query<TemplateContainer>().ForEach(RefreshAllocationFields);
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
            var rows = new List<(int index, Renderer renderer, int group, int count, string text)>();
            var currentAllocations = CaptureAllocations();
            for (var index = 0; index < Target.Entries.Count; index++)
            {
                var entry = Target.Entries[index];
                if (!entry.IsValid(Target) || entry.GetTargetRenderer(Target) is not { } renderer ||
                    RendererUtility.GetMesh(renderer) is not { } mesh) continue;
                var hasPreview = MeshiaCascadingAvatarMeshSimplifierPreview.TriangleCountCache.TryGetValue(renderer, out var preview);
                var original = mesh.GetTriangleCount();
                var count = entry.Enabled && hasPreview ? preview.simplified : original;
                var group = !entry.Enabled ? 1 : hasPreview ? 0 : 2;
                var text = !entry.Enabled
                    ? Format("{0}: not being simplified; {1:N0} source triangles.", renderer.name, original)
                    : hasPreview
                        ? Format("{0}: last preview {1:N0}; original {2:N0}.", renderer.name, count, original)
                        : Format("{0}: {1:N0} source triangles; output not measured yet.", renderer.name, original);
                if (entry.Enabled && string.IsNullOrEmpty(analysis.Error) && analysis.Allocations is { } baseline &&
                    index < baseline.Outputs.Length && index < baseline.Counts.Length && baseline.Outputs[index] >= 0)
                {
                    count = baseline.Outputs[index];
                    var fresh = false;
                    if (baseline.Settings == currentAllocations.Settings)
                    {
                        if (HasMeasuredInputs(currentAllocations.Settings) && measuredMeshes!.Meshes.TryGetValue(index, out var response) &&
                            response.TryGetOutput(entry.TargetTriangleCount, out var cached)) { count = cached; fresh = true; }
                        else fresh = analysis.Revision == CurrentAnalysisRevision && baseline.Counts[index] == entry.TargetTriangleCount;
                    }
                    group = 0;
                    text = fresh
                        ? Format("{0}: output {1:N0}; original {2:N0}.", renderer.name, count, original)
                        : Format("{0}: last measured {1:N0}; original {2:N0}. Analyze to update.", renderer.name, count, original);
                }
                if (entry.Fixed) text += Tr(" Fixed for Auto Adjust.");
                rows.Add((index, renderer, group, count, text));
            }

            var ordered = rows.OrderBy(row => row.group).ThenByDescending(row => row.count).ThenBy(row => row.index).ToList();
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
                    container.Add(new Label(Tr(row.group == 0 ? "Meshes being simplified" : row.group == 1 ? "Not being simplified" : "Other meshes"))
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

        private static bool HasFullProtection(MeshiaCascadingAvatarMeshSimplifierRendererEntry entry)
        {
            var skin = entry.Options.SkinningProtection;
            var weights = skin.Policy is SkinningProtectionPolicy.Auto or SkinningProtectionPolicy.AutoDeforming or SkinningProtectionPolicy.On ||
                (skin.Policy == SkinningProtectionPolicy.Legacy && skin.Enabled);
            return weights && (entry.Algorithm != MeshiaCascadingSimplificationAlgorithm.FaQem || skin.PreserveJointTransitions);
        }

        private void CycleMeshProtection(int index)
        {
            if (index < 0 || index >= Target.Entries.Count || !Target.Entries[index].Enabled) return;
            serializedObject.Update();
            var entry = Target.Entries[index];
            var property = EntriesProperty.GetArrayElementAtIndex(index);
            var disabled = property.FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.DisableProtections));
            var skin = property.FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.Options))
                .FindPropertyRelative(nameof(MeshSimplifierOptions.SkinningProtection));
            Undo.SetCurrentGroupName(Tr("Change mesh protection"));
            if (entry.DisableProtections || HasFullProtection(entry))
            {
                // Gray -> green restores saved geometry settings and enables deformation
                // protection. Green -> yellow keeps geometry guards but disables deformation.
                var enable = entry.DisableProtections;
                disabled.boolValue = false;
                skin.FindPropertyRelative(nameof(SkinningProtectionOptions.Policy)).intValue =
                    (int)(enable ? SkinningProtectionPolicy.On : SkinningProtectionPolicy.Off);
                skin.FindPropertyRelative(nameof(SkinningProtectionOptions.Enabled)).boolValue = enable;
                skin.FindPropertyRelative(nameof(SkinningProtectionOptions.PreserveJointTransitions)).boolValue = enable;
            }
            else disabled.boolValue = true;
            serializedObject.ApplyModifiedProperties();
        }

        private void RefreshDeformationProtection(VisualElement itemRoot)
        {
            var toggle = itemRoot.Q<Toggle>("DeformationProtectionToggle");
            if (toggle == null || target == null || itemRoot.userData is not int index ||
                index < 0 || index >= Target.Entries.Count) return;
            var entry = Target.Entries[index];
            var applicable = entry.GetTargetRenderer(Target) is { } renderer && RendererUtility.GetMesh(renderer) != null;
            toggle.style.display = DisplayStyle.Flex;
            toggle.SetEnabled(applicable && entry.Enabled);
            var full = !entry.DisableProtections && HasFullProtection(entry);
            toggle.SetValueWithoutNotify(!entry.DisableProtections);
            toggle.EnableInClassList("partial-protection", !entry.DisableProtections && !full);
            toggle.EnableInClassList("no-protection", entry.DisableProtections);
            toggle.tooltip = !applicable ? Tr("Protection: not applicable") :
                !entry.Enabled ? Tr("Protection: mesh excluded") :
                entry.DisableProtections ? Tr("No protection. Shape, seams and deformation may break. Basic mesh validity checks still apply.") :
                full ? Tr("Protection: geometry and deformation") : Tr("Protection: geometry only or partial");
            if (applicable && entry.Enabled)
            {
                toggle.tooltip += "\n" + (entry.DisableProtections ? Tr("Click to restore geometry and deformation protection.") :
                    full ? Tr("Click to keep geometry protection only.") : Tr("Click to turn off all protections for this mesh."));
                if (!entry.DisableProtections && entry.Options.SkinningProtection.Policy is SkinningProtectionPolicy.Auto or SkinningProtectionPolicy.AutoDeforming)
                    toggle.tooltip += "\n" + Tr("Automatic bone-weight selection is enabled.");
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
            var protectionsEnabled = !Target.Entries[itemIndex].DisableProtections;
            optionsField.SetEnabled(protectionsEnabled);
            itemRoot.Q<Foldout>("PreserveJointTransitionsBonesFoldout").SetEnabled(protectionsEnabled);
            preserveBorderEdgesBonesFoldout.SetEnabled(supportsSelectedBorderBones && protectionsEnabled);
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
            var missing = changed.Where(i => !measuredMeshes!.Meshes[i].TryGetOutput(current.Counts[i], out _)).ToArray();
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
            var entry = Target.Entries[index];
            var count = entry.TargetTriangleCount;
            var slider = itemRoot.Q<SliderInt>("TargetTriangleCountSlider");
            var field = itemRoot.Q<IntegerField>("TargetTriangleCountField");
            var unknown = itemRoot.Q<TextField>("UnknownOutputField");
            var hint = itemRoot.Q<Label>("MeasuredOutputHint");
            var output = -1;
            var measured = false;
            var canEdit = false;
            var pending = pendingOutputIndex == index;
            var tooltip = Tr("Analyze Build to measure this mesh before editing its output.");
            if (hint != null)
            {
                hint.style.display = DisplayStyle.None;
                hint.text = string.Empty;
                SetMarkerColor(hint, EditorGUIUtility.isProSkin ? new Color(.35f, .68f, 1f) : new Color(.08f, .35f, .65f));
            }
            if (entry.Enabled && TryGetBuildAnalysisResult(Target, out var result) &&
                string.IsNullOrEmpty(result.Error) && result.Allocations is { } baseline &&
                index < baseline.Outputs.Length && index < baseline.Counts.Length && baseline.Outputs[index] >= 0)
            {
                // Never replace stale output with a raw simplifier request.
                output = baseline.Outputs[index];
                var current = CaptureAllocations();
                var compatible = baseline.Settings == current.Settings;
                var hasInputs = compatible && HasMeasuredInputs(current.Settings);
                if (hasInputs && measuredMeshes!.Meshes.TryGetValue(index, out var mesh))
                {
                    canEdit = true;
                    if (mesh.TryGetOutput(count, out var cached)) { output = cached; measured = true; }
                }
                if (!measured && compatible && result.Revision == CurrentAnalysisRevision && baseline.Counts[index] == count)
                    measured = true;
                pending |= hasInputs && count != baseline.Counts[index] && !measured &&
                    failedEstimate != string.Join(",", current.Counts);
                tooltip = measured
                    ? Format("Output: {0:N0}\nBefore other build tools run.", output)
                    : Tr("This measurement is out of date. Analyze Build to update it.");
                if (measured && output != baseline.Outputs[index])
                    tooltip += "\n" + Format("Change since last build: {0:N0} → {1:N0}", baseline.Outputs[index], output);
                if (measured && !canEdit)
                    tooltip += "\n" + Tr("Analyze Build to refresh measurements before editing output.");
            }
            var feedback = measured && !pending && outputFeedbackIndex == index && outputFeedbackValue == output &&
                EditorApplication.timeSinceStartup < outputFeedbackUntil;
            if (entry.Enabled && hint != null && (pending || !measured || feedback))
            {
                hint.style.display = DisplayStyle.Flex;
                hint.text = pending ? "…" : feedback ? "!" : "?";
                if (pending) tooltip = Tr("Measuring the requested output. The count will update when ready.");
                else if (feedback)
                {
                    tooltip += "\n" + outputFeedback;
                    SetMarkerColor(hint, EditorGUIUtility.isProSkin ? new Color(1f, .72f, .3f) : new Color(.6f, .32f, .02f));
                }
                else SetMarkerColor(hint, EditorGUIUtility.isProSkin ? new Color(.7f, .7f, .7f) : new Color(.4f, .4f, .4f));
            }
            var displayed = pendingOutputIndex == index ? pendingOutputCount : output;
            if (displayed >= 0)
            {
                slider?.SetValueWithoutNotify(displayed);
                if (field != null && field.value != displayed) field.SetValueWithoutNotify(displayed);
            }
            slider?.SetEnabled(entry.Enabled && canEdit && !s_analysisInProgress);
            field?.SetEnabled(entry.Enabled && canEdit && !s_analysisInProgress);
            if (field != null) field.style.display = entry.Enabled && displayed >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (unknown != null) unknown.style.display = entry.Enabled && displayed < 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (entry.Enabled) tooltip += "\n" + Tr("Click the separator for output details and fine-tuning.");
            var slot = itemRoot.Q<VisualElement>("OutputStatusSlot");
            if (slot != null) slot.tooltip = tooltip;
            if (slider != null) slider.tooltip = tooltip;
            if (field != null) field.tooltip = tooltip;
            if (unknown != null) unknown.tooltip = tooltip;
            if (hint != null) hint.tooltip = tooltip;
            var divider = itemRoot.Q<Label>("TriangleCountDivider");
            if (divider != null)
                divider.style.display = hint != null && hint.style.display.value == DisplayStyle.Flex
                    ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void QueueOutputEdit(VisualElement root, int index, int desired)
        {
            var current = CaptureAllocations();
            if (s_analysisInProgress || !HasMeasuredInputs(current.Settings) ||
                !measuredMeshes!.Meshes.TryGetValue(index, out var mesh) || !Target.Entries[index].Enabled) return;
            var serial = ++outputEditSerial;
            pendingOutputIndex = index;
            pendingOutputCount = Math.Max(0, Math.Min(mesh.SourceCount, desired));
            RefreshBackgroundCalculationIndicator(root);
            outputFeedbackIndex = -1;
            root.Query<TemplateContainer>().ForEach(RefreshAllocationFields);
            void StartWhenIdle()
            {
                if (this == null || target == null || serial != outputEditSerial || root.panel == null) return;
                if (estimateRunning)
                {
                    root.schedule.Execute(StartWhenIdle).StartingIn(100);
                    return;
                }
                _ = ApplyOutputEditAsync(root, index, pendingOutputCount, serial);
            }
            root.schedule.Execute(StartWhenIdle).StartingIn(250);
        }

        private async System.Threading.Tasks.Task ApplyOutputEditAsync(VisualElement root, int index, int desired, int serial)
        {
            var starting = CaptureAllocations();
            var inputs = measuredMeshes;
            var budget = Target.TargetTriangleCount;
            var reserve = Target.BuildTriangleReserve;
            var autoAdjust = Target.AutoAdjustEnabled;
            bool Current() => this != null && target != null && serial == outputEditSerial && !s_analysisInProgress &&
                inputs != null && inputs == measuredMeshes && HasMeasuredInputs(starting.Settings) &&
                Target.TargetTriangleCount == budget && Target.BuildTriangleReserve == reserve && Target.AutoAdjustEnabled == autoAdjust &&
                CaptureAllocations().Settings == starting.Settings && CaptureAllocations().Counts.SequenceEqual(starting.Counts);
            if (!Current() || !inputs!.Meshes.TryGetValue(index, out var mesh))
            {
                if (serial == outputEditSerial) pendingOutputIndex = -1;
                return;
            }
            estimateRunning = true;
            try
            {
                var previous = Target.Entries[index].TargetTriangleCount;
                var candidate = await MeasuredMeshBudget.FindOutputTargetAsync(mesh, previous, desired, Current);
                if (!Current()) return;
                var produced = mesh.Outputs[candidate];
                if (candidate != previous) SetManualAllocation(index, candidate);
                if (produced != desired)
                {
                    outputFeedbackIndex = index;
                    outputFeedbackValue = produced;
                    outputFeedbackUntil = EditorApplication.timeSinceStartup + 8;
                    outputFeedback = produced == mesh.Outputs[previous]
                        ? Tr("No output change found at these settings.")
                        : Format("Requested {0:N0}; achieved {1:N0} with the current settings.", desired, produced);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                Debug.LogException(exception, Target);
            }
            finally
            {
                estimateRunning = false;
                if (serial == outputEditSerial) pendingOutputIndex = -1;
                if (this != null && target != null && root.panel != null && !s_analysisInProgress)
                    RefreshBudgetGuidance(root);
            }
        }

        private static void SetMarkerColor(Label hint, Color color)
        {
            hint.style.color = color;
            hint.style.borderLeftColor = hint.style.borderRightColor = color;
            hint.style.borderTopColor = hint.style.borderBottomColor = color;
        }

        private sealed class MeshOutputPopup : PopupWindowContent
        {
            private readonly MeshiaCascadingAvatarMeshSimplifierEditor owner;
            private readonly int index;
            private readonly AllocationSnapshot starting;
            private readonly MeasuredMeshSet? inputs;
            private readonly int startingBudget;
            private readonly int startingReserve;
            private int reduction = 1;
            private int candidate = -1;
            private string message = string.Empty;
            private bool running;
            private bool closed;
            private bool showDetails;

            internal MeshOutputPopup(MeshiaCascadingAvatarMeshSimplifierEditor owner, int index)
            {
                this.owner = owner;
                this.index = index;
                starting = owner.CaptureAllocations();
                inputs = owner.measuredMeshes;
                startingBudget = owner.Target.TargetTriangleCount;
                startingReserve = owner.Target.BuildTriangleReserve;
                if (TryGetBuildAnalysisResult(owner.Target, out var analysis) &&
                    analysis.Allocations is { } baseline && baseline.Settings == starting.Settings &&
                    baseline.Counts.Length == starting.Counts.Length && baseline.Outputs.Length == starting.Counts.Length &&
                    inputs != null && owner.HasMeasuredInputs(starting.Settings))
                {
                    long projected = analysis.TriangleCount;
                    var known = true;
                    for (var i = 0; i < starting.Counts.Length; i++)
                    {
                        if (starting.Counts[i] == baseline.Counts[i]) continue;
                        if (baseline.Outputs[i] < 0 || !inputs.Meshes.TryGetValue(i, out var mesh) ||
                            !mesh.TryGetOutput(starting.Counts[i], out var output)) { known = false; break; }
                        projected += (long)output - baseline.Outputs[i];
                    }
                    if (known) reduction = (int)Math.Min(int.MaxValue, Math.Max(1L, projected - startingBudget));
                }
            }

            private bool Current() => !closed && owner != null && owner.target != null && !s_analysisInProgress &&
                inputs != null && inputs == owner.measuredMeshes && owner.HasMeasuredInputs(starting.Settings) &&
                owner.Target.TargetTriangleCount == startingBudget && owner.Target.BuildTriangleReserve == startingReserve &&
                owner.CaptureAllocations().Settings == starting.Settings &&
                owner.CaptureAllocations().Counts.SequenceEqual(starting.Counts);

            public override Vector2 GetWindowSize() => new(370, 310);
            public override void OnClose() => closed = true;

            public override void OnGUI(Rect rect)
            {
                if (owner == null || owner.target == null) return;
                var entry = owner.Target.Entries[index];
                GUILayout.Label(entry.GetTargetRenderer(owner.Target)?.name ?? Tr("Mesh output"), EditorStyles.boldLabel);
                if (!Current() || !inputs!.Meshes.TryGetValue(index, out var mesh))
                {
                    EditorGUILayout.HelpBox(Tr("Analyze Build to refresh this mesh's measurements, then reopen this panel."), MessageType.Info);
                    return;
                }
                var targetCount = starting.Counts[index];
                if (!mesh.TryGetOutput(targetCount, out var output))
                {
                    GUILayout.Label(Tr("Updating the output for this target..."));
                    return;
                }
                GUILayout.Label(Format("Output: {0:N0}", output));
                if (TryGetBuildAnalysisResult(owner.Target, out var analysis) && analysis.Allocations is { } baseline &&
                    index < baseline.Outputs.Length && baseline.Outputs[index] >= 0)
                    GUILayout.Label(Format("Change since last build: {0:N0} → {1:N0}", baseline.Outputs[index], output));
                EditorGUILayout.HelpBox(Tr("Before other build tools run. Analyze Build verifies the avatar total."), MessageType.None);
                using (new EditorGUI.DisabledScope(running || owner.estimateRunning || entry.Fixed))
                {
                    var next = Math.Max(1, EditorGUILayout.IntField(Tr("Reduce output by"), reduction));
                    if (next != reduction) { reduction = next; candidate = -1; message = string.Empty; }
                    if (GUILayout.Button(Tr("Measure reduction"))) _ = Measure(mesh, targetCount, output);
                }
                if (entry.Fixed) GUILayout.Label(Tr("Unlock this mesh to reduce its output."), EditorStyles.wordWrappedLabel);
                GUILayout.Label(running ? Tr("Measuring mesh responses...") : message, EditorStyles.wordWrappedLabel);
                using (new EditorGUI.DisabledScope(running || candidate < 0 || !Current() || entry.Fixed))
                {
                    if (GUILayout.Button(Tr("Apply reduction")))
                    {
                        owner.ApplyMeshReduction(index, candidate);
                        editorWindow.Close();
                    }
                }
                GUILayout.Label(Tr("Keeps this saving instead of redistributing it. Undo restores the previous targets."), EditorStyles.wordWrappedMiniLabel);
                showDetails = EditorGUILayout.Foldout(showDetails, Tr("Calculation details"));
                if (showDetails) GUILayout.Label(Format("Internal simplifier target: {0:N0}", targetCount));
            }

            private async System.Threading.Tasks.Task Measure(MeasuredMeshResponse mesh, int targetCount, int output)
            {
                running = owner.estimateRunning = true;
                candidate = -1;
                try
                {
                    var found = await MeasuredMeshBudget.FindReductionAsync(mesh, targetCount, reduction, Current);
                    if (!Current()) return;
                    var saving = output - mesh.Outputs[found];
                    if (saving > 0)
                    {
                        candidate = found;
                        message = Format("{0:N0} fewer triangles · output {1:N0}", saving, mesh.Outputs[found]);
                    }
                    else message = Tr("No suitable reduction found with these settings. Your target is unchanged.");
                }
                catch (OperationCanceledException) { }
                catch (Exception exception)
                {
                    message = Tr("Measurement failed. Your target is unchanged.");
                    Debug.LogException(exception);
                }
                finally
                {
                    running = false;
                    if (owner != null) owner.estimateRunning = false;
                    if (!closed && editorWindow != null) editorWindow.Repaint();
                }
            }
        }

        private void ApplyMeshReduction(int index, int value)
        {
            // This explicit output-saving action keeps other targets unchanged. Ordinary
            // sliders still rebalance; reserve the allocation change so later edits retain it.
            if (index < 0 || index >= Target.Entries.Count) return;
            var entry = Target.Entries[index];
            var previous = entry.TargetTriangleCount;
            if (!entry.Enabled || entry.Fixed || value < 1 || value >= previous) return;
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(Target, Tr("Apply reduction"));
            Target.Entries[index].TargetTriangleCount = value;
            Target.BuildTriangleReserve = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue,
                (long)Target.BuildTriangleReserve + previous - value));
            EditorUtility.SetDirty(Target);
            Undo.FlushUndoRecordObjects();
            serializedObject.Update();
            InvalidateTriangleAnalysis();
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
            Func<int, bool> cancelBeforeBuild, Func<int?> measure, Func<bool> correct, Action? rollback = null,
            int? initialEstimate = null)
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
                if (autoAdjust && initialEstimate.HasValue &&
                    (initialEstimate.Value > budget || (long)budget - initialEstimate.Value > Math.Max(1, budget / 1000)))
                {
                    // Planning may use the last build plus measured mesh changes.
                    // It never counts as verification, including when no safe
                    // correction is found. Always run the complete pipeline next.
                    attemptedCorrection = true;
                    correct();
                }
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
            fitStartingTargets = startingAllocations;
            fitStartingOutputs = null;
            BuildAnalysisResult? startingAnalysis = null;
            var startingAnalysisVerified = false;
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
                        baseline.EstimatedBeforeDownstreamTriangleCount,
                        startingAnalysisVerified ? CurrentAnalysisRevision : baseline.Revision, baseline.Error, baseline.Allocations));
            }
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            s_analysisInProgress = true;
            NDMFPreview.DisablePreviewDepth = previousDisablePreviewDepth + 1;
            button.SetEnabled(false);
            button.text = Tr("Analyzing...");
            try
            {
                // Keep a genuine baseline for rollback. A projected count must
                // never be stored or restored as a verified build result.
                var prepared = PrepareBuildEstimate();
                if (prepared.HasValue)
                {
                    if (TryGetBuildAnalysisResult(Target, out var previous))
                    {
                        startingAnalysis = previous;
                        startingAnalysisVerified = previous.Revision == CurrentAnalysisRevision &&
                            previous.Allocations is { } snapshot && snapshot.Counts.SequenceEqual(CaptureAllocations().Counts);
                    }
                    retainedStartingMeshes = measuredMeshes;
                    fitStartingOutputs = prepared.Value.Allocations?.Outputs;
                }
                allocatedBeforeBuild = GetTotalSimplifiedTriangleCount(false);
                var stop = RunBoundedBuildFit(autoAdjust, Target.TargetTriangleCount,
                    pass => EditorUtility.DisplayCancelableProgressBar("Meshia",
                        Format("Analyzing build {0} of {1}. Cancel stops before the next build.", pass + 1, maxBuilds),
                        (float)pass / maxBuilds),
                    () =>
                    {
                        lastAnalysisReduction = 0;
                        allocatedBeforeBuild = GetTotalSimplifiedTriangleCount(false);
                        prepared = null;
                        var analysis = MeasureNdmfBuild(avatarRoot.gameObject);
                        StoreBuildAnalysisResult(Target, analysis);
                        if (completed == 0 && string.IsNullOrEmpty(analysis.Error) &&
                            Target.Entries.Select(entry => entry.TargetTriangleCount).SequenceEqual(startingAllocations) &&
                            Target.BuildTriangleReserve == startingReserve)
                        {
                            startingAnalysis = analysis;
                            startingAnalysisVerified = true;
                            fitStartingOutputs = analysis.Allocations?.Outputs;
                            if (retainedStartingMeshes != measuredMeshes) retainedStartingMeshes?.Dispose();
                            retainedStartingMeshes = measuredMeshes;
                        }
                        completed++;
                        return string.IsNullOrEmpty(analysis.Error) ? (int?)analysis.TriangleCount : null;
                    },
                    () => ApplyAnalyzedBudgetCorrection(allocatedBeforeBuild, prepared),
                    RestoreStartingAllocations, prepared?.TriangleCount);
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
                fitStartingTargets = fitStartingOutputs = null;
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
            var previousInputsValid = HasMeasuredInputs(allocations.Settings);
            EnsureFinalResponseSettings(allocations.Settings);
            allocations.Outputs = Enumerable.Repeat(-1, Target.Entries.Count).ToArray();
            var captured = new MeasuredMeshSet { Settings = allocations.Settings, InputRevision = meshInputRevision };
            GameObject? clone = null;
            MeshiaCascadingAvatarMeshSimplifier? cloneComponent = null;
            void CaptureMesh(GameObject root, Renderer renderer, Mesh source, MeshSimplificationTarget targetValue,
                MeshSimplifierOptions options, System.Collections.BitArray? preserve, Mesh output, FaQemCountProfile? profile)
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
                }, profile,
                    targetValue.Kind == MeshSimplificationTargetKind.FaQemTriangleCount
                        ? () => MeshSimplifier.MeasureFaQemCounts(sourceCopy, 0, options, bones) : null,
                    targetValue.Kind == MeshSimplificationTargetKind.FaQemTriangleCount
                        ? () => MeshSimplifier.MeasureFaQemCountsAsync(sourceCopy, 0, options, bones) : null);
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
            if (previousInputsValid && string.IsNullOrEmpty(error) && TryGetBuildAnalysisResult(Target, out var previous) &&
                string.IsNullOrEmpty(previous.Error) && previous.Allocations is { } before && before.Settings == allocations.Settings)
            {
                if (MeasuredMeshBudget.TryLearnFinalScale(before.Counts, allocations.Counts, before.Outputs, allocations.Outputs,
                    previous.TriangleCount, finalTriangleCount, out var index, out var scale))
                    finalResponseScales[index] = scale;
                else if (index >= 0) finalResponseScales.Remove(index);
            }
            return new BuildAnalysisResult(finalTriangleCount, estimate, CurrentAnalysisRevision, error, allocations);
        }

        private void EnsureFinalResponseSettings(string settings)
        {
            if (finalResponseInputRevision == meshInputRevision && finalResponseSettings == settings) return;
            finalResponseScales.Clear();
            finalResponseSettings = settings;
            finalResponseInputRevision = meshInputRevision;
        }

        private BuildAnalysisResult? PrepareBuildEstimate()
        {
            if (!Target.AutoAdjustEnabled || !TryGetBuildAnalysisResult(Target, out var baseline) ||
                !string.IsNullOrEmpty(baseline.Error) || baseline.Allocations is not { } previous) return null;
            var current = CaptureAllocations();
            if (!HasMeasuredInputs(current.Settings) || previous.Settings != current.Settings ||
                previous.Counts.Length != current.Counts.Length || previous.Outputs.Length != current.Counts.Length) return null;
            EnsureFinalResponseSettings(current.Settings);
            current.Outputs = (int[])previous.Outputs.Clone();
            long projected = baseline.TriangleCount;
            for (var i = 0; i < current.Counts.Length; i++)
            {
                if (current.Counts[i] == previous.Counts[i]) continue;
                if (previous.Outputs[i] < 0 || !measuredMeshes!.Meshes.TryGetValue(i, out var mesh)) return null;
                if (EditorUtility.DisplayCancelableProgressBar("Meshia", Tr("Measuring mesh responses..."),
                    i / (float)Math.Max(1, current.Counts.Length))) throw new OperationCanceledException();
                current.Outputs[i] = mesh.Measure(current.Counts[i]);
                var scale = finalResponseScales.TryGetValue(i, out var observed) ? observed : 1;
                projected += (long)Math.Round(((long)current.Outputs[i] - previous.Outputs[i]) * scale);
            }
            return new BuildAnalysisResult((int)Math.Max(0, Math.Min(int.MaxValue, projected)),
                baseline.EstimatedBeforeDownstreamTriangleCount, CurrentAnalysisRevision, null, current);
        }

        private bool ApplyAnalyzedBudgetCorrection(int analyzedAllocation, BuildAnalysisResult? prepared = null)
        {
            var hasAnalysis = TryGetBuildAnalysisResult(Target, out var analysis);
            if (prepared.HasValue) { analysis = prepared.Value; hasAnalysis = true; }
            if (!Target.AutoAdjustEnabled || !hasAnalysis ||
                analysis.Revision != CurrentAnalysisRevision || !string.IsNullOrEmpty(analysis.Error) ||
                GetTotalSimplifiedTriangleCount(false) != analyzedAllocation ||
                !HasMeasuredInputs(CaptureAllocations().Settings)) return false;
            var tolerance = Math.Max(1, Target.TargetTriangleCount / 1000);
            var difference = (long)Target.TargetTriangleCount - analysis.TriangleCount;
            if (difference >= 0 && difference <= tolerance) return false;
            // Leave room for indivisible collapses in either direction. A plan
            // landing two triangles over must not cost another whole build.
            difference -= tolerance / 2;
            var candidates = measuredMeshes!.Meshes.Values.Where(mesh =>
                mesh.Index < Target.Entries.Count && Target.Entries[mesh.Index].Enabled && !Target.Entries[mesh.Index].Fixed).ToArray();
            var targets = Target.Entries.Select(entry => entry.TargetTriangleCount).ToArray();
            if (candidates.Length == 0) return false;
            EnsureFinalResponseSettings(CaptureAllocations().Settings);
            var plan = MeasuredMeshBudget.Plan(candidates, targets, difference,
                () => EditorUtility.DisplayCancelableProgressBar("Meshia", Tr("Measuring mesh responses..."), .5f),
                fitStartingTargets, fitStartingOutputs, finalResponseScales);
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
