#nullable enable
using System;
using System.Linq;
using System.Reflection;
using Meshia.MeshSimplification.Ndmf.Editor;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Meshia.MeshSimplification.Ndmf.Tests
{
    public class GeometryOnlyNdmfTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ShouldRetainMaterialsTexturesAndUvChannels(bool cascading)
        {
            var avatar = new GameObject("Geometry-only fixture");
            avatar.AddComponent<nadena.dev.ndmf.runtime.components.NDMFAvatarRoot>();
            var child = new GameObject("Mesh");
            child.transform.SetParent(avatar.transform);
            var filter = child.AddComponent<MeshFilter>();
            var renderer = child.AddComponent<MeshRenderer>();
            var source = CreateGrid();
            var expected = new Mesh();
            var texture = new Texture2D(2, 2);
            var material = new Material(Shader.Find("Unlit/Texture"));
            material.mainTexture = texture;
            material.mainTextureScale = new Vector2(2, 3);
            material.mainTextureOffset = new Vector2(.2f, .3f);
            filter.sharedMesh = source;
            renderer.sharedMaterial = material;
            var sourceUvs = source.uv;
            var sourceSecondaryUvs = source.uv2;
            var sourceVertices = source.vertices;
            var sourceIndices = source.triangles;
            // Old scene data must not re-enable baking or affect geometry options.
            var json = JsonUtility.ToJson(FaQemOptions.Default);
            json = json.Substring(0, json.Length - 1) + ",\"BakeTextures\":true,\"AtlasResolution\":256,\"AtlasPadding\":32,\"AtlasCompression\":1}";
            var options = MeshSimplifierOptions.Default;
            options.FaQem = JsonUtility.FromJson<FaQemOptions>(json);
            var target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 30 };
            if (cascading)
            {
                var controller = avatar.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
                controller.Entries.Add(new MeshiaCascadingAvatarMeshSimplifierRendererEntry(renderer)
                {
                    TargetTriangleCount = 30,
                    Options = options,
                    PreserveBorderEdgesBones = 0,
                });
            }
            else
            {
                var controller = child.AddComponent<MeshiaMeshSimplifier>();
                controller.target = target;
                controller.options = options;
            }
            var context = new BuildContext(avatar, null, false);
            Mesh? output = null;
            try
            {
                MeshSimplifier.Simplify(source, target, options, expected);
                InvokeSimplify(context);
                output = filter.sharedMesh;
                Assert.That(output, Is.Not.SameAs(source));
                Assert.That(output.triangles.Length, Is.LessThan(sourceIndices.Length));
                CollectionAssert.AreEqual(expected.vertices, output.vertices);
                CollectionAssert.AreEqual(expected.triangles, output.triangles);
                CollectionAssert.AreEqual(expected.uv, output.uv);
                CollectionAssert.AreEqual(expected.uv2, output.uv2);
                Assert.That(output.uv2.Length, Is.EqualTo(output.vertexCount));
                Assert.That(output.uv2.All(uv => uv == new Vector2(2.25f, 1.25f)), Is.True);
                Assert.That(renderer.sharedMaterial, Is.SameAs(material));
                Assert.That(renderer.sharedMaterial.mainTexture, Is.SameAs(texture));
                Assert.That(material.mainTextureScale, Is.EqualTo(new Vector2(2, 3)));
                Assert.That(material.mainTextureOffset, Is.EqualTo(new Vector2(.2f, .3f)));
                CollectionAssert.AreEqual(sourceUvs, source.uv);
                CollectionAssert.AreEqual(sourceSecondaryUvs, source.uv2);
                CollectionAssert.AreEqual(sourceVertices, source.vertices);
                CollectionAssert.AreEqual(sourceIndices, source.triangles);
                Assert.That(((IObjectRegistry)context.ObjectRegistry).GetReference(output, false)?.Object, Is.SameAs(source));
                Assert.That(avatar.GetComponentsInChildren<MeshiaMeshSimplifier>(), Is.Empty);
                Assert.That(avatar.GetComponentsInChildren<MeshiaCascadingAvatarMeshSimplifier>(), Is.Empty);
            }
            finally
            {
                if (output != null && output != source) Object.DestroyImmediate(output);
                Object.DestroyImmediate(expected);
                Object.DestroyImmediate(avatar);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ShouldReleaseTemporaryMeshesAndKeepSourceOnInvalidBudget()
        {
            var avatar = new GameObject("Failing geometry fixture");
            var filter = avatar.AddComponent<MeshFilter>();
            avatar.AddComponent<MeshRenderer>();
            var source = CreateGrid();
            filter.sharedMesh = source;
            var controller = avatar.AddComponent<MeshiaMeshSimplifier>();
            controller.target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = -1 };
            var context = new BuildContext(avatar, null, false);
            var before = Resources.FindObjectsOfTypeAll<Mesh>().Select(mesh => mesh.GetInstanceID()).ToArray();
            try
            {
                var exception = Assert.Throws<TargetInvocationException>(() => InvokeSimplify(context));
                Assert.That(exception!.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(filter.sharedMesh, Is.SameAs(source));
                Assert.That(avatar.GetComponent<MeshiaMeshSimplifier>(), Is.Null);
                CollectionAssert.AreEquivalent(before, Resources.FindObjectsOfTypeAll<Mesh>().Select(mesh => mesh.GetInstanceID()).ToArray());
            }
            finally
            {
                Object.DestroyImmediate(avatar);
                Object.DestroyImmediate(source);
            }
        }

        private static void InvokeSimplify(BuildContext context)
        {
            var method = typeof(NdmfPlugin).GetMethod("Simplify", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method!.Invoke(null, new object[] { context });
        }

        private static Mesh CreateGrid()
        {
            const int size = 7;
            var vertices = new Vector3[size * size];
            var uv = new Vector2[vertices.Length];
            var uv1 = new Vector2[vertices.Length];
            var triangles = new int[(size - 1) * (size - 1) * 6];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var i = y * size + x;
                vertices[i] = new Vector3(x, y, 0);
                uv[i] = new Vector2(x / 6f, y / 6f);
                uv1[i] = new Vector2(2.25f, 1.25f);
            }
            var index = 0;
            for (var y = 0; y < size - 1; y++)
            for (var x = 0; x < size - 1; x++)
            {
                var a = y * size + x;
                triangles[index++] = a; triangles[index++] = a + 1; triangles[index++] = a + size;
                triangles[index++] = a + 1; triangles[index++] = a + size + 1; triangles[index++] = a + size;
            }
            var mesh = new Mesh { vertices = vertices, uv = uv, uv2 = uv1, triangles = triangles };
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
