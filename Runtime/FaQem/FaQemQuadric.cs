using System;
using Unity.Mathematics;

namespace Meshia.MeshSimplification
{
    /// <summary>A symmetric homogeneous 4x4 quadric stored as ten doubles.</summary>
    internal struct FaQemQuadric
    {
        internal double M00, M01, M02, M03, M11, M12, M13, M22, M23, M33;

        internal static FaQemQuadric OuterProduct(double3 normal, double offset, double weight = 1d)
        {
            var x = normal.x;
            var y = normal.y;
            var z = normal.z;
            var w = offset;
            return new FaQemQuadric
            {
                M00 = weight * x * x, M01 = weight * x * y, M02 = weight * x * z, M03 = weight * x * w,
                M11 = weight * y * y, M12 = weight * y * z, M13 = weight * y * w,
                M22 = weight * z * z, M23 = weight * z * w, M33 = weight * w * w,
            };
        }

        internal static FaQemQuadric Plane(double3 normal, double3 point, double weight = 1d)
            => OuterProduct(normal, -math.dot(normal, point), weight);

        internal readonly double Evaluate(double3 p)
        {
            var x = p.x;
            var y = p.y;
            var z = p.z;
            return M00 * x * x + 2d * M01 * x * y + 2d * M02 * x * z + 2d * M03 * x +
                   M11 * y * y + 2d * M12 * y * z + 2d * M13 * y + M22 * z * z +
                   2d * M23 * z + M33;
        }

        internal readonly bool TrySolve(out double3 position)
        {
            // Normalize the linear system before taking its determinant. The
            // determinant has cubic units in the matrix scale, so comparing
            // it to a threshold based on squared column lengths is not scale
            // invariant and fails for very small/large quadrics.
            var matrixScale = math.max(math.abs(M00), math.max(math.abs(M01), math.max(math.abs(M02),
                math.max(math.abs(M11), math.max(math.abs(M12), math.abs(M22))))));
            if (!math.isfinite(matrixScale) || matrixScale <= 0d)
            {
                position = default;
                return false;
            }
            var c0 = new double3(M00, M01, M02) / matrixScale;
            var c1 = new double3(M01, M11, M12) / matrixScale;
            var c2 = new double3(M02, M12, M22) / matrixScale;
            var determinant = math.dot(c0, math.cross(c1, c2));
            if (!math.isfinite(determinant) || math.abs(determinant) <= FaQemMath.SolveEpsilon)
            {
                position = default;
                return false;
            }

            var rhs = new double3(-M03, -M13, -M23) / matrixScale;
            position = new double3(
                math.dot(rhs, math.cross(c1, c2)),
                math.dot(c0, math.cross(rhs, c2)),
                math.dot(c0, math.cross(c1, rhs))) / determinant;
            return math.all(math.isfinite(position));
        }

        public static FaQemQuadric operator +(FaQemQuadric a, FaQemQuadric b)
        {
            a.M00 += b.M00; a.M01 += b.M01; a.M02 += b.M02; a.M03 += b.M03;
            a.M11 += b.M11; a.M12 += b.M12; a.M13 += b.M13;
            a.M22 += b.M22; a.M23 += b.M23; a.M33 += b.M33;
            return a;
        }

        public static FaQemQuadric operator *(FaQemQuadric value, double scale)
        {
            value.M00 *= scale; value.M01 *= scale; value.M02 *= scale; value.M03 *= scale;
            value.M11 *= scale; value.M12 *= scale; value.M13 *= scale;
            value.M22 *= scale; value.M23 *= scale; value.M33 *= scale;
            return value;
        }
    }

    internal static class FaQemMath
    {
        internal const double AreaEpsilon = 1e-24;
        internal const double SolveEpsilon = 1e-12;

        // Positions are normalized by the source bounds diagonal (paper §6.2).
        internal static bool IsCollapsibleEdge(double3 a, double3 b)
            => math.lengthsq(b - a) >= 1e-16d;

        internal static double AreaEdgeCost(double3 a, double3 b, double3 x)
        {
            var swept = math.cross(b - a, x) + math.cross(a, b);
            return 0.5d * math.lengthsq(swept);
        }

        internal static double3 SelectFiniteMinimum(FaQemQuadric q, double3 a, double3 b)
        {
            var midpoint = (a + b) * 0.5d;
            var ca = q.Evaluate(a);
            var cb = q.Evaluate(b);
            var cm = q.Evaluate(midpoint);
            if (!math.isfinite(ca)) ca = double.PositiveInfinity;
            if (!math.isfinite(cb)) cb = double.PositiveInfinity;
            if (!math.isfinite(cm)) cm = double.PositiveInfinity;
            if (ca <= cb && ca <= cm) return a;
            return cb <= cm ? b : midpoint;
        }
    }
}
