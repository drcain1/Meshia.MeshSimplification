#nullable enable
#if ENABLE_MODULAR_AVATAR

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Meshia.MeshSimplification.Ndmf.Editor
{
    internal static class DownstreamTriangleEstimator
    {
        internal delegate int AaoSurvivingTriangleCounter(SkinnedMeshRenderer renderer);

        private readonly struct CacheEntry
        {
            internal readonly Mesh Mesh;
            internal readonly int SourceTriangleCount;
            internal readonly int SurvivingTriangleCount;

            internal CacheEntry(Mesh mesh, int sourceTriangleCount, int survivingTriangleCount)
            {
                Mesh = mesh;
                SourceTriangleCount = sourceTriangleCount;
                SurvivingTriangleCount = survivingTriangleCount;
            }
        }

        private static readonly Dictionary<SkinnedMeshRenderer, CacheEntry> AaoCache = new();
        private static AaoSurvivingTriangleCounter? s_aaoCounter;

        internal static bool IsAaoAvailable => s_aaoCounter != null;

        internal static void RegisterAaoCounter(AaoSurvivingTriangleCounter counter)
        {
            s_aaoCounter = counter ?? throw new ArgumentNullException(nameof(counter));
            Invalidate();
        }

        internal static void Invalidate()
        {
            AaoCache.Clear();
        }

        internal static int EstimateFinalTriangleCount(Renderer renderer, int currentTriangleCount)
        {
            if (currentTriangleCount <= 0 || renderer is not SkinnedMeshRenderer skinnedRenderer ||
                s_aaoCounter == null || RendererUtility.GetMesh(renderer) is not { } sourceMesh)
            {
                return Math.Max(0, currentTriangleCount);
            }

            var sourceTriangleCount = sourceMesh.GetTriangleCount();
            if (sourceTriangleCount <= 0)
            {
                return Math.Max(0, currentTriangleCount);
            }

            if (!AaoCache.TryGetValue(skinnedRenderer, out var cacheEntry) ||
                cacheEntry.Mesh != sourceMesh || cacheEntry.SourceTriangleCount != sourceTriangleCount)
            {
                int survivingTriangleCount;
                try
                {
                    survivingTriangleCount = Mathf.Clamp(s_aaoCounter(skinnedRenderer), 0, sourceTriangleCount);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, skinnedRenderer);
                    survivingTriangleCount = sourceTriangleCount;
                }
                cacheEntry = new CacheEntry(sourceMesh, sourceTriangleCount, survivingTriangleCount);
                AaoCache[skinnedRenderer] = cacheEntry;
            }

            return ScaleTriangleCount(
                currentTriangleCount,
                cacheEntry.SourceTriangleCount,
                cacheEntry.SurvivingTriangleCount);
        }

        internal static int ScaleTriangleCount(
            int currentTriangleCount,
            int sourceTriangleCount,
            int survivingSourceTriangleCount)
        {
            if (currentTriangleCount <= 0 || sourceTriangleCount <= 0)
            {
                return Math.Max(0, currentTriangleCount);
            }

            var survivingRatio = Mathf.Clamp01(survivingSourceTriangleCount / (float)sourceTriangleCount);
            return Mathf.Clamp(Mathf.CeilToInt(currentTriangleCount * survivingRatio), 0, currentTriangleCount);
        }

        internal static int ApplyAnalyzedDelta(
            int currentEstimate,
            int analyzedEstimate,
            int analyzedFinalTriangleCount)
        {
            var downstreamDelta = (long)analyzedFinalTriangleCount - analyzedEstimate;
            return (int)Math.Min(int.MaxValue, Math.Max(0L, currentEstimate + downstreamDelta));
        }

        internal static int GetPreDownstreamTarget(
            int finalTargetTriangleCount,
            int analyzedEstimate,
            int analyzedFinalTriangleCount)
        {
            var downstreamDelta = (long)analyzedFinalTriangleCount - analyzedEstimate;
            return (int)Math.Min(int.MaxValue, Math.Max(0L, finalTargetTriangleCount - downstreamDelta));
        }
    }
}

#endif
