#nullable enable
namespace Meshia.MeshSimplification.Editor.Localization
{
    using CustomLocalization4EditorExtension;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEditor.UIElements;
    using UnityEngine.UIElements;

    /// <summary>Shared editor translations and live language updates.</summary>
    public static class LocalizationProvider
    {
        private const string DefaultLocale = "en";
        private const string LanguagePickerOwnerClass = "meshia-language-picker-owner";

        [AssemblyCL4EELocalization]
        internal static Localization Localization { get; } = new("ca7beb49d3e85244e803080472c014c2", DefaultLocale);

        /// <summary>Translates editor text, retaining English when a translation is absent.</summary>
        public static string Tr(string text) => Localization.Tr(text);

        /// <summary>Formats a translated message without translating its data arguments.</summary>
        public static string Format(string text, params object[] args) => string.Format(Tr(text), args);

        /// <summary>Gets or sets the shared editor language.</summary>
        public static string CurrentLocale
        {
            get { _ = Tr("Language"); return Localization.CurrentLocaleCode ?? DefaultLocale; }
            set { _ = Tr("Language"); Localization.CurrentLocaleCode = value; }
        }

        /// <summary>Localizes authored UI text and refreshes it while its panel is attached.</summary>
        public static void Bind(VisualElement root, Action? onRefresh = null)
        {
            var updates = new List<Action>();
            void Capture(string source, Action<string> setter)
            {
                if (!string.IsNullOrEmpty(source)) updates.Add(() => setter(Tr(source)));
            }
            void Visit(VisualElement element)
            {
                Capture(element.tooltip, text => element.tooltip = text);
                // Do not capture generated child labels or values: binding owns those.
                switch (element)
                {
                    case ListView _: return; // Virtualized rows localize themselves.
                    // Unity owns the object-name/None display and refreshes it as values change.
                    // Capturing its child text here would replay the pre-binding placeholder.
                    case ObjectField field: Capture(field.label, text => field.label = text); return;
                    case PropertyField field: Capture(field.label, text => field.label = text); return;
                    case DropdownField field:
                        Capture(field.label, text => field.label = text);
                        if (field.name == "LanguagePicker")
                        {
                            root.AddToClassList(LanguagePickerOwnerClass);
                            updates.Add(() =>
                            {
                                // Embedded options share their inspector's language control.
                                // A standalone options drawer still needs its own picker.
                                var hasParentPicker = false;
                                for (var parent = root.parent; parent != null; parent = parent.parent)
                                    if (parent.ClassListContains(LanguagePickerOwnerClass))
                                    {
                                        hasParentPicker = true;
                                        break;
                                    }
                                field.style.display = hasParentPicker ? DisplayStyle.None : DisplayStyle.Flex;
                            });
                            field.choices = Localization.LocalizationByIsoCode.Keys.ToList();
                            field.formatListItemCallback = code => Tr("locale:" + code);
                            field.formatSelectedValueCallback = code => Tr("locale:" + code);
                            field.RegisterValueChangedCallback(evt =>
                            {
                                // Label text changes also bubble ChangeEvent<string> through a field.
                                if (evt.target == field && field.choices.Contains(evt.newValue)) CurrentLocale = evt.newValue;
                            });
                            updates.Add(() => field.SetValueWithoutNotify(CurrentLocale));
                        }
                        else
                        {
                            field.formatListItemCallback = Tr;
                            field.formatSelectedValueCallback = Tr;
                            updates.Add(() => field.SetValueWithoutNotify(field.value));
                        }
                        return;
                    case Toggle field: Capture(field.label, text => field.label = text); return;
                    case FloatField field: Capture(field.label, text => field.label = text); return;
                    case IntegerField field: Capture(field.label, text => field.label = text); return;
                    case Slider field: Capture(field.label, text => field.label = text); return;
                    case SliderInt field: Capture(field.label, text => field.label = text); return;
                    case TextField field: Capture(field.label, text => field.label = text); return;
                    case HelpBox box: Capture(box.text, text => box.text = text); return;
                    case TextElement textElement: Capture(textElement.text, text => textElement.text = text); return;
                    case Foldout foldout: Capture(foldout.text, text => foldout.text = text); break;
                    case GroupBox group: Capture(group.text, text => group.text = text); break;
                }
                foreach (var child in element.Children()) Visit(child);
            }
            Visit(root);
            void Refresh(string _)
            {
                foreach (var update in updates) update();
                onRefresh?.Invoke();
                root.MarkDirtyRepaint();
            }
            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Localization.LocaleChanged -= Refresh;
                Localization.LocaleChanged += Refresh;
                Refresh(CurrentLocale);
            });
            root.RegisterCallback<DetachFromPanelEvent>(_ => Localization.LocaleChanged -= Refresh);
            Refresh(CurrentLocale);
        }

        /// <summary>Binds enum indices independently of translated display names.</summary>
        public static void BindEnum(DropdownField field, UnityEditor.SerializedProperty property)
        {
            field.choices = property.enumDisplayNames.ToList();
            field.formatListItemCallback = Tr;
            field.formatSelectedValueCallback = Tr;
            field.BindProperty(property);
        }

        /// <summary>Preserves the minimum used by a numeric property's IMGUI drawer.</summary>
        public static void SetMinimum(FloatField field, float minimum)
        {
            field.RegisterValueChangedCallback(evt =>
            {
                if (evt.target != field || evt.newValue >= minimum) return;
                field.value = minimum;
                evt.StopImmediatePropagation();
            });
        }
        public static void LocalizeBindedElements<T>(VisualElement root)
        {
            var typeName = typeof(T).FullName;
            root.Query().OfType<BindableElement>().Where(bindableElement => !string.IsNullOrEmpty(bindableElement.bindingPath))
                .ForEach(bindableElement =>
                {
                    if (Localization.TryTr($"{typeName}.{bindableElement.bindingPath}.label") is { } translatedLabel)
                    {
                        switch (bindableElement)
                        {
                            case Toggle toggle:
                                {
                                    toggle.label = translatedLabel;
                                }
                                break;
                            case FloatField floatField:
                                {
                                    floatField.label = translatedLabel;
                                }
                                break;
                            case Slider slider:
                                {
                                    slider.label = translatedLabel;
                                }
                                break;
                        }
                    }
                    if (Localization.TryTr($"{typeName}.{bindableElement.bindingPath}.tooltip") is { } translatedTooltip)
                    {
                        bindableElement.tooltip = translatedTooltip;
                    }
                });
        }
    }

}
