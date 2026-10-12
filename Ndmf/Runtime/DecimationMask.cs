#nullable enable
using System;
using UnityEngine;

namespace Meshia.MeshSimplification.Ndmf
{
    /// <summary>A saved UV0 texture selecting geometry to keep unchanged. White protects; black allows reduction.</summary>
    [Serializable]
    public sealed class DecimationMask
    {
        public bool Enabled = true;
        [Min(0)]
        public int MaterialSlot;
        public Texture2D? Texture;
        [HideInInspector]
        public Mesh? SourceMesh;
    }
}
