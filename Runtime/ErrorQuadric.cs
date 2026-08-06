using Unity.Mathematics;
using Plane = Unity.Mathematics.Geometry.Plane;
namespace Meshia.MeshSimplification
{
    struct ErrorQuadric
    {
        public ErrorQuadric(Plane plane)
        {
            float4 normalAndDistance = plane.NormalAndDistance;
            m0 = normalAndDistance.x * normalAndDistance.x;
            m1 = normalAndDistance.x * normalAndDistance.y;
            m2 = normalAndDistance.x * normalAndDistance.z;
            m3 = normalAndDistance.x * normalAndDistance.w;

            m4 = normalAndDistance.y * normalAndDistance.y;
            m5 = normalAndDistance.y * normalAndDistance.z;
            m6 = normalAndDistance.y * normalAndDistance.w;

            m7 = normalAndDistance.z * normalAndDistance.z;
            m8 = normalAndDistance.z * normalAndDistance.w;

            m9 = normalAndDistance.w * normalAndDistance.w;

        }
        float m0;
        float m1;
        float m2;
        float m3;

        float m4;
        float m5;
        float m6;

        float m7;
        float m8;

        float m9;

        public static ErrorQuadric operator +(ErrorQuadric left, ErrorQuadric right) => new()
        {
            m0 = left.m0 + right.m0,
            m1 = left.m1 + right.m1,
            m2 = left.m2 + right.m2,
            m3 = left.m3 + right.m3,
            m4 = left.m4 + right.m4,
            m5 = left.m5 + right.m5,
            m6 = left.m6 + right.m6,
            m7 = left.m7 + right.m7,
            m8 = left.m8 + right.m8,
            m9 = left.m9 + right.m9
        };

        public static ErrorQuadric operator *(ErrorQuadric value, float factor) => new()
        {
            m0 = value.m0 * factor,
            m1 = value.m1 * factor,
            m2 = value.m2 * factor,
            m3 = value.m3 * factor,
            m4 = value.m4 * factor,
            m5 = value.m5 * factor,
            m6 = value.m6 * factor,
            m7 = value.m7 * factor,
            m8 = value.m8 * factor,
            m9 = value.m9 * factor,
        };


        /// <summary>
        /// Determinant(0, 1, 2, 1, 4, 5, 2, 5, 7)
        /// </summary>
        /// <returns></returns>
        public readonly float Determinant1()
        {
            var det =
                m0 * m4 * m7 +
                m2 * m1 * m5 +
                m1 * m5 * m2 -
                m2 * m4 * m2 -
                m0 * m5 * m5 -
                m1 * m1 * m7;
            return det;
        }

        /// <summary>
        /// Determinant(1, 2, 3, 4, 5, 6, 5, 7, 8)
        /// </summary>
        /// <returns></returns>
        public readonly float Determinant2()
        {
            var det =
                m1 * m5 * m8 +
                m3 * m4 * m7 +
                m2 * m6 * m5 -
                m3 * m5 * m5 -
                m1 * m6 * m7 -
                m2 * m4 * m8;
            return det;
        }

        /// <summary>
        /// Determinant(0, 2, 3, 1, 5, 6, 2, 7, 8)
        /// </summary>
        /// <returns></returns>
        public readonly float Determinant3()
        {
            var det =
                m0 * m5 * m8 +
                m3 * m1 * m7 +
                m2 * m6 * m2 -
                m3 * m5 * m2 -
                m0 * m6 * m7 -
                m2 * m1 * m8;
            return det;
        }

        /// <summary>
        /// Determinant(0, 1, 3, 1, 4, 6, 2, 5, 8)
        /// </summary>
        /// <returns></returns>
        public readonly float Determinant4()
        {
            var det =
                m0 * m4 * m8 +
                m3 * m1 * m5 +
                m1 * m6 * m2 -
                m3 * m4 * m2 -
                m0 * m6 * m5 -
                m1 * m1 * m8;
            return det;
        }

        public readonly float ComputeError(float3 position)
        {
            var x = position.x;
            var y = position.y;
            var z = position.z;

            return m0 * x * x
                + 2 * m1 * x * y
                + 2 * m2 * x * z
                + 2 * m3 * x
                + m4 * y * y
                + 2 * m5 * y * z
                + 2 * m6 * y
                + m7 * z * z
                + 2 * m8 * z + m9;
        }
    }

    /// <summary>
    /// Blender's decimator stores and optimizes quadrics in double precision.
    /// Keep this separate from Meshia's original float quadric so enabling the
    /// Blender target does not change the behavior of the existing targets.
    /// </summary>
    struct BlenderErrorQuadric
    {
        double m0;
        double m1;
        double m2;
        double m3;
        double m4;
        double m5;
        double m6;
        double m7;
        double m8;
        double m9;

        public BlenderErrorQuadric(double3 normal, double3 point)
        {
            var distance = -math.dot(normal, point);
            m0 = normal.x * normal.x;
            m1 = normal.x * normal.y;
            m2 = normal.x * normal.z;
            m3 = normal.x * distance;
            m4 = normal.y * normal.y;
            m5 = normal.y * normal.z;
            m6 = normal.y * distance;
            m7 = normal.z * normal.z;
            m8 = normal.z * distance;
            m9 = distance * distance;
        }

        public static BlenderErrorQuadric operator +(BlenderErrorQuadric left, BlenderErrorQuadric right) => new()
        {
            m0 = left.m0 + right.m0,
            m1 = left.m1 + right.m1,
            m2 = left.m2 + right.m2,
            m3 = left.m3 + right.m3,
            m4 = left.m4 + right.m4,
            m5 = left.m5 + right.m5,
            m6 = left.m6 + right.m6,
            m7 = left.m7 + right.m7,
            m8 = left.m8 + right.m8,
            m9 = left.m9 + right.m9,
        };

        public static BlenderErrorQuadric operator *(BlenderErrorQuadric value, double factor) => new()
        {
            m0 = value.m0 * factor,
            m1 = value.m1 * factor,
            m2 = value.m2 * factor,
            m3 = value.m3 * factor,
            m4 = value.m4 * factor,
            m5 = value.m5 * factor,
            m6 = value.m6 * factor,
            m7 = value.m7 * factor,
            m8 = value.m8 * factor,
            m9 = value.m9 * factor,
        };

        public readonly bool TryOptimize(out double3 position)
        {
            var determinant = Determinant(m0, m1, m2, m1, m4, m5, m2, m5, m7);
            if (math.abs(determinant) <= 1e-8)
            {
                position = default;
                return false;
            }

            position = new double3(
                -Determinant(m1, m2, m3, m4, m5, m6, m5, m7, m8) / determinant,
                Determinant(m0, m2, m3, m1, m5, m6, m2, m7, m8) / determinant,
                -Determinant(m0, m1, m3, m1, m4, m6, m2, m5, m8) / determinant);
            return math.all(math.isfinite(position));
        }

        public readonly double Evaluate(double3 position)
        {
            var x = position.x;
            var y = position.y;
            var z = position.z;
            return m0 * x * x +
                   2.0 * m1 * x * y +
                   2.0 * m2 * x * z +
                   2.0 * m3 * x +
                   m4 * y * y +
                   2.0 * m5 * y * z +
                   2.0 * m6 * y +
                   m7 * z * z +
                   2.0 * m8 * z +
                   m9;
        }

        static double Determinant(
            double a, double b, double c,
            double d, double e, double f,
            double g, double h, double i)
        {
            return a * e * i + c * d * h + b * f * g - c * e * g - a * f * h - b * d * i;
        }
    }
}

