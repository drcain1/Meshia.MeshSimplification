#nullable enable
using System;
using UnityEngine;

namespace Meshia.MeshSimplification
{
    internal enum HumanoidSkinningCoverageFailure
    {
        None,
        EmptyMesh,
        InvalidMapping,
        MissingTorso,
        MissingLeftUpperArm,
        MissingLeftLowerArm,
        MissingRightUpperArm,
        MissingRightLowerArm,
        MissingLeftUpperLeg,
        MissingLeftLowerLeg,
        MissingRightUpperLeg,
        MissingRightLowerLeg,
    }

    internal readonly struct HumanoidSkinningCoverageResult
    {
        internal readonly bool IsAnatomicalBody;
        internal readonly float Score;
        internal readonly HumanoidSkinningCoverageFailure Failure;

        internal HumanoidSkinningCoverageResult(bool isAnatomicalBody, float score, HumanoidSkinningCoverageFailure failure)
        {
            IsAnatomicalBody = isAnatomicalBody;
            Score = score;
            Failure = failure;
        }
    }

    /// <summary>Conservative, renderer-name-independent classification of a skinned anatomical body.</summary>
    internal static class HumanoidSkinningCoverage
    {
        const float MinimumInfluence = 1e-5f;
        const float MinimumGroupMass = 0.005f;
        const int RequiredGroups = 9;

        internal static HumanoidSkinningCoverageResult Evaluate(int vertexCount, int influencesPerVertex,
            ReadOnlySpan<int> boneToHumanoid, ReadOnlySpan<int> boneIndices, ReadOnlySpan<float> weights)
        {
            if (vertexCount <= 0 || influencesPerVertex <= 0) return Fail(HumanoidSkinningCoverageFailure.EmptyMesh, 0f);
            if (boneIndices.Length != vertexCount * influencesPerVertex || weights.Length != boneIndices.Length)
                return Fail(HumanoidSkinningCoverageFailure.InvalidMapping, 0f);

            var groupMass = new float[RequiredGroups];
            var totalPositive = 0f;
            for (var vertex = 0; vertex < vertexCount; vertex++)
            {
                var offset = vertex * influencesPerVertex;
                for (var slot = 0; slot < influencesPerVertex; slot++)
                {
                    var weight = weights[offset + slot];
                    var bone = boneIndices[offset + slot];
                    if (weight < 0f || float.IsNaN(weight) || float.IsInfinity(weight) || bone < 0 || bone >= boneToHumanoid.Length)
                        return Fail(HumanoidSkinningCoverageFailure.InvalidMapping, 0f);
                    if (weight <= MinimumInfluence) continue;
                    totalPositive += weight;
                    var group = GroupFor(boneToHumanoid[bone]);
                    if (group >= 0) groupMass[group] += weight;
                }
            }

            if (!(totalPositive > MinimumInfluence)) return Fail(HumanoidSkinningCoverageFailure.MissingTorso, 0f);
            var groups = new bool[RequiredGroups];
            for (var i = 0; i < groups.Length; i++) groups[i] = groupMass[i] / totalPositive >= MinimumGroupMass;
            if (!groups[0]) return Fail(HumanoidSkinningCoverageFailure.MissingTorso, Score(groups));
            if (!groups[1]) return Fail(HumanoidSkinningCoverageFailure.MissingLeftUpperArm, Score(groups));
            if (!groups[2]) return Fail(HumanoidSkinningCoverageFailure.MissingLeftLowerArm, Score(groups));
            if (!groups[3]) return Fail(HumanoidSkinningCoverageFailure.MissingRightUpperArm, Score(groups));
            if (!groups[4]) return Fail(HumanoidSkinningCoverageFailure.MissingRightLowerArm, Score(groups));
            if (!groups[5]) return Fail(HumanoidSkinningCoverageFailure.MissingLeftUpperLeg, Score(groups));
            if (!groups[6]) return Fail(HumanoidSkinningCoverageFailure.MissingLeftLowerLeg, Score(groups));
            if (!groups[7]) return Fail(HumanoidSkinningCoverageFailure.MissingRightUpperLeg, Score(groups));
            if (!groups[8]) return Fail(HumanoidSkinningCoverageFailure.MissingRightLowerLeg, Score(groups));
            return new HumanoidSkinningCoverageResult(true, 1f, HumanoidSkinningCoverageFailure.None);
        }

        static int GroupFor(int humanoid)
        {
            var bone = (HumanBodyBones)humanoid;
            if (bone is HumanBodyBones.Hips or HumanBodyBones.Spine or HumanBodyBones.Chest or HumanBodyBones.UpperChest) return 0;
            if (bone is HumanBodyBones.LeftShoulder or HumanBodyBones.LeftUpperArm) return 1;
            if (bone is HumanBodyBones.LeftLowerArm) return 2;
            if (bone is HumanBodyBones.RightShoulder or HumanBodyBones.RightUpperArm) return 3;
            if (bone is HumanBodyBones.RightLowerArm) return 4;
            if (bone is HumanBodyBones.LeftUpperLeg) return 5;
            if (bone is HumanBodyBones.LeftLowerLeg) return 6;
            if (bone is HumanBodyBones.RightUpperLeg) return 7;
            if (bone is HumanBodyBones.RightLowerLeg) return 8;
            return -1;
        }

        static float Score(bool[] groups)
        {
            var count = 0;
            for (var i = 0; i < groups.Length; i++) if (groups[i]) count++;
            return count / (float)RequiredGroups;
        }

        static HumanoidSkinningCoverageResult Fail(HumanoidSkinningCoverageFailure failure, float score)
            => new(false, score, failure);
    }
}
