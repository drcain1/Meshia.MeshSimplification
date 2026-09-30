using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Meshia.MeshSimplification.Tests
{
    public class FaQemTopologyTests
    {
        [Test]
        public void ShouldCountOpenAndInteriorEdgesWithoutDependingOnWinding()
        {
            using var triangles = new NativeArray<int3>(new[] { new int3(0, 1, 2), new int3(3, 2, 1) }, Allocator.Temp);
            using var discarded = new NativeBitArray(2, Allocator.Temp);
            Assert.That(FaQemTopology.CountIncidentFaces(new int2(0, 1), triangles, discarded), Is.EqualTo(1));
            Assert.That(FaQemTopology.CountIncidentFaces(new int2(1, 2), triangles, discarded), Is.EqualTo(2));
            triangles.ElementAt(1) = new int3(1, 2, 3);
            Assert.That(FaQemTopology.CountIncidentFaces(new int2(1, 2), triangles, discarded), Is.EqualTo(2));
        }

        [Test]
        public void ShouldRejectEdgeWithThreeIncidentFaces()
        {
            using var triangles = new NativeArray<int3>(new[]
            {
                new int3(0, 1, 2), new int3(1, 0, 3), new int3(0, 1, 4),
            }, Allocator.Temp);
            using var discarded = new NativeBitArray(3, Allocator.Temp);
            Assert.That(FaQemTopology.LinkCondition(new int2(0, 1), triangles, discarded), Is.False);
            Assert.That(FaQemTopology.HasNonManifoldIncidentEdge(0, triangles, discarded), Is.True);
        }

        [Test]
        public void ShouldRejectCollapseThatJoinsBowTieNeighborhoods()
        {
            using var triangles = new NativeArray<int3>(new[]
            {
                new int3(0, 1, 2), new int3(1, 0, 3), new int3(0, 4, 5), new int3(1, 4, 6),
            }, Allocator.Temp);
            using var discarded = new NativeBitArray(4, Allocator.Temp);
            Assert.That(FaQemTopology.LinkCondition(new int2(0, 1), triangles, discarded), Is.False);
        }

        [Test]
        public void ShouldFilterRepeatedAndZeroAreaFaces()
        {
            using var positions = new NativeArray<float3>(new[]
            {
                new float3(0, 0, 0), new float3(1, 0, 0), new float3(2, 0, 0), new float3(0, 1, 0),
            }, Allocator.Temp);
            Assert.That(FaQemTopology.IsGeometricallyValid(new int3(0, 0, 3), positions), Is.False);
            Assert.That(FaQemTopology.IsGeometricallyValid(new int3(0, 1, 2), positions), Is.False);
            Assert.That(FaQemTopology.IsGeometricallyValid(new int3(0, 1, 3), positions), Is.True);
        }

        [Test]
        public void GeometricValidityShouldWidenBeforeCrossProduct()
        {
            using var positions = new NativeArray<float3>(new[]
            {
                new float3(-1e20f, 0, 0), new float3(1e20f, 0, 0), new float3(0, 1e20f, 0),
            }, Allocator.Temp);
            Assert.That(FaQemTopology.IsGeometricallyValid(new int3(0, 1, 2), positions), Is.True);
        }

        [Test]
        public void CanonicalEdgeShouldIgnoreEndpointOrder()
        {
            Assert.That(FaQemTopology.CanonicalEdge(8, 2), Is.EqualTo(new int2(2, 8)));
            Assert.That(FaQemTopology.CanonicalEdge(2, 8), Is.EqualTo(new int2(2, 8)));
        }

        [Test]
        public void LocalLinkConditionShouldAgreeWithFullConditionOnOneRing()
        {
            using var triangles = new NativeArray<int3>(new[]
            {
                new int3(0, 1, 2), new int3(0, 2, 3), new int3(0, 3, 4),
            }, Allocator.Temp);
            using var discarded = new NativeBitArray(triangles.Length, Allocator.Temp);
            using var containing = new NativeParallelMultiHashMap<int, int>(triangles.Length * 3, Allocator.Temp);
            for (var i = 0; i < triangles.Length; i++)
            {
                containing.Add(triangles[i].x, i);
                containing.Add(triangles[i].y, i);
                containing.Add(triangles[i].z, i);
            }

            var edge = new int2(0, 2);
            Assert.That(FaQemTopology.LinkConditionLocal(edge, triangles, discarded, containing),
                Is.EqualTo(FaQemTopology.LinkCondition(edge, triangles, discarded)));
            discarded.Set(1, true);
            Assert.That(FaQemTopology.CountIncidentFacesLocal(edge, triangles, discarded, containing), Is.EqualTo(1));
        }

        [Test]
        public void LocalLinkConditionShouldRejectExtraCommonNeighborInEitherOrientation()
        {
            using var triangles = new NativeArray<int3>(new[]
            {
                new int3(0, 1, 2), new int3(0, 2, 3), new int3(0, 3, 4), new int3(2, 4, 5),
            }, Allocator.Temp);
            using var discarded = new NativeBitArray(triangles.Length, Allocator.Temp);
            using var containing = new NativeParallelMultiHashMap<int, int>(triangles.Length * 3, Allocator.Temp);
            for (var i = 0; i < triangles.Length; i++)
            {
                containing.Add(triangles[i].x, i);
                containing.Add(triangles[i].y, i);
                containing.Add(triangles[i].z, i);
            }

            var edge = new int2(0, 2);
            Assert.That(FaQemTopology.LinkConditionLocal(edge, triangles, discarded, containing), Is.False);
            Assert.That(FaQemTopology.LinkConditionLocal(new int2(2, 0), triangles, discarded, containing), Is.False);
            Assert.That(FaQemTopology.LinkConditionLocal(edge, triangles, discarded, containing),
                Is.EqualTo(FaQemTopology.LinkCondition(edge, triangles, discarded)));
        }

        [Test]
        public void LinkConditionShouldRejectCollapseThatCreatesDuplicateFace()
        {
            using var triangles = new NativeArray<int3>(new[]
            {
                new int3(0, 2, 1), new int3(0, 1, 3), new int3(0, 3, 2), new int3(1, 2, 3),
            }, Allocator.Temp);
            using var discarded = new NativeBitArray(triangles.Length, Allocator.Temp);
            using var containing = new NativeParallelMultiHashMap<int, int>(triangles.Length * 3, Allocator.Temp);
            for (var i = 0; i < triangles.Length; i++)
            {
                containing.Add(triangles[i].x, i);
                containing.Add(triangles[i].y, i);
                containing.Add(triangles[i].z, i);
            }

            var edge = new int2(0, 1);
            Assert.That(FaQemTopology.LinkCondition(edge, triangles, discarded), Is.False);
            Assert.That(FaQemTopology.LinkConditionLocal(edge, triangles, discarded, containing), Is.False);
        }

        [Test]
        public void LocalLinkConditionShouldRejectNonManifoldEndpointNeighborhood()
        {
            using var triangles = new NativeArray<int3>(new[]
            {
                new int3(0, 1, 2), new int3(1, 0, 3), new int3(0, 1, 4),
            }, Allocator.Temp);
            using var discarded = new NativeBitArray(triangles.Length, Allocator.Temp);
            using var containing = new NativeParallelMultiHashMap<int, int>(triangles.Length * 3, Allocator.Temp);
            for (var i = 0; i < triangles.Length; i++)
            {
                containing.Add(triangles[i].x, i);
                containing.Add(triangles[i].y, i);
                containing.Add(triangles[i].z, i);
            }

            Assert.That(FaQemTopology.LinkConditionLocal(new int2(0, 1), triangles, discarded, containing), Is.False);
        }
    }
}
