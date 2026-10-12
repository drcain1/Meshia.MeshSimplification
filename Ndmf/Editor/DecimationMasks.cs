#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEngine;

namespace Meshia.MeshSimplification.Ndmf.Editor
{
    // Selections are immutable managed snapshots. Native copies belong to each simplification job.
    [InitializeOnLoad]
    internal static class DecimationMasks
    {
        private sealed class CacheEntry
        {
            internal Mesh Mesh = null!;
            internal string Key = "";
            internal ProtectedVertexSelection Selection = null!;
            internal readonly ComputeContext Geometry = new("Meshia protected-area selection");
        }

        private static readonly List<CacheEntry> Cache = new();
        static DecimationMasks()
        {
            Undo.undoRedoPerformed += Clear;
            EditorApplication.projectChanged += Clear;
            EditorApplication.hierarchyChanged += Clear;
        }

        internal static void Clear() => Cache.Clear();
        private static string Configuration(IEnumerable<DecimationMask>? masks) => masks == null ? "" : string.Join(";", masks.Where(m => m != null).Select(m => $"{m.Enabled}:{m.MaterialSlot}:{m.Texture?.GetInstanceID()}:{m.SourceMesh?.GetInstanceID()}"));
        internal static List<DecimationMask>? Find(Renderer renderer, ComputeContext? context = null)
        {
            var single = renderer.GetComponent<MeshiaMeshSimplifier>();
            if (single != null && single.enabled)
            {
                context?.Observe(single, c => Configuration(c.ProtectedAreas));
                return single.ProtectedAreas;
            }

#if ENABLE_MODULAR_AVATAR
            var root = nadena.dev.ndmf.runtime.RuntimeUtil.FindAvatarInParents(renderer.transform);
            var scope = root != null ? root.transform : renderer.transform.root;
            foreach (var component in scope.GetComponentsInChildren<MeshiaCascadingAvatarMeshSimplifier>(true))
            {
                // Build clones and toggleable avatar objects can be inactive.
                // A hierarchy's visibility must not discard explicit protection.
                if (!component.enabled) continue;
                var entry = component.Entries.Find(e => e.Enabled && e.GetTargetRenderer(component) == renderer);
                if (entry == null) continue;
                context?.Observe(component, c => Configuration(c.Entries.Find(e => e.GetTargetRenderer(c) == renderer)?.ProtectedAreas));
                return entry.ProtectedAreas;
            }
#endif
            return null;
        }

        internal static ProtectedVertexSelection? Resolve(Renderer renderer, Mesh input, ComputeContext? context = null)
        {
            var masks = Find(renderer, context)?.Where(m => m != null && m.Enabled).ToArray();
            if (masks == null || masks.Length == 0)
                return null;
            context?.Observe(input);
            foreach (var mask in masks)
                if (mask.Texture != null)
                    context?.Observe(mask.Texture, t => (t.imageContentsHash, t.updateCount));
            var key = string.Join(";", masks.Select(m => $"{m.SourceMesh?.GetInstanceID()}:{m.MaterialSlot}:{m.Texture?.GetInstanceID()}:{m.Texture?.imageContentsHash}:{m.Texture?.updateCount}:{m.Texture?.width}:{m.Texture?.height}:{m.Texture?.wrapModeU}:{m.Texture?.wrapModeV}"));
            var cached = Cache.Find(c => c.Mesh == input && c.Key == key && !c.Geometry.IsInvalidated);
            if (cached != null)
                return cached.Selection;
            ProtectedVertexSelection selection;
            try
            {
                var authoring = RendererUtility.GetRequiredMesh(renderer);
                if (masks.Any(m => m.SourceMesh != null && !SameLayout(m.SourceMesh, input)) || authoring != input && !SameLayout(authoring, input))
                    throw new InvalidOperationException("An earlier build step changed this mesh's UV layout or material slots.");
                selection = Select(input, masks);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"Meshia: '{renderer.name}' is kept unchanged because its protected areas cannot be resolved. {error.Message}", renderer);
                selection = new ProtectedVertexSelection(Enumerable.Repeat(true, input.vertexCount));
            }

            if (Cache.Count >= 64)
                Cache.RemoveAt(0);
            var entry = new CacheEntry{Mesh = input, Key = key, Selection = selection};
            entry.Geometry.Observe(input);
            foreach (var mask in masks)
                if (mask.SourceMesh != null)
                    entry.Geometry.Observe(mask.SourceMesh);
            Cache.Add(entry);
            return selection;
        }

