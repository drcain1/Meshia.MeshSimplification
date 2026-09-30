#nullable enable
using NUnit.Framework;
using UnityEngine;

namespace Meshia.MeshSimplification.Tests
{
    public sealed class HumanoidSkinningCoverageTests
    {
        static int H(HumanBodyBones bone) => (int)bone;

        [Test]
        public void AnatomicalBodyRequiresTorsoAndBothLimbSides()
        {
            var mapping = new[] { H(HumanBodyBones.Hips), H(HumanBodyBones.LeftUpperArm), H(HumanBodyBones.LeftLowerArm), H(HumanBodyBones.RightUpperArm), H(HumanBodyBones.RightLowerArm), H(HumanBodyBones.LeftUpperLeg), H(HumanBodyBones.LeftLowerLeg), H(HumanBodyBones.RightUpperLeg), H(HumanBodyBones.RightLowerLeg) };
            var indices = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
            var weights = new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
            var result = HumanoidSkinningCoverage.Evaluate(9, 1, mapping, indices, weights);
            Assert.That(result.IsAnatomicalBody, Is.True);
            Assert.That(result.Score, Is.EqualTo(1f));
            Assert.That(result.Failure, Is.EqualTo(HumanoidSkinningCoverageFailure.None));
        }

        [Test]
        public void FaceHeadOnlyAndLimbLimitedMeshesAreRejected()
        {
            var face = HumanoidSkinningCoverage.Evaluate(1, 1, new[] { H(HumanBodyBones.Head) }, new[] { 0 }, new[] { 1f });
            Assert.That(face.Failure, Is.EqualTo(HumanoidSkinningCoverageFailure.MissingTorso));
            var limb = HumanoidSkinningCoverage.Evaluate(2, 1,
                new[] { H(HumanBodyBones.Hips), H(HumanBodyBones.LeftUpperArm) }, new[] { 0, 1 }, new[] { 1f, 1f });
            Assert.That(limb.Failure, Is.EqualTo(HumanoidSkinningCoverageFailure.MissingLeftLowerArm));
        }

        [Test]
        public void TinyNonzeroGroupAndOneSidedLimbsDoNotCountAsBodyCoverage()
        {
            var mapping = new[] { H(HumanBodyBones.Hips), H(HumanBodyBones.LeftUpperArm), H(HumanBodyBones.LeftLowerArm), H(HumanBodyBones.RightUpperArm), H(HumanBodyBones.RightLowerArm), H(HumanBodyBones.LeftUpperLeg), H(HumanBodyBones.LeftLowerLeg), H(HumanBodyBones.RightUpperLeg), H(HumanBodyBones.RightLowerLeg) };
            var indices = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 1 };
            var weights = new[] { 1f, 1f, .0001f, 1f, 1f, 1f, 1f, 1f, 1f, 0f };
            var result = HumanoidSkinningCoverage.Evaluate(10, 1, mapping, indices, weights);
            Assert.That(result.IsAnatomicalBody, Is.False);
            Assert.That(result.Failure, Is.EqualTo(HumanoidSkinningCoverageFailure.MissingLeftLowerArm));
        }

        [Test]
        public void EmptyNullAndMalformedBoneMappingsFailClosed()
        {
            Assert.That(HumanoidSkinningCoverage.Evaluate(0, 1, System.Array.Empty<int>(), System.Array.Empty<int>(), System.Array.Empty<float>()).Failure,
                Is.EqualTo(HumanoidSkinningCoverageFailure.EmptyMesh));
            Assert.That(HumanoidSkinningCoverage.Evaluate(1, 1, System.Array.Empty<int>(), new[] { 0 }, new[] { 1f }).Failure,
                Is.EqualTo(HumanoidSkinningCoverageFailure.InvalidMapping));
            Assert.That(HumanoidSkinningCoverage.Evaluate(1, 1, new[] { H(HumanBodyBones.Hips) }, new[] { 0 }, new[] { float.NaN }).Failure,
                Is.EqualTo(HumanoidSkinningCoverageFailure.InvalidMapping));
        }
    }
}
