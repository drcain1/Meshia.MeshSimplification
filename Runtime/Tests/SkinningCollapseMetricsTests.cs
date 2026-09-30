#nullable enable
using System;
using NUnit.Framework;

namespace Meshia.MeshSimplification.Tests
{
    public sealed class SkinningCollapseMetricsTests
    {
        [Test]
        public void TotalVariationIsSymmetricAndOrderIndependent()
        {
            var aIndices = new uint[] { 1, 4, 7, 9 }; var aWeights = new[] { .5f, .3f, .15f, .05f };
            var bIndices = new uint[] { 9, 7, 4, 1 }; var bWeights = new[] { .05f, .15f, .3f, .5f };
            Assert.That(SkinningCollapseMetrics.TotalVariation(aIndices, aWeights, bIndices, bWeights), Is.EqualTo(0f).Within(1e-6f));
            Assert.That(SkinningCollapseMetrics.TotalVariation(aIndices, aWeights, bIndices, bWeights),
                Is.EqualTo(SkinningCollapseMetrics.TotalVariation(bIndices, bWeights, aIndices, aWeights)).Within(1e-6f));
        }

        [Test]
        public void TotalVariationAggregatesDuplicateBoneSlots()
        {
            var splitIndices = new uint[] { 2, 2, 7, 0 };
            var splitWeights = new[] { .25f, .25f, .5f, 0f };
            var joinedIndices = new uint[] { 7, 2, 0, 0 };
            var joinedWeights = new[] { .5f, .5f, 0f, 0f };
            Assert.That(SkinningCollapseMetrics.TotalVariation(splitIndices, splitWeights, joinedIndices, joinedWeights),
                Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void TotalVariationIsInvariantToWeightNormalization()
        {
            var indices = new uint[] { 2, 5, 8, 0 };
            var aWeights = new[] { .6f, .25f, .15f, 0f };
            var bWeights = new[] { .2f, .5f, .3f, 0f };
            var scaledA = new[] { 6f, 2.5f, 1.5f, 0f };
            var scaledB = new[] { 2f, 5f, 3f, 0f };
            var expected = SkinningCollapseMetrics.TotalVariation(indices, aWeights, indices, bWeights);
            Assert.That(SkinningCollapseMetrics.TotalVariation(indices, scaledA, indices, scaledB),
                Is.EqualTo(expected).Within(1e-6f));
        }

        [Test]
        public void RigidSameBoneHasZeroDistanceAndNoDiscard()
        {
            var indices = new uint[] { 3, 0, 0, 0 }; var weights = new[] { 1f, 0f, 0f, 0f };
            Span<uint> outputIndices = stackalloc uint[4]; Span<float> outputWeights = stackalloc float[4];
            Assert.That(SkinningCollapseMetrics.TotalVariation(indices, weights, indices, weights), Is.EqualTo(0f));
            Assert.That(SkinningCollapseMetrics.TrySimulateMerged(indices, weights, indices, weights, .5f,
                outputIndices, outputWeights, out var discarded), Is.True);
            Assert.That(discarded, Is.EqualTo(0f));
            Assert.That(outputWeights[0], Is.EqualTo(1f));
        }

        [Test]
        public void FixedWidthMergeReportsDiscardedInfluenceAndNormalizes()
        {
            var aIndices = new uint[] { 1, 2, 3, 4 }; var aWeights = new[] { .7f, .2f, .1f, 0f };
            var bIndices = new uint[] { 1, 2, 5, 6 }; var bWeights = new[] { .6f, .2f, .1f, .1f };
            Span<uint> outputIndices = stackalloc uint[2]; Span<float> outputWeights = stackalloc float[2];
            Assert.That(SkinningCollapseMetrics.TrySimulateMerged(aIndices, aWeights, bIndices, bWeights, .5f,
                outputIndices, outputWeights, out var discarded), Is.True);
            Assert.That(discarded, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(outputWeights[0] + outputWeights[1], Is.EqualTo(1f).Within(1e-6f));
        }
    }
}
