#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Meshia.MeshSimplification.Ndmf.Editor
{
    // Owns a copy of the exact input seen by the build, plus results of bounded trials.
    internal sealed class MeasuredMeshResponse : IDisposable
    {
        internal readonly int Index;
        internal readonly int SourceCount;
        internal readonly Dictionary<int, int> Outputs = new();
        private readonly Func<int, int> evaluate;
        private readonly Func<int, Task<int>>? evaluateAsync;
        private readonly Mesh? ownedSource;
        private int pendingMeasurements;
        private bool disposed;

        internal MeasuredMeshResponse(int index, int sourceCount, int requested, int produced,
            Func<int, int> evaluate, Mesh? ownedSource = null, Func<int, Task<int>>? evaluateAsync = null)
        {
            Index = index;
            SourceCount = sourceCount;
            this.evaluate = evaluate;
            this.evaluateAsync = evaluateAsync;
            this.ownedSource = ownedSource;
            Outputs[requested] = produced;
        }

        internal int Measure(int requested)
        {
            if (disposed) throw new ObjectDisposedException(nameof(MeasuredMeshResponse));
            if (!Outputs.TryGetValue(requested, out var output))
            {
                output = evaluate(requested);
                if (output < 0 || output > SourceCount) throw new InvalidOperationException("Invalid mesh measurement.");
                Outputs[requested] = output;
            }
            return output;
        }

        // Invoked on Unity's main thread. The evaluator yields while Unity jobs run;
        // never fall back to the blocking evaluator from the inspector update path.
        internal async Task<int> MeasureAsync(int requested)
        {
            if (disposed) throw new ObjectDisposedException(nameof(MeasuredMeshResponse));
            if (Outputs.TryGetValue(requested, out var cached)) return cached;
            if (evaluateAsync == null) throw new InvalidOperationException("Async mesh measurement is unavailable.");
            pendingMeasurements++;
            try
            {
                var output = await evaluateAsync(requested);
                if (output < 0 || output > SourceCount) throw new InvalidOperationException("Invalid mesh measurement.");
                if (!disposed) Outputs[requested] = output;
                return output;
            }
            finally
            {
                pendingMeasurements--;
                if (disposed && pendingMeasurements == 0) ReleaseSource();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // An inspector can close, reload, or start a build while a job reads
            // this source. Retain it until that asynchronous operation finishes.
            if (pendingMeasurements == 0) ReleaseSource();
        }

        private void ReleaseSource()
        {
            if (ownedSource != null) UnityEngine.Object.DestroyImmediate(ownedSource);
        }
    }

    internal sealed class MeasuredMeshSet : IDisposable
    {
        internal string Settings = string.Empty;
        internal int InputRevision;
        internal readonly Dictionary<int, MeasuredMeshResponse> Meshes = new();
        public void Dispose()
        {
            foreach (var mesh in Meshes.Values) mesh.Dispose();
            Meshes.Clear();
        }
    }

    internal static class MeasuredMeshBudget
    {
        // No targets are changed here. A candidate earns budget only from measured
        // output changes. Flat, reversed, or wildly discontinuous probes earn none.
        internal static Dictionary<int, int> Plan(IReadOnlyList<MeasuredMeshResponse> meshes,
            IReadOnlyList<int> targets, long outputChange, Func<bool>? cancel = null)
        {
            var plan = new Dictionary<int, int>();
            if (outputChange == 0) return plan;
            var direction = Math.Sign(outputChange);
            var remaining = Math.Abs(outputChange);
            var ordered = meshes.OrderByDescending(m => targets[m.Index] / (double)Math.Max(1, m.Measure(targets[m.Index]))).ToArray();
            foreach (var mesh in ordered)
            {
                if (cancel?.Invoke() == true) throw new OperationCanceledException();
                var current = targets[mesh.Index];
                var produced = mesh.Measure(current);
                var room = direction < 0 ? current - 1 : mesh.SourceCount - current;
                if (room <= 0) continue;
                var step = (int)Math.Min(room, Math.Max(1L, Math.Max(current / 4, remaining)));
                var trial = current + direction * step;
                var output = mesh.Measure(trial);
                var gain = direction * (long)(output - produced);
                if (gain <= 0) continue;

                // Refine an oversized response with at most three measured probes.
                // Never substitute requested savings for a measured result.
                var lower = current;
                var upper = trial;
                long lowerGain = 0, upperGain = gain;
                var bestTarget = trial;
                var bestGain = direction > 0 && gain > remaining ? 0 : gain;
                for (var attempt = 0; gain > remaining && attempt < 3; attempt++)
                {
                    // Interpolate only between this mesh's measured bracket ends.
                    // Fall back to the middle if a nonmonotonic result breaks it.
                    var width = Math.Abs(upper - lower);
                    if (width <= 1) break;
                    var fraction = upperGain > lowerGain
                        ? Math.Max(0, Math.Min(1, (remaining - lowerGain) / (double)(upperGain - lowerGain))) : .5;
                    var offset = Math.Max(1, Math.Min(width - 1, (int)Math.Round(width * fraction)));
                    var midpoint = lower + Math.Sign(upper - lower) * offset;
                    if (midpoint == lower || midpoint == upper) break;
                    if (cancel?.Invoke() == true) throw new OperationCanceledException();
                    var middleGain = direction * (long)(mesh.Measure(midpoint) - produced);
                    if (middleGain > 0 && (direction < 0 || middleGain <= remaining) &&
                        Math.Abs(middleGain - remaining) < Math.Abs(bestGain - remaining))
                    { bestTarget = midpoint; bestGain = middleGain; }
                    if (middleGain == remaining) break;
                    if (middleGain > remaining) { upper = midpoint; upperGain = middleGain; }
                    else { lower = midpoint; lowerGain = middleGain; }
                }
                // A reduction may go slightly below the goal; never accept a larger
                // miss than the gap we are trying to close. Increases cannot overshoot.
                if (bestGain <= 0 || (bestGain > remaining && (direction > 0 || bestGain - remaining >= remaining))) continue;
                plan[mesh.Index] = bestTarget;
                remaining = Math.Max(0, remaining - bestGain);
                if (remaining == 0) break;
            }
            return plan;
        }
    }
}
