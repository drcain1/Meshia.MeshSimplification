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
        // A manual reduction never removes more output than requested. Probe a bounded
        // set of targets without committing them, and return the best measured saving.
        internal static async Task<int> FindReductionAsync(MeasuredMeshResponse mesh,
            int current, int reduction, Func<bool> stillCurrent)
        {
            if (reduction <= 0 || current <= 1) return current;
            var produced = await mesh.MeasureAsync(current);
            var best = current;
            var bestSaving = 0;
            var high = current;
            var low = 1;
            var trial = Math.Max(low, current - reduction);
            for (var attempt = 0; attempt < 8 && low < high; attempt++)
            {
                if (!stillCurrent()) throw new OperationCanceledException();
                var output = await mesh.MeasureAsync(trial);
                if (!stillCurrent()) throw new OperationCanceledException();
                var saving = produced - output;
                if (saving > bestSaving && saving <= reduction)
                {
                    best = trial;
                    bestSaving = saving;
                    if (saving == reduction) break;
                }
                if (saving > reduction) low = trial + 1;
                else high = trial;
                if (low >= high) break;
                trial = low + (high - low) / 2;
            }
            return best;
        }

        internal static async Task<int> FindOutputTargetAsync(MeasuredMeshResponse mesh,
            int current, int desiredOutput, Func<bool> stillCurrent)
        {
            if (!stillCurrent()) throw new OperationCanceledException();
            var produced = await mesh.MeasureAsync(current);
            if (!stillCurrent()) throw new OperationCanceledException();
            desiredOutput = Math.Max(0, Math.Min(mesh.SourceCount, desiredOutput));
            if (desiredOutput < produced)
                return await FindReductionAsync(mesh, current, produced - desiredOutput, stillCurrent);
            if (desiredOutput == produced) return current;
            var low = current;
            var high = mesh.SourceCount;
            var best = current;
            var bestOutput = produced;
            var trial = (int)Math.Min(high, (long)current + desiredOutput - produced);
            for (var attempt = 0; low < high && attempt < 8; attempt++)
            {
                if (!stillCurrent()) throw new OperationCanceledException();
                var output = await mesh.MeasureAsync(trial);
                if (!stillCurrent()) throw new OperationCanceledException();
                if (output > bestOutput && output <= desiredOutput)
                {
                    best = trial;
                    bestOutput = output;
                    if (output == desiredOutput) break;
                }
                if (output > desiredOutput) high = trial - 1;
                else low = trial;
                if (low >= high) break;
                trial = low + (high - low + 1) / 2;
            }
            return best;
        }

        private sealed class ReductionCandidate
        {
            internal MeasuredMeshResponse Mesh = null!;
            internal int Current, Produced, Floor, FloorOutput, Capacity, Share;
            internal long Weight;
        }

        private static Dictionary<int, int> PlanReductions(IReadOnlyList<MeasuredMeshResponse> meshes,
            IReadOnlyList<int> targets, long reduction, Func<bool>? cancel,
            IReadOnlyList<int>? startingTargets, IReadOnlyList<int>? startingOutputs)
        {
            var candidates = new List<ReductionCandidate>();
            foreach (var mesh in meshes)
            {
                if (cancel?.Invoke() == true) throw new OperationCanceledException();
                var current = targets[mesh.Index];
                var start = startingTargets?[mesh.Index] ?? current;
                // A whole Analyze Build run may remove at most a quarter of any
                // starting target AND starting measured output. Never erode this
                // floor between the verification builds of the same run.
                var floor = Math.Max(1, (int)Math.Ceiling(start * .75));
                if (current <= floor) continue;
                var produced = mesh.Measure(current);
                var startOutput = startingOutputs != null && startingOutputs[mesh.Index] >= 0
                    ? startingOutputs[mesh.Index] : produced;
                var outputRoom = produced - (int)Math.Ceiling(startOutput * .75);
                if (outputRoom <= 0) continue;
                var output = mesh.Measure(floor);
                var capacity = Math.Min(outputRoom, produced - output);
                if (capacity <= 0) continue;
                candidates.Add(new ReductionCandidate { Mesh = mesh, Current = current,
                    Produced = produced, Floor = floor, FloorOutput = output,
                    Capacity = capacity, Weight = Math.Max(1, start) });
            }
            // Water-fill measured capacity in proportion to the user's starting
            // allocations. Flat meshes contribute no capacity; no first mesh pays
            // the entire overrun just because it happens to simplify readily.
            var remaining = reduction;
            while (remaining > 0)
            {
                var active = candidates.Where(c => c.Share < c.Capacity).ToArray();
                if (active.Length == 0) break;
                var weight = active.Sum(c => c.Weight);
                var passRemaining = remaining;
                foreach (var c in active)
                {
                    var share = (int)Math.Min(remaining, Math.Min(c.Capacity - c.Share,
                        Math.Max(1L, (long)(passRemaining * (double)c.Weight / weight))));
                    c.Share += share;
                    remaining -= share;
                    if (remaining == 0) break;
                }
            }
            var plan = new Dictionary<int, int>();
            foreach (var c in candidates.Where(c => c.Share > 0))
            {
                var lower = c.Floor;
                var upper = c.Current;
                var lowerGain = c.Produced - c.FloorOutput;
                var upperGain = 0;
                var best = c.Current;
                var bestGain = 0;
                if (lowerGain <= c.Share) { best = lower; bestGain = lowerGain; }
                for (var attempt = 0; bestGain < c.Share && upper - lower > 1 && attempt < 3; attempt++)
                {
                    if (cancel?.Invoke() == true) throw new OperationCanceledException();
                    var fraction = lowerGain > upperGain
                        ? Math.Max(0, Math.Min(1, (c.Share - upperGain) / (double)(lowerGain - upperGain))) : .5;
                    var offset = Math.Max(1, Math.Min(upper - lower - 1, (int)Math.Round((upper - lower) * fraction)));
                    var trial = upper - offset;
                    var gain = c.Produced - c.Mesh.Measure(trial);
                    if (gain > bestGain && gain <= c.Share) { best = trial; bestGain = gain; }
                    if (gain > c.Share) { lower = trial; lowerGain = gain; }
                    else { upper = trial; upperGain = gain; }
                }
                if (bestGain > 0) plan[c.Mesh.Index] = best;
            }
            return plan;
        }

        // No targets are changed here. A candidate earns budget only from measured
        // output changes. Flat, reversed, or wildly discontinuous probes earn none.
        internal static Dictionary<int, int> Plan(IReadOnlyList<MeasuredMeshResponse> meshes,
            IReadOnlyList<int> targets, long outputChange, Func<bool>? cancel = null,
            IReadOnlyList<int>? startingTargets = null, IReadOnlyList<int>? startingOutputs = null)
        {
            if (outputChange < 0)
                return PlanReductions(meshes, targets, -outputChange, cancel, startingTargets, startingOutputs);
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
