#nullable enable
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

namespace Meshia.MeshSimplification
{
    // Only validated decisions are recorded. Replaying uses the same merge and
    // mesh-writing paths, including skin weights, blend shapes and UV tangents.
    internal struct FaQemReplayStep
    {
        internal int A, B;
        internal float3 Position;
        internal float2 Uv;
        internal bool SolvedUv;
    }

    public partial struct MeshSimplifier
    {
        internal sealed class FaQemReplay
        {
            private readonly Mesh source;
            private readonly MeshSimplifierOptions options;
            private readonly BitArray? bones;
            private readonly FaQemReplayStep[] steps;
            internal readonly FaQemCountProfile Counts;

            internal FaQemReplay(Mesh source, MeshSimplifierOptions options, BitArray? bones, FaQemReplayStep[] steps, FaQemCountProfile counts)
            {
                this.source = source;
                this.options = options;
                this.bones = bones;
                this.steps = steps;
                Counts = counts;
            }

            // The owner must invalidate this plan when the source, options or pose
            // changes. It is intentionally scoped to a prepared preview, not global.
            internal Task WriteAsync(int target, Mesh destination, CancellationToken cancellationToken = default)
                => SimplifyAsync(source, new MeshSimplificationTarget
                {
                    Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = target
                }, options, bones, destination, steps, cancellationToken);
        }

        internal static async Task<FaQemReplay> PrepareFaQemReplayAsync(Mesh source,
            MeshSimplifierOptions options, BitArray? bones = null)
        {
            var preserved = bones == null ? null : (BitArray)bones.Clone();
            using var measurement = new FaQemCountMeasurement(source, 0, options, preserved, true);
            while (!measurement.Pending.IsCompleted) await Task.Yield();
            return new FaQemReplay(source, options, preserved, measurement.CompleteReplay(), measurement.Complete());
        }
    }
}
