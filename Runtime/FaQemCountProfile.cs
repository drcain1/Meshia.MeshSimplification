#nullable enable
using System;

namespace Meshia.MeshSimplification
{
    /// <summary>Triangle counts along one unchanged FA-QEM collapse sequence.</summary>
    /// <remarks>Valid only for the exact source mesh and resolved options used to record it.</remarks>
    public sealed class FaQemCountProfile
    {
        private readonly int[] counts;
        /// <summary>The lowest request covered by this profile.</summary>
        public int MinimumTarget { get; }
        /// <summary>The output at the end of the recorded sequence.</summary>
        public int FinalCount => counts[counts.Length - 1];
        /// <summary>Whether the algorithm stopped before reaching its requested count.</summary>
        public bool ReachedLimit { get; }

        internal FaQemCountProfile(int requested, int[] counts)
        {
            if (counts.Length == 0) throw new ArgumentException("An initial count is required.", nameof(counts));
            this.counts = counts;
            ReachedLimit = FinalCount > Math.Max(0, requested);
            MinimumTarget = ReachedLimit ? 0 : Math.Max(0, requested);
        }

        /// <summary>Looks up an exact output count without rerunning simplification.</summary>
        public bool TryGetOutput(int requested, out int output)
        {
            requested = Math.Max(0, requested);
            output = 0;
            if (requested < MinimumTarget) return false;
            // The target is only a stopping condition. Include the pre-cleanup
            // count: requesting the original count must skip degenerate cleanup.
            var low = 0;
            var high = counts.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (counts[middle] > requested) low = middle + 1;
                else high = middle;
            }
            output = counts[Math.Min(low, counts.Length - 1)];
            return true;
        }
    }
}
