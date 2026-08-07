#nullable enable
#if ENABLE_MODULAR_AVATAR

using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Meshia.MeshSimplification.Editor;
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
        private readonly struct BuildAnalysisResult
        {
            internal readonly int TriangleCount;
            internal readonly int EstimatedBeforeDownstreamTriangleCount;
            internal readonly int Revision;
            internal readonly string? Error;

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
            }
        }

        [Serializable]
        private sealed class SerializedBuildAnalysisResult
        {
            public int TriangleCount;
            public int EstimatedBeforeDownstreamTriangleCount;
            public int Revision;
            public string? Error;
        }

        private const string AnalysisRevisionSessionKey =
            "Meshia.MeshSimplification.CascadingTriangleAnalysis.Revision";
        private const string AnalysisResultSessionKeyPrefix =
            "Meshia.MeshSimplification.CascadingTriangleAnalysis.Result.";

        private static readonly Dictionary<string, BuildAnalysisResult> BuildAnalysisCache = new();
        private static bool s_analysisInProgress;
        private static int CurrentAnalysisRevision => SessionState.GetInt(AnalysisRevisionSessionKey, 0);

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
            Undo.undoRedoPerformed -= InvalidateTriangleAnalysis;
            Undo.undoRedoPerformed += InvalidateTriangleAnalysis;
        }

        private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            InvalidateTriangleAnalysis();
            return modifications;
        }

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
            Undo.RegisterCreatedObjectUndo(go, "Create Meshia Cascading Avatar Mesh Simplifier");
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
            Undo.RecordObject(Target, "Get entries");
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
                return new HelpBox("Meshia cascading inspector UI assets could not be loaded.", HelpBoxMessageType.Error);
            }

            VisualElement root = new();
            editorVisualTreeAsset.CloneTree(root);

            serializedObject.Update();
            
            root.Bind(serializedObject);
            var attachedToRootWarning = root.Q<HelpBox>("AttachedToRootWarning");
            var mainElement = root.Q<VisualElement>("MainElement");
            var targetTriangleCountField = root.Q<IntegerField>("TargetTriangleCountField");
            var targetTriangleCountPresetDropdownField = root.Q<DropdownField>("TargetTriangleCountPresetDropdownField");
            var adjustButton = root.Q<Button>("AdjustButton");
            var autoAdjustEnabledToggle = root.Q<Toggle>("AutoAdjustEnabledToggle");
            var triangleCountLabel = root.Q<IMGUIContainer>("TriangleCountLabel");
            var analyzeNdmfBuildButton = root.Q<Button>("AnalyzeNdmfBuildButton");

            var removeInvalidEntriesButton = root.Q<Button>("RemoveInvalidEntriesButton");
            var resetButton = root.Q<Button>("ResetButton");
            var entriesListView = root.Q<ListView>("EntriesListView");
            var ndmfPreviewToggle = root.Q<Toggle>("NdmfPreviewToggle");

            attachedToRootWarning.style.display = Target.transform.parent == null ? DisplayStyle.Flex : DisplayStyle.None;

            root.RegisterCallback<SerializedPropertyChangeEvent>(changeEvent =>
            {
                if (changeEvent.changedProperty.propertyPath !=
                    nameof(MeshiaCascadingAvatarMeshSimplifier.AutoAdjustEnabled))
                {
                    InvalidateTriangleAnalysis();
                }
            });


            targetTriangleCountField.RegisterValueChangedCallback(changeEvent =>
            {
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


            triangleCountLabel.onGUIHandler = () =>
            {
                var current = GetTotalSimplifiedTriangleCount(true);
                var sum = GetTotalOriginalTriangleCount();
                var targetCount = TargetTriangleCountProperty.intValue;
                EditorGUILayout.LabelField($"Meshia output (before downstream tools): {current:N0} / {sum:N0}");

                if (DownstreamTriangleEstimator.IsAaoAvailable)
                {
                    var estimatedFinal = GetTotalEstimatedFinalTriangleCount(true);
                    if (TryGetAnalyzedCalibration(Target, out var calibration, out var calibrationStale))
                    {
                        EditorGUILayout.LabelField($"AAO estimate: {estimatedFinal:N0} / {targetCount:N0}");
                        var calibratedFinal = DownstreamTriangleEstimator.ApplyAnalyzedDelta(
                            estimatedFinal,
                            calibration.EstimatedBeforeDownstreamTriangleCount,
                            calibration.TriangleCount);
                        var calibratedOverflow = targetCount < calibratedFinal;
                        var calibratedLabel = $"Calibrated estimate: {calibratedFinal:N0} / {targetCount:N0}";
                        if (calibratedOverflow)
                        {
                            calibratedLabel += " - Potential overflow";
                        }
                        if (calibrationStale)
                        {
                            calibratedLabel += " - Stale calibration";
                        }
                        EditorGUILayout.LabelField(
                            calibratedLabel,
                            calibratedOverflow ? GUIStyleHelper.RedStyle : EditorStyles.label);
                    }
                    else
                    {
                        var estimateOverflow = targetCount < estimatedFinal;
                        var estimateLabel = $"AAO estimate: {estimatedFinal:N0} / {targetCount:N0}";
                        if (estimateOverflow)
                        {
                            estimateLabel += " - Potential overflow";
                        }
                        EditorGUILayout.LabelField(
                            estimateLabel,
                            estimateOverflow ? GUIStyleHelper.RedStyle : EditorStyles.label);
                        EditorGUILayout.LabelField(
                            "Run Analyze NDMF Build once to calibrate Auto Adjust for downstream changes.");
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("AAO estimate unavailable; use Analyze NDMF Build for an exact count.");
                }

                if (TryGetBuildAnalysisResult(Target, out var analysis))
                {
                    var stale = analysis.Revision != CurrentAnalysisRevision;
                    if (!string.IsNullOrEmpty(analysis.Error))
                    {
                        if (analysis.TriangleCount > 0)
                        {
                            var warningLabel =
                                $"Analyzed NDMF build: {analysis.TriangleCount:N0} / {targetCount:N0} - {analysis.Error}";
                            if (stale)
                            {
                                warningLabel += " - Stale";
                            }
                            EditorGUILayout.LabelField(
                                warningLabel);
                        }
                        else
                        {
                            EditorGUILayout.LabelField($"NDMF analysis failed: {analysis.Error}", GUIStyleHelper.RedStyle);
                        }
                    }
                    else
                    {
                        var exactOverflow = !stale && targetCount < analysis.TriangleCount;
                        var exactLabel = $"Analyzed NDMF build: {analysis.TriangleCount:N0} / {targetCount:N0}";
                        if (stale)
                        {
                            exactLabel += " - Stale";
                        }
                        else if (exactOverflow)
                        {
                            exactLabel += " - Overflow!";
                        }
                        EditorGUILayout.LabelField(exactLabel, exactOverflow ? GUIStyleHelper.RedStyle : EditorStyles.label);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("Analyzed NDMF build: not run");
                }
            };
            analyzeNdmfBuildButton.clicked += () => AnalyzeNdmfBuild(analyzeNdmfBuildButton);
            removeInvalidEntriesButton.clicked += () =>
            {
                var target = Target;
                var entries = target.Entries;

                Undo.RecordObject(target, "Remove Invalid Entries");
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
                var originalTriangleCount = GetTotalEstimatedOriginalTriangleCount();
                var resetTargetTriangleCount = TargetTriangleCountProperty.intValue;
                if (TryGetAnalyzedCalibration(Target, out var calibration, out _))
                {
                    resetTargetTriangleCount = DownstreamTriangleEstimator.GetPreDownstreamTarget(
                        resetTargetTriangleCount,
                        calibration.EstimatedBeforeDownstreamTriangleCount,
                        calibration.TriangleCount);
                }

                var quality = originalTriangleCount > 0
                    ? resetTargetTriangleCount / (float)originalTriangleCount
                    : 1f;

                var entriesProperty = EntriesProperty;
                var arraySize = entriesProperty.arraySize;
                for (int i = 0; i < arraySize; i++)
                {
                    var entryProperty = entriesProperty.GetArrayElementAtIndex(i);
                    entryProperty.FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.Enabled)).boolValue = true;
                    entryProperty.FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.Fixed)).boolValue = false;
                }

                SetQualityAll(quality);
                serializedObject.ApplyModifiedProperties();
            };
            entriesListView.bindItem = (itemElement, index) =>
            {
                var entry = Target.Entries[index];
                var entryProperty = EntriesProperty.GetArrayElementAtIndex(index);
                var itemRoot = (TemplateContainer)itemElement;
                var targetObjectField = itemRoot.Q<ObjectField>("TargetObjectField");
                var targetPathField = itemRoot.Q<TextField>("TargetPathField");
                var targetTriangleCountSlider = itemRoot.Q<SliderInt>("TargetTriangleCountSlider");
                var targetTriangleCountField = itemRoot.Q<IntegerField>("TargetTriangleCountField");
                var originalTriangleCountField = itemRoot.Q<IntegerField>("OriginalTriangleCountField");
                var unknownOriginalTriangleCountField = itemRoot.Q<TextField>("UnknownOriginalTriangleCountField");
                var preserveBorderEdgesBonesFoldout = itemRoot.Q<Foldout>("PreserveBorderEdgesBonesFoldout");
                var previewUvsButton = itemRoot.Q<Button>("PreviewUvsButton");
                itemRoot.BindProperty(entryProperty);
                itemRoot.userData = index;
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
                var algorithmField = itemRoot.Q<PropertyField>("AlgorithmField");
                var optionsField = itemRoot.Q<PropertyField>("OptionsField");
                var preserveBorderEdgesBonesFoldout = itemRoot.Q<Foldout>("PreserveBorderEdgesBonesFoldout");
                var previewUvsButton = itemRoot.Q<Button>("PreviewUvsButton");
                HelpBox blenderOptionsHelpBox = new(
                    "Meshia options are not used by the Blender Decimate algorithm.",
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

                targetTriangleCountSlider.RegisterValueChangedCallback(changeEvent =>
                {
                    if (itemRoot.userData is int itemIndex && AutoAdjustEnabledProperty.boolValue)
                    {
                        AdjustQuality(itemIndex);
                        serializedObject.ApplyModifiedProperties();
                    }
                });

                optionsToggle.RegisterValueChangedCallback(changeEvent =>
                {
                    algorithmField.style.display = previewUvsButton.style.display = optionsField.style.display = preserveBorderEdgesBonesFoldout.style.display =
                        changeEvent.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                    UpdateAlgorithmOptionAvailability(itemRoot);
                });

                previewUvsButton.clicked += () => PreviewUvs(itemRoot);

                algorithmField.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
                {
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

            IVisualElementScheduledItem? scheduledUvPreviewRefresh = null;
            root.TrackSerializedObjectValue(serializedObject, _ =>
            {
                scheduledUvPreviewRefresh?.Pause();
                scheduledUvPreviewRefresh = root.schedule.Execute(RefreshOpenUvPreview).StartingIn(150);
            });


            return root;
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
                    entry.Options,
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
            var usesMeshiaOptions = !usesBlenderDecimate && !usesUvLoopDissolve;

            var optionsField = itemRoot.Q<PropertyField>("OptionsField");
            var preserveBorderEdgesBonesFoldout = itemRoot.Q<Foldout>("PreserveBorderEdgesBonesFoldout");
            var blenderOptionsHelpBox = itemRoot.Q<HelpBox>("BlenderOptionsHelpBox");
            var uvLoopDissolveHelpBox = itemRoot.Q<HelpBox>("UvLoopDissolveHelpBox");
            var optionsToggle = itemRoot.Q<Toggle>("OptionsToggle");

            optionsField.SetEnabled(usesMeshiaOptions);
            preserveBorderEdgesBonesFoldout.SetEnabled(usesMeshiaOptions);
            optionsField.tooltip = preserveBorderEdgesBonesFoldout.tooltip = !usesMeshiaOptions
                ? "Not supported by this algorithm. Select Meshia to use these options."
                : string.Empty;
            blenderOptionsHelpBox.style.display = usesBlenderDecimate && optionsToggle.value
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            uvLoopDissolveHelpBox.style.display = usesUvLoopDissolve && optionsToggle.value
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

        private int GetTotalEstimatedOriginalTriangleCount()
        {
            var totalCount = 0;
            var target = Target;
            foreach (var entry in target.Entries)
            {
                if (!entry.IsValid(target) || !TryGetOriginalTriangleCount(entry, false, out var triangleCount) ||
                    entry.GetTargetRenderer(target) is not { } renderer)
                {
                    continue;
                }

                totalCount += DownstreamTriangleEstimator.EstimateFinalTriangleCount(renderer, triangleCount);
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

        private void AdjustQuality(int fixedIndex = -1)
        {
            serializedObject.ApplyModifiedProperties();
            var finalTargetTotalCount = TargetTriangleCountProperty.intValue;
            var targetTotalCount = finalTargetTotalCount;
            if (TryGetAnalyzedCalibration(Target, out var calibration, out _))
            {
                targetTotalCount = DownstreamTriangleEstimator.GetPreDownstreamTarget(
                    finalTargetTotalCount,
                    calibration.EstimatedBeforeDownstreamTriangleCount,
                    calibration.TriangleCount);
            }

            var target = Target;
            var entries = target.Entries;
            var entriesProperty = EntriesProperty;

            Undo.RecordObject(target, "Adjust Quality");

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

                    TryGetEstimatedFinalTriangleCount(entry, false, out var triangleCount);

                    currentTotal += triangleCount;

                    if (entry.Enabled && !entry.Fixed && i != fixedIndex)
                    {
                        adjustableTotal += triangleCount;
                    }
                }
                
                if (adjustableTotal == 0) { Debug.LogError("Adjustable total is 0"); break; }
                
                var adjustableTargetCount = targetTotalCount - (currentTotal - adjustableTotal);
                if (adjustableTargetCount <= 0) { Debug.LogError("Adjustable target count is 0"); break; }
                
                // 比例配分で調整
                var proportion = (float)adjustableTargetCount / adjustableTotal;
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
                        
                        var newValue = Mathf.Clamp((int)(currentValue * proportion), 0, maxTriangleCount);
                        entry.TargetTriangleCount = newValue;
                    }
                }
            }
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

                    TryGetOriginalTriangleCount(entry, true, out var originalTriangleCount);
                    var targetTriangleCountProperty = entryProperty.FindPropertyRelative(nameof(MeshiaCascadingAvatarMeshSimplifierRendererEntry.TargetTriangleCount));


                    targetTriangleCountProperty.intValue = Mathf.Clamp(
                        (int)(originalTriangleCount * ratio),
                        0,
                        originalTriangleCount);
                }
            }
        }

        private void AnalyzeNdmfBuild(Button button)
        {
            var avatarRoot = RuntimeUtil.FindAvatarInParents(Target.transform);
            if (avatarRoot == null)
            {
                StoreBuildAnalysisResult(Target, new BuildAnalysisResult(
                    0,
                    0,
                    CurrentAnalysisRevision,
                    "Could not find the avatar root."));
                return;
            }

            var originalButtonText = button.text;
            GameObject? clone = null;
            var finalTriangleCount = 0;
            var estimatedBeforeDownstreamTriangleCount = GetTotalEstimatedFinalTriangleCount(true);
            string? analysisError = null;
            var previousDisablePreviewDepth = NDMFPreview.DisablePreviewDepth;
            s_analysisInProgress = true;
            NDMFPreview.DisablePreviewDepth = previousDisablePreviewDepth + 1;
            button.SetEnabled(false);
            button.text = "Analyzing...";

            try
            {
                EditorUtility.DisplayProgressBar("Meshia", "Analyzing the complete NDMF avatar build...", 0.5f);
                clone = Instantiate(avatarRoot.gameObject);
                clone.name = $"{avatarRoot.name} (Meshia Triangle Analysis)";
                clone.SetActive(true);
                var buildContext = AvatarProcessor.ProcessAvatar(clone, AmbientPlatform.CurrentPlatform);
                if (!buildContext.Successful)
                {
                    analysisError = "Build reported errors; count may be incomplete.";
                }

                foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer is MeshRenderer or SkinnedMeshRenderer && RendererUtility.GetMesh(renderer) is { } mesh)
                    {
                        finalTriangleCount += mesh.GetTriangleCount();
                    }
                }

            }
            catch (Exception exception)
            {
                Debug.LogException(exception, Target);
                analysisError = exception.Message;
            }
            finally
            {
                if (clone != null)
                {
                    DestroyImmediate(clone);
                }

                try
                {
                    AvatarProcessor.CleanTemporaryAssets();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, Target);
                }

                EditorUtility.ClearProgressBar();
                NDMFPreview.DisablePreviewDepth = previousDisablePreviewDepth;
                button.text = originalButtonText;
                button.SetEnabled(true);
            }

            StoreBuildAnalysisResult(Target, new BuildAnalysisResult(
                finalTriangleCount,
                estimatedBeforeDownstreamTriangleCount,
                CurrentAnalysisRevision,
                analysisError));
            EditorApplication.delayCall += () => s_analysisInProgress = false;
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
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
                string.IsNullOrEmpty(stored.Error) ? null : stored.Error);
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
