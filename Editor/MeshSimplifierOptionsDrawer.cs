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
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetDatabase.GUIDToAssetPath("29eaabb0631cacc44913c34b86fc38f0"));

            var root = visualTreeAsset.CloneTree();

            LocalizationProvider.Bind(root, () => LocalizationProvider.LocalizeBindedElements<MeshSimplifierOptions>(root));

            foreach (var field in root.Query<FloatField>().ToList())
            {
                if (field.bindingPath.StartsWith("FaQem."))
                {
                    LocalizationProvider.SetMinimum(field, field.bindingPath == "FaQem.PlaneAreaWeight" ? 0.000001f : 0f);
                }
            }

            root.BindProperty(property);

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
                skinningProtectionEnabled.SetEnabled(true);
                property.serializedObject.ApplyModifiedProperties();
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
                    ? (int)SkinningProtectionPolicy.Auto
                    : (skinningProtectionPolicy.enumValueIndex == (int)SkinningProtectionPolicy.Auto
                        ? (int)SkinningProtectionPolicy.Off
                        : (skinningProtectionEnabled.value ? (int)SkinningProtectionPolicy.On : (int)SkinningProtectionPolicy.Off));
                skinningProtectionEnabled.SetEnabled(!changeEvent.newValue);
                property.serializedObject.ApplyModifiedProperties();
            });
            // Binding the legacy Enabled field directly dispatches change
            // events during refresh and can overwrite Auto or explicit On.
            // Display the resolved policy without treating refresh as input.
            void RefreshSkinningProtection()
            {
                var protection = property.FindPropertyRelative(nameof(MeshSimplifierOptions.SkinningProtection));
                var policy = (SkinningProtectionPolicy)skinningProtectionPolicy.enumValueIndex;
                var automatic = policy == SkinningProtectionPolicy.Auto;
                skinningProtectionAuto.SetValueWithoutNotify(automatic);
                skinningProtectionEnabled.SetValueWithoutNotify(policy == SkinningProtectionPolicy.On ||
                    (policy == SkinningProtectionPolicy.Legacy &&
                     protection.FindPropertyRelative(nameof(SkinningProtectionOptions.Enabled)).boolValue));
                skinningProtectionEnabled.SetEnabled(!automatic);
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
                var defaults = MeshSimplifierOptions.Default;
                defaults.SkinningProtection.Policy = SkinningProtectionPolicy.Auto;
                property.boxedValue = defaults;
                property.serializedObject.ApplyModifiedProperties();
            };




            return root;
        }
    }

}
