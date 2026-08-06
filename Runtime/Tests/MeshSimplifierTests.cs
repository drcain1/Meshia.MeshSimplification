using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meshia.MeshSimplification;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace Meshia.MeshSimplification.Tests
{
    public class MeshSimplifierTests
    {
        static Mesh GetPrimitiveMesh(PrimitiveType type)
        {
            var gameObject = GameObject.CreatePrimitive(type);
            var mesh = gameObject.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(gameObject);
            return mesh;
        }
        [TestCase(PrimitiveType.Sphere)]
        [TestCase(PrimitiveType.Capsule)]
        [TestCase(PrimitiveType.Cylinder)]
        public async Task ShouldSimplifyPrimitive(PrimitiveType type)
        {
            var mesh = GetPrimitiveMesh(type);

            MeshSimplificationTarget target = new()
            {
                Kind = MeshSimplificationTargetKind.RelativeVertexCount,
                Value = 0.5f,
            };
            Mesh simplifiedMesh = new();
            await MeshSimplifier.SimplifyAsync(mesh, target, MeshSimplifierOptions.Default, simplifiedMesh);
            Assert.LessOrEqual(simplifiedMesh.vertexCount, mesh.vertexCount * 0.5f);
            Object.Destroy(simplifiedMesh);
        }
        [TestCase(PrimitiveType.Sphere)]
        [TestCase(PrimitiveType.Capsule)]
        [TestCase(PrimitiveType.Cylinder)]
        public async Task ShouldSimplifyPrimitiveIncrementally(PrimitiveType type)
        {
            var allocator = Allocator.Persistent;
            var mesh = GetPrimitiveMesh(type);
            using var blendShapes = BlendShapeData.GetMeshBlendShapes(mesh, allocator);

            Assert.Zero(blendShapes.Length, "Primitive meshes should not have blend shapes by default.");
            var meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh);
            var meshData = meshDataArray[0];

            using MeshSimplifier meshSimplifier = new(allocator);

            var load = meshSimplifier.ScheduleLoadMeshData(meshData, MeshSimplifierOptions.Default);

            while (!load.IsCompleted)
            {
                await Task.Yield();
            }

            load.Complete();

            Mesh simplifiedMesh1 = await SimplifyToTarget(new()
            {
                Kind = MeshSimplificationTargetKind.RelativeVertexCount,
                Value = 0.5f,
            });

            Assert.LessOrEqual(simplifiedMesh1.vertexCount, mesh.vertexCount * 0.5f);

            Object.Destroy(simplifiedMesh1);
            Mesh simplifiedMesh2 = await SimplifyToTarget(new()
            {
                Kind = MeshSimplificationTargetKind.RelativeVertexCount,
                Value = 0.3f,
            });

            Assert.LessOrEqual(simplifiedMesh2.vertexCount, mesh.vertexCount * 0.3f);
            Object.Destroy(simplifiedMesh2);

            async Task<Mesh> SimplifyToTarget(MeshSimplificationTarget target)
            {
                var destinationMeshDataArray = Mesh.AllocateWritableMeshData(1);
                var destinationMeshData = destinationMeshDataArray[0];

                Mesh simplifiedMesh = new();
                var simplify = meshSimplifier.ScheduleSimplify(meshData, blendShapes, target, new JobHandle());

                using NativeList<BlendShapeData> destinationBlendShapes = new(allocator);
                var write = meshSimplifier.ScheduleWriteMeshData(meshData, blendShapes, destinationMeshData, destinationBlendShapes, simplify);

                while (!write.IsCompleted)
                {
                    await Task.Yield();
                }

                write.Complete();

                Assert.Zero(destinationBlendShapes.Length, "Primitive meshes should not have blend shapes after simplification.");

                Mesh.ApplyAndDisposeWritableMeshData(destinationMeshDataArray, simplifiedMesh);
                return simplifiedMesh;
            }
        }
        [TestCase(PrimitiveType.Sphere)]
        [TestCase(PrimitiveType.Capsule)]
        [TestCase(PrimitiveType.Cylinder)]
        public async Task ShouldSimplifyPrimitiveWithDuplicatedSubMeshes(PrimitiveType type)
        {
            var mesh = Object.Instantiate(GetPrimitiveMesh(type));
            var originalSubMeshCount = mesh.subMeshCount;
            mesh.subMeshCount += 1;
            mesh.SetTriangles(mesh.GetTriangles(originalSubMeshCount - 1), originalSubMeshCount);

            MeshSimplificationTarget target = new()
            {
                Kind = MeshSimplificationTargetKind.RelativeVertexCount,
                Value = 0.5f,
            };
            Mesh simplifiedMesh = new();
            await MeshSimplifier.SimplifyAsync(mesh, target, MeshSimplifierOptions.Default, simplifiedMesh);
            Assert.LessOrEqual(simplifiedMesh.vertexCount, mesh.vertexCount * 0.5f);
            Object.Destroy(mesh);
            Object.Destroy(simplifiedMesh);
        }

        [Test]
        public async Task ShouldSimplifyWithBlenderDecimateRatio()
        {
            var source = Object.Instantiate(GetPrimitiveMesh(PrimitiveType.Sphere));
            var sourceTriangleCount = source.triangles.Length / 3;
            Mesh destination = new();

            await MeshSimplifier.SimplifyAsync(source, new MeshSimplificationTarget
            {
                Kind = MeshSimplificationTargetKind.BlenderDecimateRatio,
                Value = 0.5f,
            }, MeshSimplifierOptions.Default, destination);

            Assert.LessOrEqual(destination.triangles.Length / 3, sourceTriangleCount * 0.5f);
            Assert.AreEqual(destination.vertexCount, destination.uv.Length);
            AssertMeshHasNoDegenerateTriangles(destination);

            Object.Destroy(source);
            Object.Destroy(destination);
        }

        [Test]
        public async Task ShouldKeepMeshForFullBlenderDecimateRatio()
        {
            var source = GetPrimitiveMesh(PrimitiveType.Cube);
            Mesh destination = new();

            await MeshSimplifier.SimplifyAsync(source, new MeshSimplificationTarget
            {
                Kind = MeshSimplificationTargetKind.BlenderDecimateRatio,
                Value = 1f,
            }, MeshSimplifierOptions.Default, destination);

            Assert.AreEqual(source.vertexCount, destination.vertexCount);
            Assert.AreEqual(source.triangles.Length, destination.triangles.Length);
            Object.Destroy(destination);
        }

        [Test]
        public void ShouldMatchBlenderTopologyFallbackCost()
        {
            var cost = SimplifyJob.ComputeBlenderTopologyFallbackCost(2f, 0.5f, 0f);

            Assert.That(cost, Is.EqualTo(-0.75f).Within(1e-6f));
        }

        [Test]
        public void ShouldOptimizeBlenderQuadricsInDoublePrecision()
        {
            var quadric =
                new BlenderErrorQuadric(new double3(1, 0, 0), new double3(1, 0, 0)) +
                new BlenderErrorQuadric(new double3(0, 1, 0), new double3(0, 2, 0)) +
                new BlenderErrorQuadric(new double3(0, 0, 1), new double3(0, 0, 3));

            Assert.IsTrue(quadric.TryOptimize(out var position));
            Assert.That(position.x, Is.EqualTo(1.0).Within(1e-12));
            Assert.That(position.y, Is.EqualTo(2.0).Within(1e-12));
            Assert.That(position.z, Is.EqualTo(3.0).Within(1e-12));
            Assert.That(quadric.Evaluate(position), Is.EqualTo(0.0).Within(1e-12));
        }

        [Test]
        public async Task ShouldSimplifyFlatGridWithBlenderTopologyFallback()
        {
            const int side = 5;
            Mesh source = new();
            var vertices = new Vector3[side * side];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[(side - 1) * (side - 1) * 6];

            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    var index = y * side + x;
                    vertices[index] = new Vector3(x, y, 0);
                    normals[index] = Vector3.forward;
                    uvs[index] = new Vector2(x / (side - 1f), y / (side - 1f));
                }
            }

            var triangleIndex = 0;
            for (var y = 0; y < side - 1; y++)
            {
                for (var x = 0; x < side - 1; x++)
                {
                    var lowerLeft = y * side + x;
                    triangles[triangleIndex++] = lowerLeft;
                    triangles[triangleIndex++] = lowerLeft + 1;
                    triangles[triangleIndex++] = lowerLeft + side + 1;
                    triangles[triangleIndex++] = lowerLeft;
                    triangles[triangleIndex++] = lowerLeft + side + 1;
                    triangles[triangleIndex++] = lowerLeft + side;
                }
            }

            source.vertices = vertices;
            source.normals = normals;
            source.uv = uvs;
            source.triangles = triangles;
            Mesh destination = new();

            await MeshSimplifier.SimplifyAsync(source, new MeshSimplificationTarget
            {
                Kind = MeshSimplificationTargetKind.BlenderDecimateRatio,
                Value = 0.5f,
            }, MeshSimplifierOptions.Default, destination);

            Assert.LessOrEqual(destination.triangles.Length, triangles.Length / 2);
            AssertMeshHasNoDegenerateTriangles(destination);
            Object.Destroy(source);
            Object.Destroy(destination);
        }

        static void AssertMeshHasNoDegenerateTriangles(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var b = vertices[triangles[i + 1]];
                var c = vertices[triangles[i + 2]];
                Assert.Greater(Vector3.Cross(b - a, c - a).sqrMagnitude, 1e-12f, $"Triangle {i / 3} is degenerate.");
            }
        }
    }

}
