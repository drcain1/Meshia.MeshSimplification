#if ENABLE_MODULAR_AVATAR

using NUnit.Framework;
using Meshia.MeshSimplification.Ndmf.Editor;
using UnityEngine;

namespace Meshia.MeshSimplification.Ndmf.Tests
{
    public class MeshiaCascadingAvatarMeshSimplifierTests
    {
        [TestCase("Body", false)]
        [TestCase("body", false)]
        [TestCase("BODY", false)]
        [TestCase("Body_base", true)]
        [TestCase("Hair", true)]
        public void ShouldExcludeBodyByDefaultWithoutOverwritingUserChoices(string name, bool enabled)
        {
            var avatar = new GameObject("Default test");
            var meshObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            meshObject.name = name;
            meshObject.transform.SetParent(avatar.transform);
            var settings = new GameObject("Settings");
            settings.transform.SetParent(avatar.transform);
            try
            {
                var component = settings.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
                component.RefreshEntries();
                Assert.AreEqual(enabled, component.Entries[0].Enabled);
                component.Entries[0].Enabled = !enabled;
                component.RefreshEntries();
                Assert.AreEqual(!enabled, component.Entries[0].Enabled, "Refresh must keep a manual override.");
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void ShouldPreserveAlgorithmValuesAndMapUvTarget()
        {
            Assert.AreEqual(0, (int)MeshiaCascadingSimplificationAlgorithm.BlenderDecimate);
            Assert.AreEqual(1, (int)MeshiaCascadingSimplificationAlgorithm.Meshia);
            Assert.AreEqual(2, (int)MeshiaCascadingSimplificationAlgorithm.UvLoopDissolve);
            Assert.AreEqual(3, (int)MeshiaCascadingSimplificationAlgorithm.FaQem);

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

        [Test]
        public void ShouldDefaultNewEntriesToFaQemTarget()
        {
            var gameObject = new GameObject("Meshia FA-QEM target test");
            try
            {
                var entry = new MeshiaCascadingAvatarMeshSimplifierRendererEntry(
                    gameObject.AddComponent<MeshRenderer>())
                {
                    TargetTriangleCount = 123,
                };

                var target = entry.CreateTarget(1000);
                Assert.AreEqual(MeshiaCascadingSimplificationAlgorithm.FaQem, entry.Algorithm);
                Assert.AreEqual(MeshSimplificationTargetKind.FaQemTriangleCount, target.Kind);
                Assert.AreEqual(123, target.Value);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ShouldKeepSerializedAlgorithmSelection(int savedAlgorithm)
        {
            var entry = JsonUtility.FromJson<MeshiaCascadingAvatarMeshSimplifierRendererEntry>(
                "{\"Algorithm\":" + savedAlgorithm + ",\"TargetTriangleCount\":123,\"Enabled\":false}");

            Assert.AreEqual((MeshiaCascadingSimplificationAlgorithm)savedAlgorithm, entry.Algorithm);
            Assert.AreEqual(123, entry.TargetTriangleCount);
            Assert.IsFalse(entry.Enabled);
        }

        [Test]
        public void ShouldKeepLegacyAlgorithmWhenSerializedSelectionIsMissing()
        {
            var entry = JsonUtility.FromJson<MeshiaCascadingAvatarMeshSimplifierRendererEntry>(
                "{\"TargetTriangleCount\":123}");

            Assert.AreEqual(MeshiaCascadingSimplificationAlgorithm.BlenderDecimate, entry.Algorithm);
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
