// Research adaptation of touma-tw/Meshia.MeshSimplification at ed01f0b9b9f0f049d3e899c11c977bc70d0ebbc5.
// Original MIT notice retained in Licenses/UPSTREAM-MIT.txt. Changes: double precision; normalized caller coordinates.
using Unity.Mathematics;

namespace Meshia.MeshSimplification
{
    /// <summary>
    /// Garland-Heckbert / Hoppe attribute aware quadric error metric over the 5D space
    /// (position.xyz, weighted UV0.xy). Measures the squared distance of an extended point
    /// to the affine subspace spanned by a triangle's three extended vertices, so the error
    /// penalizes UV distortion in addition to geometric distortion.
    ///
    /// The UV components are stored pre multiplied by the configured UV weight, so callers must
    /// pass <c>weight * uv</c> to <see cref="ComputeError"/> and divide the solved UV from
    /// <see cref="TrySolveOptimal"/> by the same weight.
    ///
    /// Error(p, u) = [p u]^T A [p u] + 2 b^T [p u] + c, with A symmetric, stored in block form:
    /// <code>
    /// A = | App  Apu |   b = | Bp |
    ///     | Apu^T Auu|       | Bu |
    /// </code>
    /// </summary>
    struct FaQemUvQuadric
    {
        // Symmetric position-position block (3x3).
        double3x3 App;
        // Position-UV block (3x2), stored as its two columns (one per UV component).
        double3 Apu0;
        double3 Apu1;
        // Symmetric UV-UV block (2x2).
        double2x2 Auu;
        // Linear terms.
        double3 Bp;
        double2 Bu;
        // Constant term.
        double C;

        static double3x3 Outer(double3 a, double3 b) => new(a * b.x, a * b.y, a * b.z);
        static double2x2 Outer(double2 a, double2 b) => new(a * b.x, a * b.y);

        /// <summary>
        /// Builds the quadric of a single triangle from its three positions and (weighted) UVs.
        /// </summary>
        public static FaQemUvQuadric FromTriangle(double3 p1, double3 p2, double3 p3, double2 weightedUv1, double2 weightedUv2, double2 weightedUv3)
        {
            // First basis vector: normalized edge v2 - v1 in 5D.
            double3 e1p = p2 - p1;
            double2 e1u = weightedUv2 - weightedUv1;
            var len1 = math.sqrt(math.dot(e1p, e1p) + math.dot(e1u, e1u));
            var has1 = len1 > 1e-12d;
            if (has1)
            {
                var inv = 1d / len1;
                e1p *= inv;
                e1u *= inv;
            }
            else
            {
                e1p = double3.zero;
                e1u = double2.zero;
            }

            // Second basis vector: (v3 - v1) orthogonalized against e1, then normalized.
            double3 e2p = p3 - p1;
            double2 e2u = weightedUv3 - weightedUv1;
            if (has1)
            {
                var proj = math.dot(e2p, e1p) + math.dot(e2u, e1u);
                e2p -= proj * e1p;
                e2u -= proj * e1u;
            }
            var len2 = math.sqrt(math.dot(e2p, e2p) + math.dot(e2u, e2u));
            var has2 = len2 > 1e-12d;
            if (has2)
            {
                var inv = 1d / len2;
                e2p *= inv;
                e2u *= inv;
            }
            else
            {
                e2p = double3.zero;
                e2u = double2.zero;
            }

            // A = I - e1 e1^T - e2 e2^T (projection onto the orthogonal complement of span{e1, e2}).
            var app = double3x3.identity;
            double3 apu0 = double3.zero;
            double3 apu1 = double3.zero;
            var auu = double2x2.identity;

            if (has1)
            {
                app -= Outer(e1p, e1p);
                apu0 -= e1p * e1u.x;
                apu1 -= e1p * e1u.y;
                auu -= Outer(e1u, e1u);
            }
            if (has2)
            {
                app -= Outer(e2p, e2p);
                apu0 -= e2p * e2u.x;
                apu1 -= e2p * e2u.y;
                auu -= Outer(e2u, e2u);
            }

            // b = (v1 . e1) e1 + (v1 . e2) e2 - v1
            var a1 = has1 ? math.dot(p1, e1p) + math.dot(weightedUv1, e1u) : 0d;
            var a2 = has2 ? math.dot(p1, e2p) + math.dot(weightedUv1, e2u) : 0d;

            return new FaQemUvQuadric
            {
                App = app,
                Apu0 = apu0,
                Apu1 = apu1,
                Auu = auu,
                Bp = a1 * e1p + a2 * e2p - p1,
                Bu = a1 * e1u + a2 * e2u - weightedUv1,
                C = math.dot(p1, p1) + math.dot(weightedUv1, weightedUv1) - a1 * a1 - a2 * a2,
            };
        }

