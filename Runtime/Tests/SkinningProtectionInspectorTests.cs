using System.Collections;
using Meshia.MeshSimplification.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Meshia.MeshSimplification.Tests
{
    public class SkinningProtectionInspectorTests
    {
        public class OptionsHost : ScriptableObject
        {
            public MeshSimplifierOptions Options = MeshSimplifierOptions.Default;
        }

        public class TestWindow : EditorWindow { }

        [UnityTest]
        public IEnumerator ShouldRefreshSkinningPolicyWithoutChangingSerializedSettings()
        {
            var host = ScriptableObject.CreateInstance<OptionsHost>();
            var window = ScriptableObject.CreateInstance<TestWindow>();
            try
            {
                window.Show();
                foreach (var policy in new[] { SkinningProtectionPolicy.Auto, SkinningProtectionPolicy.On,
                    SkinningProtectionPolicy.Off, SkinningProtectionPolicy.Legacy })
                foreach (var enabled in new[] { false, true })
                {
                    window.rootVisualElement.Clear();
                    host.Options.SkinningProtection.Policy = policy;
                    host.Options.SkinningProtection.Enabled = enabled;
                    using var serialized = new SerializedObject(host);
                    var root = new MeshSimplifierOptionsDrawer().CreatePropertyGUI(serialized.FindProperty(nameof(OptionsHost.Options)));
                    window.rootVisualElement.Add(root);
                    root.Bind(serialized);
                    for (var i = 0; i < 5; i++) yield return null;

                    Assert.That(host.Options.SkinningProtection.Policy, Is.EqualTo(policy), "Opening or rebinding the inspector must not change policy.");
                    Assert.That(host.Options.SkinningProtection.Enabled, Is.EqualTo(enabled));
                    Assert.That(root.Q<Toggle>("SkinningProtectionAuto").value, Is.EqualTo(policy == SkinningProtectionPolicy.Auto));
                    Assert.That(root.Q<Toggle>("SkinningProtectionEnabled").value,
                        Is.EqualTo(policy == SkinningProtectionPolicy.On || (policy == SkinningProtectionPolicy.Legacy && enabled)));
                    root.Unbind();
                }
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator ShouldApplyManualSkinningChoicesAndRefreshAfterUndo()
        {
            var host = ScriptableObject.CreateInstance<OptionsHost>();
            var window = ScriptableObject.CreateInstance<TestWindow>();
            try
            {
                host.Options.SkinningProtection.Policy = SkinningProtectionPolicy.Off;
                using var serialized = new SerializedObject(host);
                var root = new MeshSimplifierOptionsDrawer().CreatePropertyGUI(serialized.FindProperty(nameof(OptionsHost.Options)));
                window.rootVisualElement.Add(root);
                window.Show();
                root.Bind(serialized);
                for (var i = 0; i < 5; i++) yield return null;
                var manual = root.Q<Toggle>("SkinningProtectionEnabled");
                var automatic = root.Q<Toggle>("SkinningProtectionAuto");
                manual.value = true;
                Assert.That(host.Options.SkinningProtection.Policy, Is.EqualTo(SkinningProtectionPolicy.On));
                Assert.That(host.Options.SkinningProtection.Enabled, Is.True);

                Undo.IncrementCurrentGroup();
                automatic.value = true;
                Assert.That(host.Options.SkinningProtection.Policy, Is.EqualTo(SkinningProtectionPolicy.Auto));
                Assert.That(manual.enabledSelf, Is.False);
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                // UI Toolkit's serialized tracking is throttled independently
                // of test frames, which can run extremely fast in batch mode.
                var deadline = EditorApplication.timeSinceStartup + 2d;
                while (automatic.value && EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.That(host.Options.SkinningProtection.Policy, Is.EqualTo(SkinningProtectionPolicy.On));
                Assert.That(automatic.value, Is.False);
                Assert.That(manual.value, Is.True);
                Assert.That(manual.enabledSelf, Is.True);
                manual.value = false;
                Assert.That(host.Options.SkinningProtection.Policy, Is.EqualTo(SkinningProtectionPolicy.Off));
                Assert.That(host.Options.SkinningProtection.Enabled, Is.False);
                root.Unbind();
            }
            finally
            {
                Undo.ClearUndo(host);
                window.Close();
                Object.DestroyImmediate(host);
            }
        }
    }
}
