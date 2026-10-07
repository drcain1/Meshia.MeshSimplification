#nullable enable
using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace Meshia.MeshSimplification
{
    public partial struct MeshSimplifier
    {
        /// <summary>Measures an FA-QEM count sequence without constructing an output Unity mesh.</summary>
        public static FaQemCountProfile MeasureFaQemCounts(Mesh source, int minimumTarget,
            MeshSimplifierOptions options, BitArray? preserveBones = null)
        {
            using var measurement = new FaQemCountMeasurement(source, minimumTarget, options, preserveBones);
            return measurement.Complete();
        }

        /// <summary>Measures an FA-QEM count sequence while yielding to the editor during the job.</summary>
        public static async Task<FaQemCountProfile> MeasureFaQemCountsAsync(Mesh source, int minimumTarget,
            MeshSimplifierOptions options, BitArray? preserveBones = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var measurement = new FaQemCountMeasurement(source, minimumTarget, options, preserveBones);
            while (!measurement.Pending.IsCompleted) await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return measurement.Complete();
        }

        private sealed class FaQemCountMeasurement : IDisposable
        {
            private Mesh.MeshDataArray original;
            private NativeList<BlendShapeData> blendShapes;
            private NativeBitArray preserve;
            private MeshSimplifier simplifier;
            private bool hasOriginal, hasSimplifier;
            private readonly int requested;
            internal JobHandle Pending;

            internal FaQemCountMeasurement(Mesh source, int minimumTarget, MeshSimplifierOptions options, BitArray? bones, bool recordReplay = false)
            {
                requested = Math.Max(0, minimumTarget);
                var target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = requested };
                ValidateFaQemTarget(target, options);
                try
                {
                    original = Mesh.AcquireReadOnlyMeshData(source);
                    hasOriginal = true;
                    blendShapes = BlendShapeData.GetMeshBlendShapes(source, Unity.Collections.Allocator.Persistent);
                    preserve = new NativeBitArray(bones?.Length ?? 0, Unity.Collections.Allocator.Persistent, NativeArrayOptions.ClearMemory);
                    if (bones != null)
                        for (var i = 0; i < bones.Length; i++) preserve.Set(i, bones[i]);
                    simplifier = new MeshSimplifier(Unity.Collections.Allocator.Persistent) { RecordFaQemCounts = true, RecordFaQemReplay = recordReplay };
                    hasSimplifier = true;
                    Pending = simplifier.ScheduleLoadMeshData(original[0], options, preserve);
                    Pending = simplifier.ScheduleSimplify(original[0], blendShapes, target, preserve, Pending);
                    JobHandle.ScheduleBatchedJobs();
                }
                catch { Dispose(); throw; }
            }

            internal FaQemCountProfile Complete()
            {
                Pending.Complete();
                return new FaQemCountProfile(requested, simplifier.FaQemTriangleCounts.AsArray().ToArray());
            }

            internal FaQemReplayStep[] CompleteReplay()
            {
                Pending.Complete();
                return simplifier.FaQemReplaySteps.AsArray().ToArray();
            }

            public void Dispose()
            {
                Pending.Complete();
                if (hasSimplifier) simplifier.Dispose();
                if (preserve.IsCreated) preserve.Dispose();
                if (blendShapes.IsCreated)
                {
                    foreach (var shape in blendShapes) shape.Dispose();
                    blendShapes.Dispose();
                }
                if (hasOriginal) original.Dispose();
            }
        }
    }
}
