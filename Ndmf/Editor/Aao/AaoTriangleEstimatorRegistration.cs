#nullable enable

using System;
using Anatawa12.AvatarOptimizer.API;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Meshia.MeshSimplification.Ndmf.Editor.Aao
{
    internal static class AaoTriangleEstimatorRegistration
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            DownstreamTriangleEstimator.RegisterAaoCounter(CountSurvivingTriangles);
        }

        private static int CountSurvivingTriangles(SkinnedMeshRenderer renderer)
        {
            var mesh = renderer.sharedMesh;
            if (mesh == null)
            {
                return 0;
            }

            using var removalProvider = MeshRemovalProvider.GetForRenderer(renderer);
            var survivingTriangleCount = 0;
            var triangle = new int[3];

            for (var subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
            {
                if (mesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
                {
                    continue;
                }

                var indices = mesh.GetIndices(subMeshIndex);
                if (removalProvider == null)
                {
                    survivingTriangleCount += indices.Length / 3;
                    continue;
                }

                for (var index = 0; index + 2 < indices.Length; index += 3)
                {
                    triangle[0] = indices[index];
                    triangle[1] = indices[index + 1];
                    triangle[2] = indices[index + 2];
                    if (!removalProvider.WillRemovePrimitive(MeshTopology.Triangles, subMeshIndex, triangle))
                    {
                        survivingTriangleCount++;
                    }
                }
            }

            return survivingTriangleCount;
        }
    }
}
