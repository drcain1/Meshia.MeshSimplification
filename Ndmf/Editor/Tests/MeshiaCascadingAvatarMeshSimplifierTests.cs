#if ENABLE_MODULAR_AVATAR

using NUnit.Framework;
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
    }
}

#endif