        /// <summary>
        /// Builds a position only plane quadric (UV components are unconstrained).
        /// Used to constrain border vertices the same way the geometric quadric does.
        /// </summary>
        public static FaQemUvQuadric FromPositionPlane(double3 normal, double3 pointOnPlane)
        {
            var d = -math.dot(normal, pointOnPlane);
            return new FaQemUvQuadric
            {
                App = Outer(normal, normal),
                Apu0 = double3.zero,
                Apu1 = double3.zero,
                Auu = double2x2.zero,
                Bp = d * normal,
                Bu = double2.zero,
                C = d * d,
            };
        }

        public static FaQemUvQuadric operator +(FaQemUvQuadric left, FaQemUvQuadric right) => new()
        {
            App = left.App + right.App,
            Apu0 = left.Apu0 + right.Apu0,
            Apu1 = left.Apu1 + right.Apu1,
            Auu = left.Auu + right.Auu,
            Bp = left.Bp + right.Bp,
            Bu = left.Bu + right.Bu,
            C = left.C + right.C,
        };

        internal FaQemUvQuadric WithGeometry(FaQemQuadric geometry, double weight)
        {
            return new FaQemUvQuadric
            {
                App = App * weight + new double3x3(geometry.M00, geometry.M01, geometry.M02,
                    geometry.M01, geometry.M11, geometry.M12, geometry.M02, geometry.M12, geometry.M22),
                Apu0 = Apu0 * weight, Apu1 = Apu1 * weight, Auu = Auu * weight,
                Bp = Bp * weight + new double3(geometry.M03, geometry.M13, geometry.M23),
                Bu = Bu * weight, C = C * weight + geometry.M33,
            };
        }

        /// <summary>
        /// Evaluates the error at the given position and weighted UV.
        /// </summary>
        public readonly double ComputeError(double3 position, double2 weightedUv)
        {
            var appp = math.mul(App, position);
            var apuu = Apu0 * weightedUv.x + Apu1 * weightedUv.y;
            var auuu = math.mul(Auu, weightedUv);

            var quadratic = math.dot(position, appp) + 2d * math.dot(position, apuu) + math.dot(weightedUv, auuu);
            var linear = 2d * (math.dot(Bp, position) + math.dot(Bu, weightedUv));
            return quadratic + linear + C;
        }

