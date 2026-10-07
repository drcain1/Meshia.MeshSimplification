#nullable enable
using Meshia.MeshSimplification.Editor.Localization;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
namespace Meshia.MeshSimplification.Editor
{
    [CustomPropertyDrawer(typeof(MeshSimplifierOptions))]
    public class MeshSimplifierOptionsDrawer : PropertyDrawer
    {
        /// <summary>Shows only the settings used by the selected algorithm, including late-bound drawers.</summary>
        public static void SetAlgorithmVisibility(VisualElement root, MeshSimplificationTargetKind? kind)
        {
            var blender = kind == MeshSimplificationTargetKind.BlenderDecimateRatio;
            var uv = kind == MeshSimplificationTargetKind.UvLoopDissolveTriangleCount;
            var fa = kind == MeshSimplificationTargetKind.FaQemTriangleCount;
            root.EnableInClassList("meshia-options-blender", blender);
            root.EnableInClassList("meshia-options-uv", uv);
            root.EnableInClassList("meshia-options-fa", fa);
            root.EnableInClassList("meshia-options-meshia", kind.HasValue && !blender && !uv && !fa);
            root.EnableInClassList("meshia-options-mixed", !kind.HasValue);
        }

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetDatabase.GUIDToAssetPath("29eaabb0631cacc44913c34b86fc38f0"));

            var root = visualTreeAsset.CloneTree();

            System.Action refreshUvLabels = () => { };
            LocalizationProvider.Bind(root, () =>
            {
                LocalizationProvider.LocalizeBindedElements<MeshSimplifierOptions>(root);
                refreshUvLabels();
            });

            foreach (var field in root.Query<FloatField>().ToList())
            {
                if (field.bindingPath.StartsWith("FaQem."))
                {
                    LocalizationProvider.SetMinimum(field, field.bindingPath == "FaQem.PlaneAreaWeight" ? 0.000001f : 0f);
                }
            }

            root.BindProperty(property);

            // Keep the useful small tolerances spread across the track. The separate
            // numeric field retains the full supported range, including saved values
            // above the slider maximum; merely opening the drawer never clamps them.
            var deviation = property.FindPropertyRelative(nameof(MeshSimplifierOptions.FaQem))
                .FindPropertyRelative(nameof(FaQemOptions.MaxSurfaceDeviation));
            var deviationSlider = root.Q<Slider>("SurfaceDeviationSlider");
            var deviationField = root.Q<FloatField>("SurfaceDeviationField");
            void RefreshDeviation()
            {
                deviationSlider.SetValueWithoutNotify(Mathf.Clamp(deviation.floatValue, 0f, .005f));
                deviationField.SetValueWithoutNotify(deviation.floatValue);
            }
            void SetDeviation(float value)
            {
                if (!float.IsNaN(value) && !float.IsInfinity(value))
                {
                    deviation.floatValue = Mathf.Clamp(value, 0f, .1f);
                    property.serializedObject.ApplyModifiedProperties();
                }
                RefreshDeviation();
            }
            deviationSlider.RegisterValueChangedCallback(evt => SetDeviation(Mathf.Round(evt.newValue * 100000f) / 100000f));
            deviationField.RegisterValueChangedCallback(evt => SetDeviation(evt.newValue));
            root.TrackPropertyValue(deviation, _ => RefreshDeviation());
            void RefreshDeviationAfterUndo()
            {
                property.serializedObject.UpdateIfRequiredOrScript();
                RefreshDeviation();
                RefreshUvControls();
            }
            RefreshDeviation();

