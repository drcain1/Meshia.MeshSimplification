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
            Object.DestroyImmediate(gameObject);
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
            Object.DestroyImmediate(simplifiedMesh);
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

            Object.DestroyImmediate(simplifiedMesh1);
            Mesh simplifiedMesh2 = await SimplifyToTarget(new()
            {
                Kind = MeshSimplificationTargetKind.RelativeVertexCount,
                Value = 0.3f,
            });

            Assert.LessOrEqual(simplifiedMesh2.vertexCount, mesh.vertexCount * 0.3f);
            Object.DestroyImmediate(simplifiedMesh2);

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
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(simplifiedMesh);
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

            Object.DestroyImmediate(source);
            Object.DestroyImmediate(destination);
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
            Object.DestroyImmediate(destination);
        }

        [Test]
        public void ShouldSimplifyBlenderBatchWithDeferredMergeBuffers()
        {
            var sources = new[]
            {
                Object.Instantiate(GetPrimitiveMesh(PrimitiveType.Sphere)),
                Object.Instantiate(GetPrimitiveMesh(PrimitiveType.Capsule)),
                Object.Instantiate(GetPrimitiveMesh(PrimitiveType.Cylinder)),
            };
            var destinations = new[] { new Mesh(), new Mesh(), new Mesh() };
            var target = new MeshSimplificationTarget
            {
                Kind = MeshSimplificationTargetKind.BlenderDecimateRatio,
                Value = 0.5f,
            };
            var parameters = new List<(Mesh Mesh, MeshSimplificationTarget Target, MeshSimplifierOptions Options, Mesh Destination)>();

            try
            {
                for (var i = 0; i < sources.Length; i++)
                {
                    parameters.Add((sources[i], target, MeshSimplifierOptions.Default, destinations[i]));
                }

                MeshSimplifier.SimplifyBatch(parameters);

                for (var i = 0; i < sources.Length; i++)
                {
                    Assert.Greater(destinations[i].vertexCount, 0);
                    Assert.LessOrEqual(destinations[i].triangles.Length, sources[i].triangles.Length / 2);
                    AssertMeshHasNoDegenerateTriangles(destinations[i]);
                }
            }
            finally
            {
                foreach (var source in sources)
                {
                    Object.DestroyImmediate(source);
                }
                foreach (var destination in destinations)
                {
                    Object.DestroyImmediate(destination);
                }
            }
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
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(destination);
        }

        [Test]
        public async Task ShouldSimplifyUvLoopGridDeterministically()
        {
            var source = CreateUvGridMesh(7, 7);
            var destinationA = new Mesh();
            var destinationB = new Mesh();
            var target = new MeshSimplificationTarget
            {
                Kind = MeshSimplificationTargetKind.UvLoopDissolveTriangleCount,
                Value = 40,
            };

            try
            {
                await MeshSimplifier.SimplifyAsync(source, target, MeshSimplifierOptions.Default, destinationA);
                await MeshSimplifier.SimplifyAsync(source, target, MeshSimplifierOptions.Default, destinationB);

                Assert.LessOrEqual(destinationA.triangles.Length / 3, 40);
                Assert.AreEqual(destinationA.vertexCount, destinationA.uv.Length);
                CollectionAssert.AreEqual(destinationA.triangles, destinationB.triangles);
                CollectionAssert.AreEqual(destinationA.vertices, destinationB.vertices);
                AssertMeshHasNoDegenerateTriangles(destinationA);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(destinationA);
                Object.DestroyImmediate(destinationB);
            }
        }

        [Test]
        public async Task ShouldFallbackToBlenderWhenUv0IsMissing()
        {
            var source = CreateUvGridMesh(5, 5);
            source.uv = System.Array.Empty<Vector2>();
            var uvLoopDestination = new Mesh();
            var blenderDestination = new Mesh();

            try
            {
                await MeshSimplifier.SimplifyAsync(source, new MeshSimplificationTarget
                {
                    Kind = MeshSimplificationTargetKind.UvLoopDissolveTriangleCount,
                    Value = 16,
                }, MeshSimplifierOptions.Default, uvLoopDestination);
                await MeshSimplifier.SimplifyAsync(source, new MeshSimplificationTarget
                {
                    Kind = MeshSimplificationTargetKind.BlenderDecimateRatio,
                    Value = 0.5f,
                }, MeshSimplifierOptions.Default, blenderDestination);

                Assert.AreEqual(blenderDestination.triangles.Length, uvLoopDestination.triangles.Length);
                CollectionAssert.AreEqual(blenderDestination.triangles, uvLoopDestination.triangles);
                CollectionAssert.AreEqual(blenderDestination.vertices, uvLoopDestination.vertices);
                AssertMeshHasNoDegenerateTriangles(uvLoopDestination);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(uvLoopDestination);
                Object.DestroyImmediate(blenderDestination);
            }
        }

        [Test]
        public void ShouldSimplifyUvLoopBatch()
        {
            var sources = new[] { CreateUvGridMesh(5, 5), CreateUvGridMesh(7, 5) };
            var destinations = new[] { new Mesh(), new Mesh() };
            var parameters = new List<(Mesh Mesh, MeshSimplificationTarget Target, MeshSimplifierOptions Options, Mesh Destination)>();

            try
            {
                for (var i = 0; i < sources.Length; i++)
                {
                    parameters.Add((sources[i], new MeshSimplificationTarget
                    {
                        Kind = MeshSimplificationTargetKind.UvLoopDissolveTriangleCount,
                        Value = sources[i].triangles.Length / 6,
                    }, MeshSimplifierOptions.Default, destinations[i]));
                }

                MeshSimplifier.SimplifyBatch(parameters);

                for (var i = 0; i < sources.Length; i++)
                {
                    Assert.LessOrEqual(destinations[i].triangles.Length, sources[i].triangles.Length / 2);
                    AssertMeshHasNoDegenerateTriangles(destinations[i]);
                }
            }
            finally
            {
                foreach (var source in sources) Object.DestroyImmediate(source);
                foreach (var destination in destinations) Object.DestroyImmediate(destination);
            }
        }

        [Test]
        public void ShouldNotApplyUvLoopBatchBelowTarget()
        {
            var source = CreateUvGridMesh(7, 7);
            var uvLoopDestination = new Mesh();
            var blenderDestination = new Mesh();

            try
            {
                MeshSimplifier.Simplify(source, new MeshSimplificationTarget
                {
                    Kind = MeshSimplificationTargetKind.UvLoopDissolveTriangleCount,
                    Value = 60,
                }, MeshSimplifierOptions.Default, uvLoopDestination);
                MeshSimplifier.Simplify(source, new MeshSimplificationTarget
                {
                    Kind = MeshSimplificationTargetKind.BlenderDecimateRatio,
                    Value = 60f / 72f,
                }, MeshSimplifierOptions.Default, blenderDestination);

                CollectionAssert.AreEqual(blenderDestination.triangles, uvLoopDestination.triangles);
                CollectionAssert.AreEqual(blenderDestination.vertices, uvLoopDestination.vertices);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(uvLoopDestination);
                Object.DestroyImmediate(blenderDestination);
            }
        }

        [Test]
        public void ShouldPreserveUvLoopSurvivorAttributes()
        {
            var source = CreateUvGridMesh(7, 7);
            var destination = new Mesh();
            var tangents = new Vector4[source.vertexCount];
            var colors = new Color[source.vertexCount];
            var uv2 = new Vector2[source.vertexCount];
            var boneWeights = new BoneWeight[source.vertexCount];
            var blendShapeVertices = new Vector3[source.vertexCount];

            for (var vertex = 0; vertex < source.vertexCount; vertex++)
            {
                tangents[vertex] = new Vector4(1, 0, 0, 1);
                colors[vertex] = Color.Lerp(Color.red, Color.blue, vertex / (source.vertexCount - 1f));
                uv2[vertex] = source.uv[vertex] * 0.5f;
                boneWeights[vertex] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
                blendShapeVertices[vertex] = Vector3.forward * 0.01f;
            }

            source.tangents = tangents;
            source.colors = colors;
            source.uv2 = uv2;
            source.boneWeights = boneWeights;
            source.bindposes = new[] { Matrix4x4.identity };
            source.AddBlendShapeFrame("Offset", 100, blendShapeVertices, null, null);

            try
            {
                MeshSimplifier.Simplify(source, new MeshSimplificationTarget
                {
                    Kind = MeshSimplificationTargetKind.UvLoopDissolveTriangleCount,
                    Value = 40,
                }, MeshSimplifierOptions.Default, destination);

                Assert.AreEqual(destination.vertexCount, destination.normals.Length);
                Assert.AreEqual(destination.vertexCount, destination.tangents.Length);
                Assert.AreEqual(destination.vertexCount, destination.colors.Length);
                Assert.AreEqual(destination.vertexCount, destination.uv.Length);
                Assert.AreEqual(destination.vertexCount, destination.uv2.Length);
                Assert.AreEqual(destination.vertexCount, destination.boneWeights.Length);
                Assert.AreEqual(1, destination.blendShapeCount);
                Assert.AreEqual("Offset", destination.GetBlendShapeName(0));
                AssertMeshHasNoDegenerateTriangles(destination);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(destination);
            }
        }

        [Test]
        public void ShouldPreserveUvLoopMaterialBoundary()
        {
            var source = CreateUvGridMesh(7, 7);
            var destination = new Mesh();
            var triangles = source.triangles;
            var half = triangles.Length / 2;
            var firstSubMesh = new int[half];
            var secondSubMesh = new int[triangles.Length - half];
            System.Array.Copy(triangles, 0, firstSubMesh, 0, firstSubMesh.Length);
            System.Array.Copy(triangles, half, secondSubMesh, 0, secondSubMesh.Length);
            source.subMeshCount = 2;
            source.SetTriangles(firstSubMesh, 0);
            source.SetTriangles(secondSubMesh, 1);

            try
            {
                MeshSimplifier.Simplify(source, new MeshSimplificationTarget
                {
                    Kind = MeshSimplificationTargetKind.UvLoopDissolveTriangleCount,
                    Value = 40,
                }, MeshSimplifierOptions.Default, destination);

                Assert.AreEqual(2, destination.subMeshCount);
                Assert.Greater(destination.GetTriangles(0).Length, 0);
                Assert.Greater(destination.GetTriangles(1).Length, 0);
                AssertMeshHasNoDegenerateTriangles(destination);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(destination);
            }
        }

        [Test]
        public void ShouldKeepSerializedTargetKindValuesStable()
        {
            Assert.AreEqual(0, (int)MeshSimplificationTargetKind.RelativeVertexCount);
            Assert.AreEqual(6, (int)MeshSimplificationTargetKind.BlenderDecimateRatio);
            Assert.AreEqual(7, (int)MeshSimplificationTargetKind.UvLoopDissolveTriangleCount);
        }

        static Mesh CreateUvGridMesh(int width, int height)
        {
            var mesh = new Mesh();
            var vertices = new Vector3[width * height];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[(width - 1) * (height - 1) * 6];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var vertex = y * width + x;
                    vertices[vertex] = new Vector3(x, y, 0);
                    normals[vertex] = Vector3.forward;
                    uvs[vertex] = new Vector2(x / (width - 1f), y / (height - 1f));
                }
            }

            var triangle = 0;
            for (var y = 0; y < height - 1; y++)
            {
                for (var x = 0; x < width - 1; x++)
                {
                    var lowerLeft = y * width + x;
                    triangles[triangle++] = lowerLeft;
                    triangles[triangle++] = lowerLeft + 1;
                    triangles[triangle++] = lowerLeft + width + 1;
                    triangles[triangle++] = lowerLeft;
                    triangles[triangle++] = lowerLeft + width + 1;
                    triangles[triangle++] = lowerLeft + width;
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            return mesh;
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
