using Unity.Collections;
using Unity.Mathematics;

namespace Meshia.MeshSimplification
{
    partial struct SimplifyJob
    {
        // Research branch only: optional joint placement alongside geometric ranking.

        bool IsFaQemJointUvValid(int2 edge, float2 uv)
        {
            if (!math.all(math.isfinite(uv))) return false;
            var a = VertexTexCoord0Buffer[edge.x].xy;
            var b = VertexTexCoord0Buffer[edge.y].xy;
            // Do not cross UV tiles or freely bridge an island boundary.
            if (math.any(math.floor(a) != math.floor(b)) || math.any(math.floor(uv) != math.floor(a))) return false;
            var min = math.min(a, b); var max = math.max(a, b);
            for (var side = 0; side < 2; side++)
            {
                var vertex = side == 0 ? edge.x : edge.y;
                var other = side == 0 ? edge.y : edge.x;
                foreach (var ti in VertexContainingTriangles.GetValuesForKey(vertex))
                {
                    if (IsDiscardedTriangle(ti)) continue;
                    var t = Triangles[ti];
                    var u0 = VertexTexCoord0Buffer[t.x].xy;
                    var u1 = VertexTexCoord0Buffer[t.y].xy;
                    var u2 = VertexTexCoord0Buffer[t.z].xy;
                    min = math.min(min, math.min(u0, math.min(u1, u2)));
                    max = math.max(max, math.max(u0, math.max(u1, u2)));
                    if (math.any(t == other)) continue;
                    var before = UvSignedArea(u0, u1, u2);
                    var after = UvSignedArea(t.x == vertex ? uv : u0, t.y == vertex ? uv : u1, t.z == vertex ? uv : u2);
                    if (!math.isfinite(after) || before * after < 0d ||
                        math.abs(after) < math.abs(before) * .05d) return false;
                    // Leave degenerate UV faces degenerate rather than introducing a new mapping.
                    if (math.abs(before) < 1e-14d && math.abs(after) > 1e-14d) return false;
                }
            }
            return math.all(uv >= min) && math.all(uv <= max);
        }

        static double UvSignedArea(float2 a, float2 b, float2 c)
        {
            var ab = (double2)b - a; var ac = (double2)c - a;
            return ab.x * ac.y - ab.y * ac.x;
        }

        // UV edits change the tangent basis. Rebuild on the job path so sync,
        // async, batch and preview exports all receive the same attributes.
        // Blend-shape tangents remain deltas, never normalized direction vectors.
        unsafe void RebuildFaQemUvTangents()
        {
            if (VertexTangentBuffer.Length == 0 || VertexNormalBuffer.Length == 0) return;
            using var tangents = new NativeArray<double3>(VertexPositionBuffer.Length, Allocator.Temp);
            using var bitangents = new NativeArray<double3>(VertexPositionBuffer.Length, Allocator.Temp);
            using var originalTangents = new NativeArray<float4>(VertexTangentBuffer, Allocator.Temp);
            RebuildFaQemUvTangentFrame(tangents, bitangents, originalTangents, default, false);
            for (var s = 0; s < BlendShapes.Length; s++)
            {
                var frames = BlendShapes[s].Frames;
                for (var f = 0; f < frames.Length; f++)
                    RebuildFaQemUvTangentFrame(tangents, bitangents, originalTangents, frames[f], true);
            }
        }

