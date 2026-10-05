// Adapted from touma-tw at ed01f0b; MIT notice: Licenses/UPSTREAM-MIT.txt.
using NUnit.Framework;
using Unity.Mathematics;

namespace Meshia.MeshSimplification.Tests
{
    public class FaQemUvQuadricTests
    {
        const float Tolerance = 1e-4f;

        static FaQemUvQuadric UnitTriangle()
        {
            // Triangle on the XY plane with UV equal to (x, y).
            return FaQemUvQuadric.FromTriangle(
                new float3(0f, 0f, 0f), new float3(1f, 0f, 0f), new float3(0f, 1f, 0f),
                new float2(0f, 0f), new float2(1f, 0f), new float2(0f, 1f));
        }

        [Test]
        public void ErrorIsZeroAtTriangleVertices()
        {
            var q = UnitTriangle();
            Assert.That(q.ComputeError(new float3(0f, 0f, 0f), new float2(0f, 0f)), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(q.ComputeError(new float3(1f, 0f, 0f), new float2(1f, 0f)), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(q.ComputeError(new float3(0f, 1f, 0f), new float2(0f, 1f)), Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void ErrorIsZeroForAffineCombinationOnTheSubspace()
        {
            var q = UnitTriangle();
            // Centroid lies on the extended affine subspace, so its error must be ~0.
            Assert.That(q.ComputeError(new float3(1f / 3f, 1f / 3f, 0f), new float2(1f / 3f, 1f / 3f)), Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void GeometricDeviationGivesSquaredDistance()
        {
            var q = UnitTriangle();
            // One unit off the plane along Z, with a UV consistent with the in-plane position -> error == 1.
            Assert.That(q.ComputeError(new float3(0f, 0f, 1f), new float2(0f, 0f)), Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void UvDeviationContributesToError()
        {
            var q = UnitTriangle();
            // Same position as a vertex but a wrong UV must produce a strictly positive error.
            var error = q.ComputeError(new float3(0f, 0f, 0f), new float2(0f, 1f));
            Assert.That(error, Is.GreaterThan(Tolerance));
        }

        [Test]
        public void AdditionIsLinearOverError()
        {
            var q1 = UnitTriangle();
            var q2 = FaQemUvQuadric.FromTriangle(
                new float3(0f, 0f, 0f), new float3(0f, 0f, 1f), new float3(0f, 1f, 0f),
                new float2(0f, 0f), new float2(1f, 0f), new float2(0f, 1f));

            var p = new float3(0.2f, 0.3f, 0.4f);
            var u = new float2(0.1f, 0.7f);

            var summed = (q1 + q2).ComputeError(p, u);
            var separate = q1.ComputeError(p, u) + q2.ComputeError(p, u);
            Assert.That(summed, Is.EqualTo(separate).Within(Tolerance));
        }

        [Test]
        public void SolveReportsSingularForRankDeficientQuadric()
        {
            // A single triangle only constrains 2 of the 5 dimensions, so the optimal solve is singular
            // and must report failure (the caller then falls back to endpoint/midpoint candidates).
            var q = UnitTriangle();
            Assert.That(q.TrySolveOptimal(out _, out _), Is.False);
        }
    }
}