        internal static MeshSimplificationTarget SupportedTarget(MeshSimplificationTarget target, Mesh mesh, ProtectedVertexSelection? selection)
        {
            if (selection != null && selection.Count > 0 && target.Kind != MeshSimplificationTargetKind.FaQemTriangleCount)
                return new MeshSimplificationTarget{Kind = MeshSimplificationTargetKind.FaQemTriangleCount, Value = mesh.GetTriangleCount()};
            return target;
        }

        private static bool SameLayout(Mesh source, Mesh input)
        {
            if (source.vertexCount != input.vertexCount || source.subMeshCount != input.subMeshCount || !source.uv.SequenceEqual(input.uv))
                return false;
            for (var slot = 0; slot < source.subMeshCount; slot++)
            {
                var original = source.GetTriangles(slot);
                var faces = new HashSet<(int, int, int)>();
                for (var i = 0; i < original.Length; i += 3)
                    faces.Add((original[i], original[i + 1], original[i + 2]));
                var prepared = input.GetTriangles(slot);
                for (var i = 0; i < prepared.Length; i += 3)
                    if (!faces.Contains((prepared[i], prepared[i + 1], prepared[i + 2])))
                        return false;
            }

            return true;
        }

        internal static ProtectedVertexSelection Select(Mesh mesh, IEnumerable<DecimationMask> masks)
        {
            var uv = mesh.uv;
            if (uv.Length != mesh.vertexCount)
                throw new InvalidOperationException("UV0 is missing.");
            var selected = new bool[mesh.vertexCount];
            foreach (var mask in masks.Where(m => m.Enabled))
            {
                if (mask.Texture == null)
                    throw new InvalidOperationException("Assign a mask texture or remove the empty mask entry.");
                if (mask.MaterialSlot < 0 || mask.MaterialSlot >= mesh.subMeshCount)
                    throw new InvalidOperationException("The mask material slot is missing.");
                var image = ReadMask(mask.Texture);
                var sampler = new Coverage(image, mask.Texture.width, mask.Texture.height, mask.Texture.wrapModeU, mask.Texture.wrapModeV);
                var triangles = mesh.GetTriangles(mask.MaterialSlot);
                for (var i = 0; i < triangles.Length; i += 3)
                {
                    var a = triangles[i];
                    var b = triangles[i + 1];
                    var c = triangles[i + 2];
                    if (sampler.Intersects(uv[a], uv[b], uv[c]))
                        selected[a] = selected[b] = selected[c] = true;
                }
            }

            // Preserve coincident split vertices as well, without welding their different attributes.
            var positions = mesh.vertices;
            var protectedPositions = new HashSet<Vector3>();
            for (var i = 0; i < selected.Length; i++)
                if (selected[i])
                    protectedPositions.Add(positions[i]);
            for (var i = 0; i < selected.Length; i++)
                if (protectedPositions.Contains(positions[i]))
                    selected[i] = true;
            return new ProtectedVertexSelection(selected);
        }