        unsafe void RebuildFaQemUvTangentFrame(NativeArray<double3> tangents, NativeArray<double3> bitangents,
            NativeArray<float4> originalTangents, BlendShapeFrameData frame, bool deformed)
        {
            for (var i = 0; i < tangents.Length; i++) { tangents[i] = 0; bitangents[i] = 0; }
            for (var ti = 0; ti < Triangles.Length; ti++)
            {
                if (IsDiscardedTriangle(ti)) continue;
                var t = Triangles[ti];
                var p0 = (double3)VertexPositionBuffer[t.x];
                var p1 = (double3)VertexPositionBuffer[t.y];
                var p2 = (double3)VertexPositionBuffer[t.z];
                if (deformed) { p0 += (double3)frame.DeltaVertices[t.x]; p1 += (double3)frame.DeltaVertices[t.y]; p2 += (double3)frame.DeltaVertices[t.z]; }
                var uv0 = (double2)VertexTexCoord0Buffer[t.x].xy;
                var d1 = (double2)VertexTexCoord0Buffer[t.y].xy - uv0;
                var d2 = (double2)VertexTexCoord0Buffer[t.z].xy - uv0;
                var determinant = d1.x * d2.y - d1.y * d2.x;
                if (math.abs(determinant) < 1e-14d) continue;
                var tangent = ((p1 - p0) * d2.y - (p2 - p0) * d1.y) / determinant;
                var bitangent = ((p2 - p0) * d1.x - (p1 - p0) * d2.x) / determinant;
                if (!math.all(math.isfinite(tangent)) || !math.all(math.isfinite(bitangent))) continue;
                for (var j = 0; j < 3; j++) { tangents[t[j]] += tangent; bitangents[t[j]] += bitangent; }
            }
            for (var i = 0; i < VertexPositionBuffer.Length; i++)
            {
                if (IsDiscardedVertex(i)) continue;
                var normal = (double3)VertexNormalBuffer[i].xyz;
                if (deformed) normal += (double3)frame.DeltaNormals[i];
                normal = math.normalizesafe(normal);
                var projected = tangents[i] - normal * math.dot(normal, tangents[i]);
                if (!math.all(math.isfinite(projected)) || math.lengthsq(projected) < 1e-24d)
                {
                    // If this pose has no usable UV tangent, retain its previous
                    // absolute direction even if the base tangent was rebuilt.
                    if (deformed) frame.DeltaTangents[i] += originalTangents[i].xyz - VertexTangentBuffer[i].xyz;
                    continue;
                }
                var tangent = (float3)math.normalize(projected);
                if (deformed) frame.DeltaTangents[i] = tangent - VertexTangentBuffer[i].xyz;
                else
                {
                    var sign = math.dot(math.cross(normal, (double3)tangent), bitangents[i]) < 0d ? -1f : 1f;
                    VertexTangentBuffer[i] = new float4(tangent, sign);
                }
            }
        }

        void InitializeFaQemUvQuadrics(NativeArray<FaQemUvQuadric> quadrics, double3 center, double scale)
        {
            for (var i = 0; i < Triangles.Length; i++)
            {
                if (IsDiscardedTriangle(i)) continue;
                var t = Triangles[i];
                var q = FaQemUvQuadric.FromTriangle(
                    ((double3)VertexPositionBuffer[t.x] - center) / scale,
                    ((double3)VertexPositionBuffer[t.y] - center) / scale,
                    ((double3)VertexPositionBuffer[t.z] - center) / scale,
                    (double2)VertexTexCoord0Buffer[t.x].xy,
                    (double2)VertexTexCoord0Buffer[t.y].xy,
                    (double2)VertexTexCoord0Buffer[t.z].xy);
                quadrics[t.x] = quadrics[t.x] + q;
                quadrics[t.y] = quadrics[t.y] + q;
                quadrics[t.z] = quadrics[t.z] + q;
            }
        }

        float2 PredictFaQemMergedUv(int a, int b, float3 position)
        {
            if (PreservedVertexPredicator.IsPreserved(a)) return VertexTexCoord0Buffer[a].xy;
            if (PreservedVertexPredicator.IsPreserved(b)) return VertexTexCoord0Buffer[b].xy;
            if (Options.UseBarycentricCoordinateInterpolation)
            {
                // Keep the same triangle traversal and interpolation as MergeVertexAttributeData.
                foreach (var ti in VertexContainingTriangles.GetValuesForKey(a))
                {
                    var t = Triangles[ti];
                    if (!math.any(t == b)) continue;
                    var weights = ComputeBarycentricCoordinate(new float3x3(
                        VertexPositionBuffer[t.x], VertexPositionBuffer[t.y], VertexPositionBuffer[t.z]), position);
                    return VertexTexCoord0Buffer[t.x].xy * weights.x +
                        VertexTexCoord0Buffer[t.y].xy * weights.y + VertexTexCoord0Buffer[t.z].xy * weights.z;
                }
                return VertexTexCoord0Buffer[a].xy;
            }
            return math.lerp(VertexTexCoord0Buffer[a].xy, VertexTexCoord0Buffer[b].xy, ComputeLerpFactor(a, b, position));
        }
    }
}
