using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Meshia.MeshSimplification
{
    partial struct SimplifyJob
    {
        const byte FaQemVisibilityBoundaryVertex = 8;
        readonly bool HasVisibilityBones => Options.SkinningProtection.PreserveAllBoneMembership ||
            Options.SkinningProtection.VisibilityBoneIndices.Length > 0;
        readonly bool IsVisibilityBone(uint index)
        {
            if (Options.SkinningProtection.PreserveAllBoneMembership) return true;
            var selected = Options.SkinningProtection.VisibilityBoneIndices;
            for (var i = 0; i < selected.Length; i++) if (selected[i] == index) return true;
            return false;
        }

        readonly bool SameVisibility(ReadOnlySpan<uint> a, ReadOnlySpan<float> wa,
            ReadOnlySpan<uint> b, ReadOnlySpan<float> wb)
        {
            for (var side = 0; side < 2; side++)
            {
                var indices = side == 0 ? a : b; var weights = side == 0 ? wa : wb;
                var other = side == 0 ? b : a; var otherWeights = side == 0 ? wb : wa;
                for (var i = 0; i < indices.Length; i++)
                {
                    if (!math.isfinite(weights[i])) return false;
                    if (weights[i] == 0 || !IsVisibilityBone(indices[i])) continue;
                    var found = false;
                    for (var j = 0; j < other.Length; j++)
                        if (other[j] == indices[i] && otherWeights[j] != 0) { found = true; break; }
                    if (!found) return false;
                }
            }
            return true;
        }

        readonly bool SameVertexVisibility(int a, int b, int dimension)
            => SameVisibility(VertexBlendIndicesBuffer.AsSpan().Slice(a * dimension, dimension),
                VertexBlendWeightBuffer.AsSpan().Slice(a * dimension, dimension),
                VertexBlendIndicesBuffer.AsSpan().Slice(b * dimension, dimension),
                VertexBlendWeightBuffer.AsSpan().Slice(b * dimension, dimension));

        void InitializeFaQemVisibilityBoundaries(NativeArray<byte> flags)
        {
            if (!HasVisibilityBones || VertexBlendIndicesBuffer.Length == 0 || VertexPositionBuffer.Length == 0) return;
            var dimension = VertexBlendIndicesBuffer.Length / VertexPositionBuffer.Length;
            if (dimension <= 0 || VertexBlendWeightBuffer.Length != VertexBlendIndicesBuffer.Length) return;
            for (var ti = 0; ti < Triangles.Length; ti++)
            {
                if (IsDiscardedTriangle(ti)) continue;
                var t = Triangles[ti];
                if (SameVertexVisibility(t.x, t.y, dimension) && SameVertexVisibility(t.x, t.z, dimension)) continue;
                flags.ElementAt(t.x) |= FaQemVisibilityBoundaryVertex;
                flags.ElementAt(t.y) |= FaQemVisibilityBoundaryVertex;
                flags.ElementAt(t.z) |= FaQemVisibilityBoundaryVertex;
            }
        }

        readonly bool IsVisibilityCollapseValid(int a, int b, float3 position)
        {
            if (!HasVisibilityBones || VertexBlendIndicesBuffer.Length == 0) return true;
            if (VertexPositionBuffer.Length == 0) return false;
            var dimension = VertexBlendIndicesBuffer.Length / VertexPositionBuffer.Length;
            if (dimension <= 0 || dimension > 32 || VertexBlendWeightBuffer.Length != VertexBlendIndicesBuffer.Length) return false;
            if (!SameVertexVisibility(a, b, dimension)) return false;
            var ia = VertexBlendIndicesBuffer.AsSpan().Slice(a * dimension, dimension);
            var ib = VertexBlendIndicesBuffer.AsSpan().Slice(b * dimension, dimension);
            var wa = VertexBlendWeightBuffer.AsSpan().Slice(a * dimension, dimension);
            var wb = VertexBlendWeightBuffer.AsSpan().Slice(b * dimension, dimension);
            Span<uint> indices = stackalloc uint[dimension];
            Span<float> weights = stackalloc float[dimension];
            return SkinningCollapseMetrics.TrySimulateMerged(ia, wa, ib, wb,
                       ComputeLerpFactor(a, b, position), indices, weights, out _) &&
                   SameVisibility(ia, wa, indices, weights);
        }
    }
}
