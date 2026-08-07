namespace Meshia.MeshSimplification
{
    /// <summary>
    /// Describes which stages contributed to a mesh simplification result.
    /// </summary>
    public readonly struct MeshSimplificationReport
    {
        internal MeshSimplificationReport(
            int uvLoopDissolvePassCount,
            int uvLoopDissolvedTriangleCount,
            bool usedBlenderFallback)
        {
            UvLoopDissolvePassCount = uvLoopDissolvePassCount;
            UvLoopDissolvedTriangleCount = uvLoopDissolvedTriangleCount;
            UsedBlenderFallback = usedBlenderFallback;
        }

        /// <summary>The number of accepted UV loop-dissolve passes.</summary>
        public int UvLoopDissolvePassCount { get; }

        /// <summary>The number of triangles removed by UV loop-dissolve passes.</summary>
        public int UvLoopDissolvedTriangleCount { get; }

        /// <summary>Whether Blender Decimate was used to finish the requested target.</summary>
        public bool UsedBlenderFallback { get; }

    }

    internal static class UvLoopDissolveDiagnostics
    {
        public const int PassCount = 0;
        public const int DissolvedTriangleCount = 1;
        public const int UsedBlenderFallback = 2;
        public const int LoopPhaseStopped = 3;
        public const int Length = 4;
    }
}
