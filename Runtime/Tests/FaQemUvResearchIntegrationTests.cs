using System;
using NUnit.Framework;
using Unity.Mathematics;

namespace Meshia.MeshSimplification.Tests
{
    public class FaQemUvResearchIntegrationTests
    {
        [Test]
        public void ShouldEnableJointUvByDefaultAndIncludeItsSettingsInOptionIdentity()
        {
            var original = MeshSimplifierOptions.Default;
            Assert.IsTrue(original.FaQem.ExperimentalUvEnabled);
            Assert.AreEqual(5000, original.FaQem.ExperimentalUvWeight);
            Assert.IsTrue(original.FaQem.ExperimentalJointUv);
            var joint = original; joint.FaQem.ExperimentalJointUv = false;
            Assert.AreNotEqual(original, joint);
            Assert.AreNotEqual(original.FaQem.GetHashCode(), joint.FaQem.GetHashCode());
            var changed = original;
            changed.FaQem.ExperimentalUvEnabled = false;
            Assert.AreNotEqual(original, changed);
            Assert.AreNotEqual(original.FaQem.GetHashCode(), changed.FaQem.GetHashCode());
            Assert.IsFalse(original.WithoutProtections().FaQem.ExperimentalUvEnabled);
            Assert.IsTrue(original.FaQem.ExperimentalUvEnabled, "Bypass must not overwrite saved settings.");
            var stronger = changed;
            stronger.FaQem.ExperimentalUvWeight = 10000;
            Assert.AreNotEqual(changed, stronger);
        }

        [Test]
        public void ShouldPreserveSavedUvOptOutWhenDefaultsAreEnabled()
        {
            var saved = FaQemOptions.Default;
            saved.ExperimentalUvEnabled = false;
            saved.ExperimentalJointUv = false;
            saved.ExperimentalUvWeight = 10;
            var restored = UnityEngine.JsonUtility.FromJson<FaQemOptions>(UnityEngine.JsonUtility.ToJson(saved)).Effective;
            Assert.AreEqual(saved, restored);
            Assert.IsFalse(restored.ExperimentalUvEnabled);
            Assert.IsFalse(restored.ExperimentalJointUv);
            Assert.AreEqual(10, restored.ExperimentalUvWeight);
            foreach (var options in new[] { MeshSimplifierOptions.Default, MeshSimplifierOptions.AvatarInitial,
                         MeshSimplifierOptions.ConservativeAvatar })
            {
                Assert.IsTrue(options.FaQem.ExperimentalUvEnabled);
                Assert.IsTrue(options.FaQem.ExperimentalJointUv);
                Assert.AreEqual(5000, options.FaQem.ExperimentalUvWeight);
            }
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void ShouldRejectInvalidUvWeights(float weight)
        {
            var options = FaQemOptions.Default;
            options.ExperimentalUvWeight = weight;
            Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        }

        [TestCase(0d)]
        [TestCase(100d)]
        [TestCase(1000d)]
        public void ShouldPreserveZeroErrorAfterRecenteringTranslatedSource(double offset)
        {
            var shift = new double3(offset, -offset, offset);
            var a = new double3(.1,.2,.3) + shift;
            var b = new double3(1.3,.4,.6) + shift;
            var c = new double3(.2,1.5,.9) + shift;
            var min = math.min(a, math.min(b,c)); var max = math.max(a,math.max(b,c));
            var center = (min+max)*.5;var scale=math.length(max-min);
            a=(a-center)/scale;b=(b-center)/scale;c=(c-center)/scale;
            var ua=new double2(.1,.2);var ub=new double2(.8,.2);var uc=new double2(.1,.9);
            var q=FaQemUvQuadric.FromTriangle(a,b,c,ua,ub,uc);
            Assert.That(q.ComputeError(a*.2+b*.3+c*.5,ua*.2+ub*.3+uc*.5),Is.EqualTo(0).Within(1e-10));
        }
    }
}
