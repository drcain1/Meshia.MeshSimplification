#nullable enable
using System;
using NUnit.Framework;
using UnityEngine;

namespace Meshia.MeshSimplification.Tests
{
    public sealed class SkinningProtectionOptionsTests
    {
        [Test]
        public void ConservativeAvatarDefaultsKeepCoreSkinningOptIn()
        {
            var options = MeshSimplifierOptions.ConservativeAvatar;
            Assert.That(options.SkinningProtection.Policy, Is.EqualTo(SkinningProtectionPolicy.AutoDeforming));
            Assert.That(options.SkinningProtection.PreserveJointTransitions, Is.True);
            Assert.That(options.SkinningProtection.Strength, Is.EqualTo(2f));
            Assert.That(options.SkinningProtection.MaxWeightDistance, Is.EqualTo(.1f));
            Assert.That(options.SkinningProtection.MaxDiscardedWeight, Is.EqualTo(.02f));
            Assert.That(options.FaQem.MaxSurfaceDeviation, Is.EqualTo(.0005f));
            Assert.That(options.PreserveBorderEdges && options.FaQem.PreserveAttributeSeams, Is.True);
            Assert.That(options.SkinningProtection.Resolve(true).Enabled, Is.True);
            Assert.That(options.SkinningProtection.Resolve(false).Enabled, Is.False);
            Assert.That(MeshSimplifierOptions.Default.SkinningProtection.Policy, Is.EqualTo(SkinningProtectionPolicy.Legacy));
            Assert.That(MeshSimplifierOptions.Default.SkinningProtection.PreserveJointTransitions, Is.False);
            Assert.That(MeshSimplifierOptions.Default.FaQem.MaxSurfaceDeviation, Is.EqualTo(.0005f));
        }

        [Test]
        public void LegacyZeroBlockRemainsDisabledAndValid()
        {
            var options = new SkinningProtectionOptions();
            Assert.That(options.Enabled, Is.False);
            Assert.DoesNotThrow(options.Validate);
            Assert.That(options.Policy, Is.EqualTo(SkinningProtectionPolicy.Legacy));
            Assert.That(options.Resolve(true).Enabled, Is.False);
        }

        [Test]
        public void AvatarInitialKeepsGeometryGuardsButRequiresOptInForDeformation()
        {
            var initial = MeshSimplifierOptions.AvatarInitial;
            Assert.IsFalse(initial.SkinningProtection.Resolve(true).Enabled);
            Assert.IsFalse(initial.SkinningProtection.PreserveJointTransitions);
            Assert.IsTrue(initial.PreserveBorderEdges);
            Assert.IsTrue(initial.FaQem.PreserveAttributeSeams);
            Assert.AreEqual(.0005f, initial.FaQem.MaxSurfaceDeviation);
            Assert.IsTrue(MeshSimplifierOptions.ConservativeAvatar.SkinningProtection.Resolve(true).Enabled);
        }

        [Test]
        public void PolicyResolutionPreservesLegacyAndHonorsExplicitModes()
        {
            var options = SkinningProtectionOptions.Default;
            Assert.That(options.Policy, Is.EqualTo(SkinningProtectionPolicy.Legacy));
            options.Policy = SkinningProtectionPolicy.Auto;
            Assert.That(options.Resolve(true).Enabled, Is.True);
            Assert.That(options.Resolve(false).Enabled, Is.False);
            options.Policy = SkinningProtectionPolicy.On;
            Assert.That(options.Resolve(false).Enabled, Is.True);
            options.Policy = SkinningProtectionPolicy.Off;
            Assert.That(options.Resolve(true).Enabled, Is.False);
            options.Policy = SkinningProtectionPolicy.Legacy;
            options.Enabled = true;
            Assert.That(options.Resolve(false).Enabled, Is.True);
        }

        [Test]
        public void PolicyParticipatesInEqualityAndHash()
        {
            var a = SkinningProtectionOptions.Default;
            var b = a;
            b.Policy = SkinningProtectionPolicy.On;
            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.Not.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void DefaultBlockIsStableAndIncludedInParentEquality()
        {
            var first = MeshSimplifierOptions.Default;
            var second = MeshSimplifierOptions.Default;
            Assert.That(first.SkinningProtection, Is.EqualTo(SkinningProtectionOptions.Default));
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));

            second.SkinningProtection.Enabled = true;
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(first.GetHashCode(), Is.Not.EqualTo(second.GetHashCode()));
        }

        [Test]
        public void ValidationRejectsNonFiniteAndOutOfRangeValues()
        {
            var invalid = SkinningProtectionOptions.Default;
            invalid.Strength = float.NaN;
            Assert.Throws<ArgumentOutOfRangeException>(invalid.Validate);
            invalid = SkinningProtectionOptions.Default;
            invalid.Strength = float.PositiveInfinity;
            Assert.Throws<ArgumentOutOfRangeException>(invalid.Validate);
            invalid = SkinningProtectionOptions.Default;
            invalid.MaxWeightDistance = 1.01f;
            Assert.Throws<ArgumentOutOfRangeException>(invalid.Validate);
            invalid = SkinningProtectionOptions.Default;
            invalid.MaxDiscardedWeight = -0.01f;
            Assert.Throws<ArgumentOutOfRangeException>(invalid.Validate);
        }

        [Test]
        public void DisabledStrictZeroValuesRemainIntentional()
        {
            var options = new SkinningProtectionOptions
            {
                Enabled = false,
                Strength = 0f,
                MaxWeightDistance = 0f,
                MaxDiscardedWeight = 0f,
            };
            Assert.DoesNotThrow(options.Validate);
            Assert.That(options.Enabled, Is.False);
            Assert.That(options.Strength, Is.Zero);
        }

        [Test]
        public void InvalidProtectionIsRejectedForBlenderStyleTargets()
        {
            var source = new Mesh();
            var destination = new Mesh();
            try
            {
                source.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                source.triangles = new[] { 0, 1, 2 };
                var options = MeshSimplifierOptions.Default;
                options.SkinningProtection.Enabled = true;
                options.SkinningProtection.MaxWeightDistance = float.NaN;
                Assert.Throws<ArgumentOutOfRangeException>(() => MeshSimplifier.Simplify(source,
                    new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.BlenderDecimateRatio, Value = .5f },
                    options, destination));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(destination);
            }
        }
    }
}
