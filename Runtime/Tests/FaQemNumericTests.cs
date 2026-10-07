using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Meshia.MeshSimplification.Tests
{
    public class FaQemNumericTests
    {
        [Test]
        public void ShouldMatchExhaustiveSurfaceQueriesAcrossCacheEvictionAndDifferentTolerances()
        {
            using var positions = new NativeArray<float3>(96, Allocator.Temp);
            using var triangles = new NativeArray<int3>(32, Allocator.Temp);
            using var discarded = new NativeBitArray(32, Allocator.Temp);
            var random = new Unity.Mathematics.Random(1729);
            var writablePositions = positions;
            var writableTriangles = triangles;
            for (var i = 0; i < positions.Length; i++) writablePositions[i] = random.NextFloat3();
            for (var i = 0; i < triangles.Length; i++)
            {
                writableTriangles[i] = new int3(i * 3, i * 3 + 1, i * 3 + 2);
                if (i % 11 == 0) discarded.Set(i, true);
            }
            using var envelope = new FaQemSurfaceEnvelope(positions, triangles, discarded, double3.zero, 1d, .03d);
            var tight = envelope.WithTolerance(.0005d);
            for (var i = 0; i < 5000; i++)
            {
                var point = random.NextDouble3(new double3(-.1), new double3(1.1));
                if (i % 3 == 0)
                {
                    var triangle = triangles[i % triangles.Length];
                    point = ((double3)positions[triangle.x] + positions[triangle.y] + positions[triangle.z]) / 3d;
                }
                var distance = double.PositiveInfinity;
                for (var t = 0; t < triangles.Length; t++)
                {
                    if (discarded.IsSet(t)) continue;
                    var triangle = triangles[t];
                    distance = System.Math.Min(distance, FaQemSurfaceEnvelope.PointTriangleDistanceSquared(point,
                        positions[triangle.x], positions[triangle.y], positions[triangle.z]));
                }
                Assert.AreEqual(distance <= .03d * .03d, envelope.Contains(point));
                Assert.AreEqual(distance <= .0005d * .0005d, tight.Contains(point), "A looser cache entry must not satisfy a cut guard.");
                Assert.AreEqual(distance <= .03d * .03d, envelope.Contains(point));
            }
        }

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
