#nullable enable
using System;
using System.Threading.Tasks;
using Meshia.MeshSimplification.Ndmf.Editor.Preview;
using nadena.dev.ndmf.preview;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Meshia.MeshSimplification.Ndmf.Editor.Tests
{
    public class PreviewSkinningPolicyTests
    {
        [TestCase(false, SkinningProtectionPolicy.On, false)]
        [TestCase(true, SkinningProtectionPolicy.On, false)]
        [TestCase(false, SkinningProtectionPolicy.Off, true)]
        [TestCase(true, SkinningProtectionPolicy.Off, true)]
        [TestCase(false, SkinningProtectionPolicy.Auto, true)]
        [TestCase(true, SkinningProtectionPolicy.Auto, true)]
        public async Task ShouldUseResolvedPolicyInPreview(bool cascading, SkinningProtectionPolicy policy, bool legacyEnabled)
        {
            var root = new GameObject("Preview policy fixture");
            root.AddComponent<nadena.dev.ndmf.runtime.components.NDMFAvatarRoot>();
            var child = new GameObject("Skinned surface"); child.transform.SetParent(root.transform);
            var renderer = child.AddComponent<SkinnedMeshRenderer>();
            var source = new Mesh(); var expected = new Mesh();
            var proxyObject = new GameObject("Preview proxy");
            var proxy = proxyObject.AddComponent<SkinnedMeshRenderer>();
            var context = new ComputeContext("Preview skinning regression");
            IRenderFilterNode? node = null;
            try
            {
                const int width = 8;
                var vertices = new Vector3[width * width];
                var weights = new BoneWeight[vertices.Length];
                var triangles = new int[(width - 1) * (width - 1) * 6];
                for (var y = 0; y < width; y++) for (var x = 0; x < width; x++)
                {
                    var i = y * width + x; vertices[i] = new Vector3(x, y, 0);
                    weights[i] = new BoneWeight { boneIndex0 = (x + y) % 2, weight0 = 1f };
                }
                var n = 0;
                for (var y = 0; y < width - 1; y++) for (var x = 0; x < width - 1; x++)
                {
                    var a = y * width + x;
                    triangles[n++] = a; triangles[n++] = a + 1; triangles[n++] = a + width;
                    triangles[n++] = a + 1; triangles[n++] = a + width + 1; triangles[n++] = a + width;
                }
                source.vertices = vertices; source.triangles = triangles;
                source.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity }; source.boneWeights = weights;
                var boneA = new GameObject("Bone A").transform; boneA.SetParent(root.transform);
                var boneB = new GameObject("Bone B").transform; boneB.SetParent(root.transform);
                renderer.bones = proxy.bones = new[] { boneA, boneB };
                source.RecalculateNormals(); renderer.sharedMesh = proxy.sharedMesh = source;
                var options = MeshSimplifierOptions.Default;
                options.SkinningProtection.Policy = policy; options.SkinningProtection.Enabled = legacyEnabled;
                options.SkinningProtection.MaxWeightDistance = 0f;
                var target = new MeshSimplificationTarget { Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = 30 };
                RenderGroup group;
                IRenderFilter filter;
                if (cascading)
                {
                    var c = root.AddComponent<MeshiaCascadingAvatarMeshSimplifier>();
                    c.Entries.Add(new MeshiaCascadingAvatarMeshSimplifierRendererEntry(renderer)
                    { Options = options, Algorithm = MeshiaCascadingSimplificationAlgorithm.FaQem,
                        TargetTriangleCount = 30, PreserveBorderEdgesBones = 0 });
                    group = RenderGroup.For(renderer).WithData((c, 0));
                    filter = new MeshiaCascadingAvatarMeshSimplifierPreview();
                }
                else
                {
                    var c = child.AddComponent<MeshiaMeshSimplifier>(); c.options = options; c.target = target;
                    group = RenderGroup.For(renderer); filter = new MeshiaMeshSimplifierPreview();
                }
                var resolved = options;
                // No humanoid animator: Auto must resolve to off regardless of
                // the serialized legacy flag, exactly as it does in a build.
                resolved.SkinningProtection = options.SkinningProtection.Resolve(false);
                MeshSimplifier.Simplify(source, target, resolved, expected);
                node = await filter.Instantiate(group, new[] { ((Renderer)renderer, (Renderer)proxy) }, context);
                node.OnFrame(renderer, proxy);
                CollectionAssert.AreEqual(expected.vertices, proxy.sharedMesh.vertices);
                CollectionAssert.AreEqual(expected.triangles, proxy.sharedMesh.triangles);
                if (policy != SkinningProtectionPolicy.On)
                    Assert.That(proxy.sharedMesh.triangles.Length, Is.LessThan(source.triangles.Length));
            }
            finally
            {
                context.Invalidate(); (node as IDisposable)?.Dispose();
                Object.DestroyImmediate(proxyObject); Object.DestroyImmediate(root);
                Object.DestroyImmediate(source); Object.DestroyImmediate(expected);
            }
        }
    }
}