        private static Color32[] ReadMask(Texture2D texture)
        {
            if (texture.isReadable)
                return texture.GetPixels32(0);
            var previous = RenderTexture.active;
            var filter = texture.filterMode;
            var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, texture.isDataSRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, false);
            var srgb = GL.sRGBWrite;
            try
            {
                texture.filterMode = FilterMode.Point;
                GL.sRGBWrite = texture.isDataSRGB && QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                copy.Apply();
                return copy.GetPixels32();
            }
            finally
            {
                texture.filterMode = filter;
                GL.sRGBWrite = srgb;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        // A summed-area table rejects empty regions; recursive rectangle intersection catches paint
        // inside triangles without scanning every texel in large UV faces.
        private sealed class Coverage
        {
            private readonly int width, height;
            private readonly int[] sums;
            private readonly TextureWrapMode wrapU, wrapV;
            internal Coverage(Color32[] pixels, int width, int height, TextureWrapMode u, TextureWrapMode v)
            {
                this.width = width;
                this.height = height;
                wrapU = u;
                wrapV = v;
                sums = new int[(width + 1) * (height + 1)];
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                        sums[(y + 1) * (width + 1) + x + 1] = (pixels[y * width + x].r >= 128 ? 1 : 0) + sums[y * (width + 1) + x + 1] + sums[(y + 1) * (width + 1) + x] - sums[y * (width + 1) + x];
            }

            private int Sum(int x0, int y0, int x1, int y1) => sums[y1 * (width + 1) + x1] - sums[y0 * (width + 1) + x1] - sums[y1 * (width + 1) + x0] + sums[y0 * (width + 1) + x0];
            internal bool Intersects(Vector2 a, Vector2 b, Vector2 c)
            {
                if (Sum(0, 0, width, height) == 0)
                    return false;
                var min = Vector2.Min(a, Vector2.Min(b, c));
                var max = Vector2.Max(a, Vector2.Max(b, c));
                if (float.IsNaN(min.x) || float.IsNaN(min.y) || float.IsInfinity(min.x) || float.IsInfinity(min.y) || float.IsInfinity(max.x) || float.IsInfinity(max.y))
                    return true;
                // Clamp outside the tile conservatively covers the projected bounding region.
                if ((wrapU != TextureWrapMode.Repeat && wrapU != TextureWrapMode.Mirror && (min.x < 0 || max.x > 1)) || (wrapV != TextureWrapMode.Repeat && wrapV != TextureWrapMode.Mirror && (min.y < 0 || max.y > 1)))
                    return true;
                if (max.x - min.x > 8 || max.y - min.y > 8 || Mathf.Abs(min.x) > 100000 || Mathf.Abs(min.y) > 100000)
                    return true;
                for (var ty = Mathf.FloorToInt(min.y); ty <= Mathf.FloorToInt(max.y); ty++)
                    for (var tx = Mathf.FloorToInt(min.x); tx <= Mathf.FloorToInt(max.x); tx++)
                    {
                        Vector2 Local(Vector2 p)
                        {
                            p -= new Vector2(tx, ty);
                            if (wrapU == TextureWrapMode.Mirror && (tx & 1) != 0)
                                p.x = 1 - p.x;
                            if (wrapV == TextureWrapMode.Mirror && (ty & 1) != 0)
                                p.y = 1 - p.y;
                            return new Vector2(p.x * width, p.y * height);
                        }

                        if (Hit(Local(a), Local(b), Local(c), 0, 0, width, height))
                            return true;
                    }

                return false;
            }

            private bool Hit(Vector2 a, Vector2 b, Vector2 c, int x0, int y0, int x1, int y1)
            {
                if (Sum(x0, y0, x1, y1) == 0 || !Overlap(a, b, c, x0, y0, x1, y1))
                    return false;
                if ((x1 - x0 == 1 && y1 - y0 == 1) || Sum(x0, y0, x1, y1) == (x1 - x0) * (y1 - y0))
                    return true;
                if (x1 - x0 >= y1 - y0)
                {
                    var mid = (x0 + x1) / 2;
                    return Hit(a, b, c, x0, y0, mid, y1) || Hit(a, b, c, mid, y0, x1, y1);
                }
                else
                {
                    var mid = (y0 + y1) / 2;
                    return Hit(a, b, c, x0, y0, x1, mid) || Hit(a, b, c, x0, mid, x1, y1);
                }
            }

            private static bool Overlap(Vector2 a, Vector2 b, Vector2 c, float x0, float y0, float x1, float y1)
            {
                if (Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < x0 || Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > x1 || Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < y0 || Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > y1)
                    return false;
                return Axis(a, b, c, b - a, x0, y0, x1, y1) && Axis(a, b, c, c - b, x0, y0, x1, y1) && Axis(a, b, c, a - c, x0, y0, x1, y1);
            }

            private static bool Axis(Vector2 a, Vector2 b, Vector2 c, Vector2 edge, float x0, float y0, float x1, float y1)
            {
                var n = new Vector2(-edge.y, edge.x);
                var center = new Vector2((x0 + x1) * .5f, (y0 + y1) * .5f);
                var radius = Mathf.Abs(n.x) * (x1 - x0) * .5f + Mathf.Abs(n.y) * (y1 - y0) * .5f;
                var d = Vector2.Dot(center, n);
                var da = Vector2.Dot(a, n);
                var db = Vector2.Dot(b, n);
                var dc = Vector2.Dot(c, n);
                return Mathf.Max(da, Mathf.Max(db, dc)) >= d - radius - 1e-5f && Mathf.Min(da, Mathf.Min(db, dc)) <= d + radius + 1e-5f;
            }
        }
    }
}
