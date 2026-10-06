#nullable enable
using System;
using System.Collections.Generic;
using nadena.dev.ndmf.preview;
using UnityEngine;

namespace Meshia.MeshSimplification.Ndmf.Editor
{
    // Optional integrations return an owned copy only when they actually remove faces.
    // The same preparation is used by builds, preview proxies and measurement inputs.
    internal sealed class CutMeshPreparation : IDisposable
    {
        internal delegate Mesh? PrepareCuts(Renderer renderer, Mesh input, ComputeContext? context);
        private static PrepareCuts? prepare;
        private static readonly Dictionary<Renderer, (Mesh source, int count)> Counts = new();
        internal readonly Mesh Mesh;
        internal readonly bool Changed;

        private CutMeshPreparation(Mesh input, Mesh? prepared)
        {
            Mesh = prepared != null ? prepared : input;
            Changed = prepared != null && prepared != input;
        }

        internal static void Register(PrepareCuts callback) { prepare = callback; Invalidate(); }
        internal static void Invalidate() => Counts.Clear();

        internal static bool Supports(MeshSimplificationTarget target, MeshSimplifierOptions options)
            // Linear, clamped interpolation keeps unselected blendshape deltas inside
            // their tolerance. Other algorithms and extrapolation need separate support.
            => target.Kind == MeshSimplificationTargetKind.FaQemTriangleCount &&
               !options.UseBarycentricCoordinateInterpolation;

        internal static CutMeshPreparation Prepare(Renderer renderer, Mesh input,
            MeshSimplificationTarget target, MeshSimplifierOptions options, ComputeContext? context = null)
            => new(input, Supports(target, options) ? prepare?.Invoke(renderer, input, context) : null);

        internal static int PreparedTriangleCount(Renderer renderer, Mesh input,
            MeshSimplificationTarget target, MeshSimplifierOptions options)
        {
            if (!Supports(target, options)) return input.GetTriangleCount();
            if (Counts.TryGetValue(renderer, out var cached) && cached.source == input) return cached.count;
            using var prepared = Prepare(renderer, input, target, options);
            var count = prepared.Mesh.GetTriangleCount();
            Counts[renderer] = (input, count);
            return count;
        }

        public void Dispose()
        {
            if (Changed) UnityEngine.Object.DestroyImmediate(Mesh);
        }
    }
}
