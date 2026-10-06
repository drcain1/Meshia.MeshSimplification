#nullable enable
#if ENABLE_AAO_CUT_PREPARATION
using System.Collections.Generic;
using System.Linq;
using Anatawa12.AvatarOptimizer;
using Anatawa12.AvatarOptimizer.API;
using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEngine;

namespace Meshia.MeshSimplification.Ndmf.Editor.Aao
{
    internal static class AaoCutPreparation
    {
        [InitializeOnLoadMethod]
        private static void Register() => CutMeshPreparation.Register(Prepare);

        internal static Mesh? Prepare(Renderer renderer, Mesh input, ComputeContext? context)
        {
            if (renderer is not SkinnedMeshRenderer || !input.isReadable) return null;
            IEnumerable<RemoveMeshByBlendShape> components = context != null
                ? context.GetComponents<RemoveMeshByBlendShape>(renderer.gameObject)
                : renderer.GetComponents<RemoveMeshByBlendShape>();
            if (!components.Any()) return null;
            GameObject? scratch = null;
            Mesh? result = null;
            try
            {
                // Query AAO against the exact upstream mesh. Never swap a live
                // renderer's mesh or assume preview indices match the authored mesh.
                scratch = new GameObject("Meshia cut prediction") { hideFlags = HideFlags.HideAndDontSave };
                var copy = scratch.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = input;
                var supported = false;
                foreach (var component in components)
                {
                    context?.Observe(component);
                    var settings = scratch.AddComponent<RemoveMeshByBlendShape>();
                    EditorUtility.CopySerialized(component, settings);
                    settings.Initialize(1);
                    // Inverted selections are not closed under interpolating deltas.
                    // Leave them to AAO until they have their own validated integration.
                    if (settings.InvertSelection)
                        Object.DestroyImmediate(settings);
                    else supported = true;
                }
                if (!supported) return null;
                // Dispose the predictor before changing any mesh data, as AAO requires.
                var kept = new Dictionary<int, List<int>>();
                using (var provider = MeshRemovalProvider.GetForRenderer(copy))
                {
                    if (provider == null) return null;
                    var triangle = new int[3];
                    for (var submesh = 0; submesh < input.subMeshCount; submesh++)
                    {
                        if (input.GetTopology(submesh) != MeshTopology.Triangles) continue;
                        var indices = input.GetIndices(submesh);
                        var remaining = new List<int>(indices.Length);
                        for (var i = 0; i < indices.Length; i += 3)
                        {
                            triangle[0] = indices[i]; triangle[1] = indices[i + 1]; triangle[2] = indices[i + 2];
                            if (provider.WillRemovePrimitive(MeshTopology.Triangles, submesh, triangle)) continue;
                            remaining.AddRange(triangle);
                        }
                        if (remaining.Count != indices.Length) kept.Add(submesh, remaining);
                    }
                }
                if (kept.Count == 0) return null;
                result = Object.Instantiate(input);
                result.hideFlags = HideFlags.HideAndDontSave;
                // Retain vertex indices, every attribute, blendshape frame, and slot.
                foreach (var pair in kept) result.SetTriangles(pair.Value, pair.Key, false);
                return result;
            }
            catch
            {
                if (result != null) Object.DestroyImmediate(result);
                throw;
            }
            finally
            {
                if (scratch != null) Object.DestroyImmediate(scratch);
            }
        }
    }
}
#endif
