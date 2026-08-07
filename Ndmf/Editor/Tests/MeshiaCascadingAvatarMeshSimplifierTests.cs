#if ENABLE_MODULAR_AVATAR

using NUnit.Framework;
using Meshia.MeshSimplification.Ndmf.Editor;
using UnityEngine;

namespace Meshia.MeshSimplification.Ndmf.Tests
{
    public class MeshiaCascadingAvatarMeshSimplifierTests
    {
        [Test]
        public void ShouldPreserveAlgorithmValuesAndMapUvTarget()
        {
            Assert.AreEqual(0, (int)MeshiaCascadingSimplificationAlgorithm.BlenderDecimate);
            Assert.AreEqual(1, (int)MeshiaCascadingSimplificationAlgorithm.Meshia);
            Assert.AreEqual(2, (int)MeshiaCascadingSimplificationAlgorithm.UvLoopDissolve);

            var gameObject = new GameObject("Meshia NDMF target test");
            try
            {
                var entry = new MeshiaCascadingAvatarMeshSimplifierRendererEntry(
                    gameObject.AddComponent<MeshRenderer>())
                {
                    Algorithm = MeshiaCascadingSimplificationAlgorithm.UvLoopDissolve,
                    TargetTriangleCount = 123,
                };

                var target = entry.CreateTarget(1000);
                Assert.AreEqual(MeshSimplificationTargetKind.UvLoopDissolveTriangleCount, target.Kind);
                Assert.AreEqual(123, target.Value);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [TestCase(80000, 100000, 75000, 60000)]
        [TestCase(1, 3, 1, 1)]
        [TestCase(70000, 0, 0, 70000)]
        [TestCase(70000, 100000, 0, 0)]
        public void ShouldConservativelyEstimatePostAaoTriangleCount(
            int currentTriangleCount,
            int sourceTriangleCount,
            int survivingSourceTriangleCount,
            int expected)
        {
            Assert.AreEqual(
                expected,
                DownstreamTriangleEstimator.ScaleTriangleCount(
                    currentTriangleCount,
                    sourceTriangleCount,
                    survivingSourceTriangleCount));
        }

        [Test]
        public void ShouldApplyAnalyzedDownstreamDeltaToEstimateAndTarget()
        {
            const int analyzedEstimate = 81644;
            const int analyzedFinal = 70000;

            Assert.AreEqual(
                70000,
                DownstreamTriangleEstimator.ApplyAnalyzedDelta(
                    81644,
                    analyzedEstimate,
                    analyzedFinal));
            Assert.AreEqual(
                81644,
                DownstreamTriangleEstimator.GetPreDownstreamTarget(
                    70000,
                    analyzedEstimate,
                    analyzedFinal));
            Assert.AreEqual(
                65000,
                DownstreamTriangleEstimator.ApplyAnalyzedDelta(
                    76644,
                    analyzedEstimate,
                    analyzedFinal));
        }
    }
}

#endif
