using NUnit.Framework;
using Unity.Mathematics;

namespace Meshia.MeshSimplification.Tests
{
    public class FaQemNumericTests
    {
        [Test]
        public void ShouldRejectEdgesBelowRelativeLengthTolerance()
        {
            Assert.That(FaQemMath.IsCollapsibleEdge(double3.zero, new double3(0.5e-8, 0, 0)), Is.False);
            Assert.That(FaQemMath.IsCollapsibleEdge(double3.zero, double3.zero), Is.False);
            Assert.That(FaQemMath.IsCollapsibleEdge(double3.zero, new double3(2e-8, 0, 0)), Is.True);
        }

        [Test]
        public void ShouldMeasureDistanceToTriangleInteriorEdgesAndVertices()
        {
            var a = double3.zero;
            var b = new double3(1, 0, 0);
            var c = new double3(0, 1, 0);
            Assert.That(FaQemSurfaceEnvelope.PointTriangleDistanceSquared(new double3(.2, .3, 2), a, b, c), Is.EqualTo(4).Within(1e-12));
            Assert.That(FaQemSurfaceEnvelope.PointTriangleDistanceSquared(new double3(.5, -.5, 0), a, b, c), Is.EqualTo(.25).Within(1e-12));
            Assert.That(FaQemSurfaceEnvelope.PointTriangleDistanceSquared(new double3(2, 0, 0), a, b, c), Is.EqualTo(1).Within(1e-12));
            Assert.That(FaQemSurfaceEnvelope.PointTriangleDistanceSquared(new double3(0, 0, 2), a, a, a), Is.EqualTo(4).Within(1e-12));
        }

        [Test]
        public void ShouldSolveIntersectionOfThreePlanes()
        {
            var q = FaQemQuadric.Plane(new double3(1, 0, 0), new double3(2, 0, 0)) +
                    FaQemQuadric.Plane(new double3(0, 1, 0), new double3(0, -3, 0)) +
                    FaQemQuadric.Plane(new double3(0, 0, 1), new double3(0, 0, 4));
            Assert.That(q.TrySolve(out var result), Is.True);
            Assert.That(result.x, Is.EqualTo(2d).Within(1e-12));
            Assert.That(result.y, Is.EqualTo(-3d).Within(1e-12));
            Assert.That(result.z, Is.EqualTo(4d).Within(1e-12));
            Assert.That(q.Evaluate(result), Is.EqualTo(0d).Within(1e-10));
        }

        [Test]
        public void ShouldUseDeterministicFallbackForSingularQuadric()
        {
            var q = FaQemQuadric.Plane(new double3(0, 0, 1), double3.zero);
            Assert.That(q.TrySolve(out _), Is.False);
            Assert.That(FaQemMath.SelectFiniteMinimum(q, new double3(3, 0, 0), new double3(1, 0, 0)),
                Is.EqualTo(new double3(3, 0, 0)), "Equal costs select endpoint A deterministically.");
        }

        [Test]
        public void AreaOracleShouldBeTranslationInvariantAndZeroOnLine()
        {
            var a = new double3(4, -2, 3);
            var b = new double3(7, 1, 5);
            var x = a + 0.37d * (b - a);
            Assert.That(FaQemMath.AreaEdgeCost(a, b, x), Is.EqualTo(0d).Within(1e-25));
            var offLine = new double3(6, 3, -1);
            var shift = new double3(-13, 9, 21);
            var cost = FaQemMath.AreaEdgeCost(a, b, offLine);
            Assert.That(cost, Is.GreaterThanOrEqualTo(0d));
            Assert.That(FaQemMath.AreaEdgeCost(a + shift, b + shift, offLine + shift), Is.EqualTo(cost).Within(1e-10));
        }

        [Test]
        public void PlaneQuadricShouldBeTranslationAndUniformScaleConsistent()
        {
            var n = math.normalize(new double3(1, 2, -3));
            var p = new double3(4, 5, 6);
            var x = new double3(-2, 3, 7);
            var q = FaQemQuadric.Plane(n, p);
            var shift = new double3(8, -4, 1);
            Assert.That(FaQemQuadric.Plane(n, p + shift).Evaluate(x + shift), Is.EqualTo(q.Evaluate(x)).Within(1e-10));
            const double scale = 7d;
            Assert.That(FaQemQuadric.Plane(n, p * scale).Evaluate(x * scale), Is.EqualTo(q.Evaluate(x) * scale * scale).Within(1e-9));
        }

        [Test]
        public void SolveShouldBeStableAcrossUniformQuadricScales()
        {
            var baseQuadric = FaQemQuadric.Plane(new double3(1, 0, 0), new double3(2, 0, 0)) +
                              FaQemQuadric.Plane(new double3(0, 1, 0), new double3(0, -3, 0)) +
                              FaQemQuadric.Plane(new double3(0, 0, 1), new double3(0, 0, 4));
            foreach (var multiplier in new[] { 1e-12d, 1e12d })
            {
                var q = baseQuadric * multiplier;
                Assert.That(q.TrySolve(out var result), Is.True);
                Assert.That(result.x, Is.EqualTo(2d).Within(1e-9));
                Assert.That(result.y, Is.EqualTo(-3d).Within(1e-9));
                Assert.That(result.z, Is.EqualTo(4d).Within(1e-9));
            }
        }
    }
}
