#nullable enable
using System;

namespace Meshia.MeshSimplification
{
    internal static class SkinningCollapseMetrics
    {
        // Aggregate duplicate slots and break ties by bone index so influence ordering
        // cannot move the protected transition ring.
        internal static int DominantBone(ReadOnlySpan<uint> indices, ReadOnlySpan<float> weights)
        {
            var best = -1;
            var maximum = 0f;
            for (var i = 0; i < indices.Length; i++)
            {
                var weight = 0f;
                for (var j = 0; j < indices.Length; j++)
                    if (indices[j] == indices[i]) weight += MathF.Max(0f, weights[j]);
                var bone = (int)indices[i];
                if (weight > maximum || (weight == maximum && weight > 0f && bone < best))
                {
                    best = bone;
                    maximum = weight;
                }
            }
            return best;
        }

        internal static float TotalVariation(ReadOnlySpan<uint> indicesA, ReadOnlySpan<float> weightsA,
            ReadOnlySpan<uint> indicesB, ReadOnlySpan<float> weightsB)
        {
            if (indicesA.Length != weightsA.Length || indicesB.Length != weightsB.Length) throw new ArgumentException("Bone index and weight dimensions must match.");
            var sumA = SumPositive(weightsA); var sumB = SumPositive(weightsB);
            if (sumA <= 1e-8f || sumB <= 1e-8f) return sumA <= 1e-8f && sumB <= 1e-8f ? 0f : 1f;
            var distance = 0f;
            for (var i = 0; i < indicesA.Length; i++)
            {
                var alreadyCounted = false;
                for (var k = 0; k < i; k++) if (indicesA[k] == indicesA[i]) { alreadyCounted = true; break; }
                if (alreadyCounted) continue;
                var weightA = 0f;
                var weightB = 0f;
                for (var j = 0; j < indicesA.Length; j++) if (indicesA[j] == indicesA[i]) weightA += MathF.Max(0f, weightsA[j]) / sumA;
                for (var j = 0; j < indicesB.Length; j++) if (indicesB[j] == indicesA[i]) weightB += MathF.Max(0f, weightsB[j]) / sumB;
                distance += MathF.Abs(weightA - weightB);
            }
            for (var i = 0; i < indicesB.Length; i++)
            {
                var alreadyCounted = false;
                for (var k = 0; k < i; k++) if (indicesB[k] == indicesB[i]) { alreadyCounted = true; break; }
                if (alreadyCounted) continue;
                var found = false;
                for (var j = 0; j < indicesA.Length; j++) if (indicesA[j] == indicesB[i]) { found = true; break; }
                if (!found)
                    for (var j = 0; j < indicesB.Length; j++) if (indicesB[j] == indicesB[i]) distance += MathF.Max(0f, weightsB[j]) / sumB;
            }
            return MathF.Min(1f, .5f * distance);
        }

        internal static bool TrySimulateMerged(ReadOnlySpan<uint> indicesA, ReadOnlySpan<float> weightsA,
            ReadOnlySpan<uint> indicesB, ReadOnlySpan<float> weightsB, float lerpFactor,
            Span<uint> mergedIndices, Span<float> mergedWeights, out float discardedWeight)
        {
            if (indicesA.Length != weightsA.Length || indicesB.Length != weightsB.Length || mergedIndices.Length != mergedWeights.Length)
                throw new ArgumentException("Bone index and weight dimensions must match.");
            if (float.IsNaN(lerpFactor) || float.IsInfinity(lerpFactor)) { discardedWeight = 0f; return false; }
            var count = indicesA.Length + indicesB.Length;
            // Unity skinning uses a small fixed influence width. Refuse an
            // oversized contract rather than allocating in a Burst/job path.
            if (count > 64) { discardedWeight = 0f; return false; }
            if (mergedIndices.Length == 0) { discardedWeight = 0f; return true; }
            Span<uint> unionIndices = stackalloc uint[count];
            Span<float> unionWeights = stackalloc float[count];
            var unionCount = 0;
            for (var i = 0; i < indicesA.Length; i++) Add(unionIndices, unionWeights, ref unionCount, indicesA[i], weightsA[i] * (1f - lerpFactor));
            for (var i = 0; i < indicesB.Length; i++) Add(unionIndices, unionWeights, ref unionCount, indicesB[i], weightsB[i] * lerpFactor);

            var totalPositive = 0f;
            for (var i = 0; i < unionCount; i++) totalPositive += MathF.Max(0f, unionWeights[i]);
            var usedPositive = 0f;
            for (var output = 0; output < mergedIndices.Length; output++)
            {
                if (output >= unionCount)
                {
                    mergedIndices[output] = 0u;
                    mergedWeights[output] = 0f;
                    continue;
                }
                var best = 0;
                var bestWeight = float.NegativeInfinity;
                for (var i = 0; i < unionCount; i++)
                {
                    if (unionWeights[i] > bestWeight) { best = i; bestWeight = unionWeights[i]; }
                }
                mergedIndices[output] = unionCount == 0 ? 0u : unionIndices[best];
                mergedWeights[output] = unionCount == 0 ? 0f : bestWeight;
                if (unionCount != 0) { usedPositive += MathF.Max(0f, bestWeight); unionWeights[best] = float.NegativeInfinity; }
            }
            discardedWeight = totalPositive <= 1e-8f ? 0f : Clamp01((totalPositive - usedPositive) / totalPositive);
            var sum = 0f;
            for (var i = 0; i < mergedWeights.Length; i++) sum += mergedWeights[i];
            if (!(sum > 1e-8f) || float.IsNaN(sum) || float.IsInfinity(sum)) return false;
            for (var i = 0; i < mergedWeights.Length; i++) mergedWeights[i] /= sum;
            return true;
        }

        private static float Clamp01(float value) => MathF.Max(0f, MathF.Min(1f, value));

        private static void Add(Span<uint> indices, Span<float> weights, ref int count, uint index, float weight)
        {
            for (var i = 0; i < count; i++) if (indices[i] == index) { weights[i] += weight; return; }
            if (count < indices.Length) { indices[count] = index; weights[count] = weight; count++; }
        }

        private static float SumPositive(ReadOnlySpan<float> weights)
        {
            var sum = 0f;
            for (var i = 0; i < weights.Length; i++) sum += MathF.Max(0f, weights[i]);
            return sum;
        }
    }
}