            var faQemPropertyForUv = property.FindPropertyRelative(nameof(MeshSimplifierOptions.FaQem));
            var uvEnabled = faQemPropertyForUv.FindPropertyRelative(nameof(FaQemOptions.ExperimentalUvEnabled));
            var uvJoint = faQemPropertyForUv.FindPropertyRelative(nameof(FaQemOptions.ExperimentalJointUv));
            var uvWeight = faQemPropertyForUv.FindPropertyRelative(nameof(FaQemOptions.ExperimentalUvWeight));
            var uvToggle = root.Q<Toggle>("ExperimentalUvToggle");
            var uvControls = root.Q<VisualElement>("UvPreservationControls");
            var uvSlider = root.Q<SliderInt>("UvStrengthSlider");
            var uvPreset = root.Q<DropdownField>("UvStrengthPreset");
            var uvNumber = root.Q<FloatField>("ExperimentalUvWeightField");
            var uvWeights = new[] { 1000f, 5000f, 10000f };
            var uvNames = new[] { "Low", "Medium", "High" };
            void RefreshUvControls()
            {
                uvToggle.SetValueWithoutNotify(uvEnabled.boolValue);
                uvNumber.SetValueWithoutNotify(uvWeight.floatValue);
                uvControls.style.display = uvEnabled.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
                var preset = System.Array.IndexOf(uvWeights, uvWeight.floatValue);
                // A custom value has no exact slider position. Display the nearest
                // preset, but keep its Custom label and never rewrite the saved value.
                var position = preset >= 0 ? preset : uvWeight.floatValue < 3000f ? 0 : uvWeight.floatValue < 7500f ? 1 : 2;
                uvSlider.SetValueWithoutNotify(position);
                uvPreset.choices = preset >= 0
                    ? new System.Collections.Generic.List<string>(uvNames)
                    : new System.Collections.Generic.List<string> { "Low", "Medium", "High", "Custom" };
                uvPreset.SetValueWithoutNotify(preset >= 0 ? uvNames[preset] : "Custom");
            }
            refreshUvLabels = RefreshUvControls;
            uvToggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.target != uvToggle) return;
                uvEnabled.boolValue = evt.newValue;
                if (evt.newValue)
                {
                    // Explicitly enabling the combined control selects the current
                    // method. Passive binding/localization never migrates legacy data.
                    uvJoint.boolValue = true;
                    if (!(uvWeight.floatValue > 0) || float.IsInfinity(uvWeight.floatValue))
                        uvWeight.floatValue = FaQemOptions.Default.ExperimentalUvWeight;
                }
                property.serializedObject.ApplyModifiedProperties();
                RefreshUvControls();
            });
            uvSlider.RegisterValueChangedCallback(evt =>
            {
                if (evt.target != uvSlider) return;
                uvWeight.floatValue = uvWeights[Mathf.Clamp(evt.newValue, 0, 2)];
                property.serializedObject.ApplyModifiedProperties();
                RefreshUvControls();
            });
            uvPreset.RegisterValueChangedCallback(evt =>
            {
                if (evt.target != uvPreset) return;
                var preset = System.Array.IndexOf(uvNames, evt.newValue);
                if (preset < 0) return;
                uvWeight.floatValue = uvWeights[preset];
                property.serializedObject.ApplyModifiedProperties();
                RefreshUvControls();
            });
            root.TrackPropertyValue(uvEnabled, _ => RefreshUvControls());
            root.TrackPropertyValue(uvJoint, _ => RefreshUvControls());
            root.TrackPropertyValue(uvWeight, _ => RefreshUvControls());
            RefreshUvControls();
            root.RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += RefreshDeviationAfterUndo);
            root.RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= RefreshDeviationAfterUndo);

            var enableSmartLinkToggle = root.Q<Toggle>("EnableSmartLinkToggle");
            var smartLinkOptionsGroup = root.Q<GroupBox>("SmartLinkOptionsGroup");
            var faQemOptionsGroup = root.Q<Foldout>("FaQemOptionsGroup");
            var skinningProtectionEnabled = root.Q<Toggle>("SkinningProtectionEnabled");
            var skinningProtectionAuto = root.Q<Toggle>("SkinningProtectionAuto");
            var skinningProtectionPolicy = property.FindPropertyRelative(nameof(MeshSimplifierOptions.SkinningProtection))
                .FindPropertyRelative(nameof(SkinningProtectionOptions.Policy));
            var resetOptionsButton = root.Q<Button>("ResetOptionsButton");

            LocalizationProvider.LocalizeBindedElements<MeshSimplifierOptions>(root);
            smartLinkOptionsGroup.text = LocalizationProvider.Localization.Tr("Meshia.MeshSimplification.MeshSimplifierOptions.SmartLinkOptions");
            faQemOptionsGroup.text = LocalizationProvider.Localization.Tr("Meshia.MeshSimplification.MeshSimplifierOptions.FaQemOptions");
            skinningProtectionEnabled.RegisterValueChangedCallback(changeEvent =>
            {
                var protection = property.FindPropertyRelative(nameof(MeshSimplifierOptions.SkinningProtection));
                if (changeEvent.newValue && protection.FindPropertyRelative(nameof(SkinningProtectionOptions.Strength)).floatValue == 0f &&
                    protection.FindPropertyRelative(nameof(SkinningProtectionOptions.MaxWeightDistance)).floatValue == 0f &&
                    protection.FindPropertyRelative(nameof(SkinningProtectionOptions.MaxDiscardedWeight)).floatValue == 0f)
                {
                    protection.boxedValue = SkinningProtectionOptions.Default;
                    protection.FindPropertyRelative(nameof(SkinningProtectionOptions.Enabled)).boolValue = true;
                    property.serializedObject.ApplyModifiedProperties();
                }
                skinningProtectionPolicy.enumValueIndex = changeEvent.newValue
                    ? (int)SkinningProtectionPolicy.On
                    : (int)SkinningProtectionPolicy.Off;
                protection.FindPropertyRelative(nameof(SkinningProtectionOptions.Enabled)).boolValue = changeEvent.newValue;
                skinningProtectionAuto.SetValueWithoutNotify(false);
                property.serializedObject.ApplyModifiedProperties();
                RefreshSkinningProtection();
            });

            skinningProtectionAuto.RegisterValueChangedCallback(changeEvent =>
            {
                var protection = property.FindPropertyRelative(nameof(MeshSimplifierOptions.SkinningProtection));
                var wasLegacy = skinningProtectionPolicy.enumValueIndex == (int)SkinningProtectionPolicy.Legacy;
                if (changeEvent.newValue && wasLegacy &&
                    protection.FindPropertyRelative(nameof(SkinningProtectionOptions.Strength)).floatValue == 0f &&
                    protection.FindPropertyRelative(nameof(SkinningProtectionOptions.MaxWeightDistance)).floatValue == 0f &&
                    protection.FindPropertyRelative(nameof(SkinningProtectionOptions.MaxDiscardedWeight)).floatValue == 0f)
                {
                    var defaults = SkinningProtectionOptions.Default;
                    protection.FindPropertyRelative(nameof(SkinningProtectionOptions.Strength)).floatValue = defaults.Strength;
                    protection.FindPropertyRelative(nameof(SkinningProtectionOptions.MaxWeightDistance)).floatValue = defaults.MaxWeightDistance;
                    protection.FindPropertyRelative(nameof(SkinningProtectionOptions.MaxDiscardedWeight)).floatValue = defaults.MaxDiscardedWeight;
                }
                skinningProtectionPolicy.enumValueIndex = changeEvent.newValue
                    ? (int)SkinningProtectionPolicy.AutoDeforming
                    : ((skinningProtectionPolicy.enumValueIndex == (int)SkinningProtectionPolicy.Auto || skinningProtectionPolicy.enumValueIndex == (int)SkinningProtectionPolicy.AutoDeforming)
                        ? (int)SkinningProtectionPolicy.Off
                        : (skinningProtectionEnabled.value ? (int)SkinningProtectionPolicy.On : (int)SkinningProtectionPolicy.Off));
                property.serializedObject.ApplyModifiedProperties();
                RefreshSkinningProtection();
            });
            // Binding the legacy Enabled field directly dispatches change
            // events during refresh and can overwrite Auto or explicit On.
            // Display the resolved policy without treating refresh as input.
            void RefreshSkinningProtection()
            {
                var protection = property.FindPropertyRelative(nameof(MeshSimplifierOptions.SkinningProtection));
                var policy = (SkinningProtectionPolicy)skinningProtectionPolicy.enumValueIndex;
                var automatic = policy == SkinningProtectionPolicy.Auto || policy == SkinningProtectionPolicy.AutoDeforming;
                root.Q<HelpBox>("LegacyAutomaticProtectionHelp").style.display = policy == SkinningProtectionPolicy.Auto ? DisplayStyle.Flex : DisplayStyle.None;
                root.Q<Label>("AutomaticJointProtectionHelp").style.display = policy == SkinningProtectionPolicy.AutoDeforming ? DisplayStyle.Flex : DisplayStyle.None;
                skinningProtectionAuto.SetValueWithoutNotify(automatic);
                skinningProtectionEnabled.SetValueWithoutNotify(policy == SkinningProtectionPolicy.On ||
                    (policy == SkinningProtectionPolicy.Legacy &&
                     protection.FindPropertyRelative(nameof(SkinningProtectionOptions.Enabled)).boolValue));
                skinningProtectionEnabled.SetEnabled(!automatic);
                // Auto resolves against renderer bone data during preview/build.
                // An unchecked manual override is not its effective protection state.
                skinningProtectionEnabled.style.display = automatic ? DisplayStyle.None : DisplayStyle.Flex;
            }
            root.TrackPropertyValue(skinningProtectionPolicy, _ => RefreshSkinningProtection());
            root.TrackPropertyValue(property.FindPropertyRelative(nameof(MeshSimplifierOptions.SkinningProtection))
                .FindPropertyRelative(nameof(SkinningProtectionOptions.Enabled)), _ => RefreshSkinningProtection());
            RefreshSkinningProtection();

            faQemOptionsGroup.RegisterValueChangedCallback(changeEvent =>
            {
                if (!changeEvent.newValue)
                {
                    return;
                }

                var faQemProperty = property.FindPropertyRelative(nameof(MeshSimplifierOptions.FaQem));
                var versionProperty = faQemProperty.FindPropertyRelative("version");
                if (versionProperty != null && versionProperty.intValue == 0)
                {
                    faQemProperty.boxedValue = FaQemOptions.Default;
                    property.serializedObject.ApplyModifiedProperties();
                }
            });

            enableSmartLinkToggle.RegisterValueChangedCallback(changeEvent =>
            {
                smartLinkOptionsGroup.style.display = changeEvent.newValue ? DisplayStyle.Flex : DisplayStyle.None;

            });


            resetOptionsButton.clicked += () =>
            {
                var defaults = MeshSimplifierOptions.ConservativeAvatar;
                property.boxedValue = defaults;
                property.serializedObject.ApplyModifiedProperties();
                RefreshDeviation();
                RefreshUvControls();
            };




            return root;
        }
    }

}
