using System;
using NUnit.Framework;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Meshia.MeshSimplification.Tests
{
    public class FaQemDeformationTests
    {
        [TestCase(true, false, 0f)]
        [TestCase(true, false, 0.01f)]
        [TestCase(false, true, 0f)]
        [TestCase(true, true, 0f)]
        [TestCase(false, false, 0f)]
        public void ShouldKeepFlatSourceFacesThatCanOpenUnderDeformation(
            bool blendShape, bool skinning, float surfaceDeviation)
        {
            var source = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.right * 2, Vector3.up },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            var destination = new Mesh();
            try
            {
                if (blendShape)
                {
                    source.AddBlendShapeFrame("Open flat face", 100,
                        new[] { Vector3.zero, Vector3.up, Vector3.zero, Vector3.zero }, null, null);
                }
                if (skinning)
                {
                    source.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
                    source.boneWeights = new[]
                    {
                        new BoneWeight { boneIndex0 = 0, weight0 = 1 },
                        new BoneWeight { boneIndex0 = 1, weight0 = 1 },
                        new BoneWeight { boneIndex0 = 0, weight0 = 1 },
                        new BoneWeight { boneIndex0 = 0, weight0 = 1 },
                    };
                }
                var options = MeshSimplifierOptions.Default;
                options.EnableSmartLink = false;
                options.PreserveBorderEdges = false;
                options.FaQem.PreserveAttributeSeams = false;
                options.FaQem.MaxSurfaceDeviation = surfaceDeviation;
                var history = MeshSimplifier.SimplifyWithHistory(source,
                    new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 0 },
                    options, null, destination);
                var flatFace = Array.IndexOf(history.OutputTriangleToSourceTriangle, 0);
                if (!blendShape && !skinning)
                {
                    Assert.That(flatFace, Is.EqualTo(-1), "Static zero-area faces can still be removed.");
                    return;
                }

                Assert.That(flatFace, Is.GreaterThanOrEqualTo(0), "The animated source face must survive.");
                var positions = destination.vertices;
                var delta = new Vector3[destination.vertexCount];
                if (blendShape) destination.GetBlendShapeFrameVertices(0, 0, delta, null, null);
                var weights = destination.boneWeights;
                var face = new Vector3[3];
                for (var corner = 0; corner < 3; corner++)
                {
                    var index = destination.triangles[flatFace * 3 + corner];
                    var sourceIndex = history.OutputVertexToSourceVertex[index];
                    Assert.That(positions[index], Is.EqualTo(source.vertices[sourceIndex]));
                    face[corner] = positions[index] + delta[index];
                    if (skinning && weights[index].boneIndex0 == 1) face[corner] += Vector3.up;
                }
                Assert.That(Vector3.Cross(face[1] - face[0], face[2] - face[0]).sqrMagnitude,
                    Is.GreaterThan(0), "The retained face must open when its deformation is applied.");
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(destination);
            }
        }

        static Mesh MakeSkinnedCube()
        {
            var mesh = new Mesh { name = "FA-QEM skinned deformation fixture" };
            mesh.vertices = new[]
            {
                new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(1, 1, -1), new Vector3(-1, 1, -1),
                new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 1, 1), new Vector3(-1, 1, 1),
            };
            mesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2,
                0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5,
            };
            var weights = new BoneWeight[mesh.vertexCount];
            for (var i = 0; i < weights.Length; i++)
            {
                weights[i] = new BoneWeight
                {
                    boneIndex0 = 0, boneIndex1 = 1,
                    weight0 = i < 4 ? 0.75f : 0.25f,
                    weight1 = i < 4 ? 0.25f : 0.75f,
                };
            }
            mesh.boneWeights = weights;
            mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.Translate(new Vector3(0.5f, 0, 0)) };
            var frameA = new Vector3[mesh.vertexCount];
            var frameB = new Vector3[mesh.vertexCount];
            for (var i = 0; i < mesh.vertexCount; i++)
            {
                frameA[i] = Vector3.up * 0.1f;
                frameB[i] = Vector3.right * 0.2f;
            }
            mesh.AddBlendShapeFrame("Lift", 50, frameA, null, null);
            mesh.AddBlendShapeFrame("Lift", 100, frameB, null, null);
            return mesh;
        }

        [TestCase(0f)]
        [TestCase(0.1f)]
        public void ShouldPreserveSkinnedWeightsAndBlendShapeChannels(float surfaceDeviation)
        {
            var source = MakeSkinnedCube();
            var destination = new Mesh();
            GameObject rig = null;
            Mesh baked = null;
            Scene previewScene = default;
            var sourceVertices = source.vertices;
            var sourceTriangles = source.triangles;
            try
            {
                var options = MeshSimplifierOptions.Default;
                options.FaQem.MaxSurfaceDeviation = surfaceDeviation;
                MeshSimplifier.Simplify(source,
                    new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 8 },
                    options, destination);

                Assert.That(destination.triangles.Length, Is.LessThan(source.triangles.Length));
                Assert.That(destination.bindposes.Length, Is.EqualTo(2));
                Assert.That(destination.bindposes[0], Is.EqualTo(source.bindposes[0]));
                Assert.That(destination.bindposes[1], Is.EqualTo(source.bindposes[1]));
                using var bonesPerVertex = destination.GetBonesPerVertex();
                using var allWeights = destination.GetAllBoneWeights();
                Assert.That(bonesPerVertex.Length, Is.EqualTo(destination.vertexCount));
                var weightOffset = 0;
                for (var vertex = 0; vertex < bonesPerVertex.Length; vertex++)
                {
                    var sum = 0f;
                    for (var j = 0; j < bonesPerVertex[vertex]; j++)
                    {
                        var weight = allWeights[weightOffset++];
                        Assert.That(weight.boneIndex, Is.InRange(0, 1));
                        Assert.That(weight.weight, Is.InRange(0f, 1f));
                        sum += weight.weight;
                    }
                    Assert.That(sum, Is.EqualTo(1f).Within(0.02f));
                }
                Assert.That(weightOffset, Is.EqualTo(allWeights.Length));
                Assert.That(destination.blendShapeCount, Is.EqualTo(1));
                Assert.That(destination.GetBlendShapeName(0), Is.EqualTo("Lift"));
                Assert.That(destination.GetBlendShapeFrameCount(0), Is.EqualTo(2));
                for (var frame = 0; frame < 2; frame++)
                {
                    var delta = new Vector3[destination.vertexCount];
                    destination.GetBlendShapeFrameVertices(0, frame, delta, null, null);
                    var nonzero = false;
                    foreach (var value in delta)
                    {
                        Assert.That(float.IsNaN(value.x) || float.IsInfinity(value.x) ||
                                    float.IsNaN(value.y) || float.IsInfinity(value.y) ||
                                    float.IsNaN(value.z) || float.IsInfinity(value.z), Is.False);
                        nonzero |= value.sqrMagnitude > 1e-12f;
                    }
                    Assert.That(nonzero, Is.True, $"Blend-shape frame {frame} was lost.");
                    var expected = frame == 0 ? new Vector3(0, 0.1f, 0) : new Vector3(0.2f, 0, 0);
                    foreach (var value in delta)
                        Assert.That((value - expected).sqrMagnitude, Is.LessThan(1e-8f));
                }
                rig = new GameObject("FA-QEM deformation rig") { hideFlags = HideFlags.HideAndDontSave };
                previewScene = EditorSceneManager.NewPreviewScene();
                SceneManager.MoveGameObjectToScene(rig, previewScene);
                var bone0 = new GameObject("Bone0").transform;
                var bone1 = new GameObject("Bone1").transform;
                bone0.SetParent(rig.transform, false);
                bone1.SetParent(rig.transform, false);
                bone1.localRotation = Quaternion.Euler(0, 0, 35);
                var renderer = rig.AddComponent<SkinnedMeshRenderer>();
                renderer.enabled = false;
                renderer.sharedMesh = destination;
                renderer.bones = new[] { bone0, bone1 };
                renderer.rootBone = bone0;
                renderer.SetBlendShapeWeight(0, 100);
                baked = new Mesh();
                renderer.BakeMesh(baked);
                var outputWeights = destination.boneWeights;
                var frameDelta = new Vector3[destination.vertexCount];
                destination.GetBlendShapeFrameVertices(0, 1, frameDelta, null, null);
                for (var i = 0; i < destination.vertexCount; i++)
                {
                    var weight = outputWeights[i];
                    var basePosition = destination.vertices[i] + frameDelta[i];
                    var matrices = new[]
                    {
                        bone0.localToWorldMatrix * destination.bindposes[0],
                        bone1.localToWorldMatrix * destination.bindposes[1],
                    };
                    var expected = matrices[weight.boneIndex0].MultiplyPoint3x4(basePosition) * weight.weight0;
                    expected += matrices[weight.boneIndex1].MultiplyPoint3x4(basePosition) * weight.weight1;
                    Assert.That((baked.vertices[i] - expected).sqrMagnitude, Is.LessThan(1e-8f));
                }
                Assert.That(source.vertices, Is.EqualTo(sourceVertices));
                Assert.That(source.triangles, Is.EqualTo(sourceTriangles));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(destination);
                if (rig != null) Object.DestroyImmediate(rig);
                if (baked != null) Object.DestroyImmediate(baked);
                if (previewScene.IsValid() && previewScene.isLoaded) EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

    }
}
