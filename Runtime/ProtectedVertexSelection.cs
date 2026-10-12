#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Meshia.MeshSimplification
{
    /// <summary>Immutable source-index selection. Selected vertices cannot be changed by FA-QEM.</summary>
    public sealed class ProtectedVertexSelection : IEquatable<ProtectedVertexSelection>
    {
        private readonly bool[] vertices;
        private readonly int hash;
        /// <summary>Number of source vertices addressed by this selection.</summary>
        public int Length => vertices.Length;
        /// <summary>Number of explicitly protected source vertices.</summary>
        public int Count { get; }

        /// <summary>Whether the source vertex at this index is protected.</summary>
        public bool this[int index] => vertices[index];
        /// <summary>Copies the input selection so later caller edits cannot affect pending work.</summary>
        public ProtectedVertexSelection(IEnumerable<bool> vertices)
        {
            this.vertices = vertices.ToArray();
            unchecked
            {
                hash = 17;
                foreach (var selected in this.vertices)
                {
                    hash = hash * 31 + (selected ? 1 : 0);
                    if (selected)
                        Count++;
                }
            }
        }

        public bool Equals(ProtectedVertexSelection? other) => ReferenceEquals(this, other) || other != null && hash == other.hash && vertices.SequenceEqual(other.vertices);
        public override bool Equals(object? other) => other is ProtectedVertexSelection selection && Equals(selection);
        public override int GetHashCode() => hash;
        internal void Validate(MeshSimplificationTarget target, int vertexCount)
        {
            if (Length != vertexCount)
                throw new ArgumentException("Protection mask does not match the source mesh.");
            if (Count > 0 && target.Kind != MeshSimplificationTargetKind.FaQemTriangleCount)
                throw new ArgumentException("Protected areas require FA-QEM.");
        }
    }
}
