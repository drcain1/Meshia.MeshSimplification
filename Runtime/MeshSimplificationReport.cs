namespace Meshia.MeshSimplification
{
    /// <summary>Explains completion of a feature-aware simplification attempt.</summary>
    public enum FaQemStopReason
    {
        /// <summary>The selected algorithm was not FA-QEM.</summary>
        NotRequested,
        /// <summary>The output satisfies the requested triangle budget.</summary>
        TargetReached,
        /// <summary>The active constraints left no accepted collapse to reach the budget.</summary>
        ConstraintsExhausted,
    }
    /// <summary>
    /// Describes which stages contributed to a mesh simplification result.
    /// </summary>
    public readonly struct MeshSimplificationReport
    {
        internal MeshSimplificationReport(
            int uvLoopDissolvePassCount,
            int uvLoopDissolvedTriangleCount,
            bool usedBlenderFallback,
            bool usedFaQem = false,
            int inputTriangleCount = 0,
            int outputTriangleCount = 0,
            int requestedTriangleCount = 0)
        {
            UvLoopDissolvePassCount = uvLoopDissolvePassCount;
            UvLoopDissolvedTriangleCount = uvLoopDissolvedTriangleCount;
            UsedBlenderFallback = usedBlenderFallback;
            UsedFaQem = usedFaQem;
            InputTriangleCount = inputTriangleCount;
            OutputTriangleCount = outputTriangleCount;
            RequestedTriangleCount = requestedTriangleCount;
        }

        /// <summary>The number of accepted UV loop-dissolve passes.</summary>
        public int UvLoopDissolvePassCount { get; }

        /// <summary>The number of triangles removed by UV loop-dissolve passes.</summary>
        public int UvLoopDissolvedTriangleCount { get; }

        /// <summary>Whether Blender Decimate was used to finish the requested target.</summary>
        public bool UsedBlenderFallback { get; }

        /// <summary>Whether the feature-aware algorithm was selected.</summary>
        public bool UsedFaQem { get; }
        /// <summary>The source triangle count before simplification.</summary>
        public int InputTriangleCount { get; }
        /// <summary>The actual output triangle count.</summary>
        public int OutputTriangleCount { get; }
        /// <summary>The requested FA-QEM triangle budget, or zero for other algorithms.</summary>
        public int RequestedTriangleCount { get; }
        /// <summary>Whether the feature-aware triangle budget was reached or constrained.</summary>
        public FaQemStopReason FaQemTermination => !UsedFaQem ? FaQemStopReason.NotRequested :
            OutputTriangleCount <= RequestedTriangleCount ? FaQemStopReason.TargetReached : FaQemStopReason.ConstraintsExhausted;

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
