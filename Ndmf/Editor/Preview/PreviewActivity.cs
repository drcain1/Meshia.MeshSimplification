#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Meshia.MeshSimplification.Ndmf.Editor.Preview
{
    // Track the request until its output is actually displayed, not just until
    // triangle measurement finishes. Older nodes cannot clear a newer request.
    internal static class PreviewActivity
    {
        private static long serial;
        private static readonly Dictionary<Renderer, long> Pending = new();

        internal static long Begin(IEnumerable<Renderer> renderers)
        {
            var request = ++serial;
            foreach (var renderer in renderers) Pending[renderer] = request;
            return request;
        }

        internal static bool IsPending(Renderer renderer) => renderer != null && Pending.ContainsKey(renderer);

        internal static void Finish(IEnumerable<Renderer> renderers, long request)
        {
            foreach (var renderer in renderers)
                if (Pending.TryGetValue(renderer, out var current) && current == request)
                    Pending.Remove(renderer);
        }
    }
}