        /// <summary>
        /// Solves A v = -b for the optimal extended point v = (position, weighted UV).
        /// Returns false when the system is (near) singular so the caller can fall back to candidates.
        /// </summary>
        public readonly bool TrySolveOptimal(out double3 position, out double2 weightedUv)
        {
            // Assemble the symmetric 5x5 system in row major order.
            System.Span<double> m = stackalloc double[25];
            System.Span<double> rhs = stackalloc double[5];

            // App (rows/cols 0..2)
            m[0] = App.c0.x; m[1] = App.c1.x; m[2] = App.c2.x;
            m[5] = App.c0.y; m[6] = App.c1.y; m[7] = App.c2.y;
            m[10] = App.c0.z; m[11] = App.c1.z; m[12] = App.c2.z;
            // Apu (rows 0..2, cols 3..4)
            m[3] = Apu0.x; m[4] = Apu1.x;
            m[8] = Apu0.y; m[9] = Apu1.y;
            m[13] = Apu0.z; m[14] = Apu1.z;
            // Apu^T (rows 3..4, cols 0..2)
            m[15] = Apu0.x; m[16] = Apu0.y; m[17] = Apu0.z;
            m[20] = Apu1.x; m[21] = Apu1.y; m[22] = Apu1.z;
            // Auu (rows/cols 3..4)
            m[18] = Auu.c0.x; m[19] = Auu.c1.x;
            m[23] = Auu.c0.y; m[24] = Auu.c1.y;

            rhs[0] = -Bp.x; rhs[1] = -Bp.y; rhs[2] = -Bp.z;
            rhs[3] = -Bu.x; rhs[4] = -Bu.y;

            // Equilibrate both variables and equations: position and UV blocks can differ
            // by orders of magnitude after adding the geometric quadric.
            System.Span<double> inverseScales = stackalloc double[5];
            for (var i = 0; i < 5; i++)
            {
                var diagonal = m[i * 5 + i];
                if (!math.isfinite(diagonal) || diagonal <= 0d)
                {
                    position = default; weightedUv = default; return false;
                }
                inverseScales[i] = 1d / math.sqrt(diagonal);
            }
            for (var r = 0; r < 5; r++)
            {
                rhs[r] *= inverseScales[r];
                for (var c = 0; c < 5; c++) m[r * 5 + c] *= inverseScales[r] * inverseScales[c];
            }

            // Relative singularity threshold. A quadric that constrains fewer than 5 dimensions
            // (e.g. a single triangle, or a flat/colinear neighborhood) is mathematically rank
            // deficient; floating point rounding leaves tiny non-zero pivots, so an absolute
            // threshold is unreliable. Scale the threshold by the matrix magnitude instead, and
            // report failure so the caller falls back to the endpoint/midpoint candidates rather
            // than producing a wildly off optimum.
            var matrixScale = 0d;
            for (int i = 0; i < 25; i++)
            {
                matrixScale = math.max(matrixScale, math.abs(m[i]));
            }
            var singularThreshold = math.max(matrixScale * 1e-10d, 1e-12d);

            // Gaussian elimination with partial pivoting.
            for (int col = 0; col < 5; col++)
            {
                int pivot = col;
                var maxAbs = math.abs(m[col * 5 + col]);
                for (int r = col + 1; r < 5; r++)
                {
                    var a = math.abs(m[r * 5 + col]);
                    if (a > maxAbs)
                    {
                        maxAbs = a;
                        pivot = r;
                    }
                }

                if (maxAbs < singularThreshold)
                {
                    position = default;
                    weightedUv = default;
                    return false;
                }

                if (pivot != col)
                {
                    for (int c = 0; c < 5; c++)
                    {
                        var tmp = m[col * 5 + c];
                        m[col * 5 + c] = m[pivot * 5 + c];
                        m[pivot * 5 + c] = tmp;
                    }
                    var tmpRhs = rhs[col];
                    rhs[col] = rhs[pivot];
                    rhs[pivot] = tmpRhs;
                }

                var pivotValue = m[col * 5 + col];
                for (int r = col + 1; r < 5; r++)
                {
                    var factor = m[r * 5 + col] / pivotValue;
                    if (factor != 0d)
                    {
                        for (int c = col; c < 5; c++)
                        {
                            m[r * 5 + c] -= factor * m[col * 5 + c];
                        }
                        rhs[r] -= factor * rhs[col];
                    }
                }
            }

            // Back substitution.
            System.Span<double> x = stackalloc double[5];
            for (int r = 4; r >= 0; r--)
            {
                var sum = rhs[r];
                for (int c = r + 1; c < 5; c++)
                {
                    sum -= m[r * 5 + c] * x[c];
                }
                x[r] = sum / m[r * 5 + r];
            }

            position = new double3(x[0] * inverseScales[0], x[1] * inverseScales[1], x[2] * inverseScales[2]);
            weightedUv = new double2(x[3] * inverseScales[3], x[4] * inverseScales[4]);
            return math.all(math.isfinite(position)) && math.all(math.isfinite(weightedUv));
        }
    }
}
