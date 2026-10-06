using System;
using Unity.Collections;

namespace Meshia.MeshSimplification
{
    /// <summary>Compact, value-owned source-index ranges for build-only overlap protection.</summary>
    public struct SourceVertexRanges : IEquatable<SourceVertexRanges>
    {
        // Common short ranges use one word. Large indices/lengths use two;
        // no vertex-count limit or borrowed native allocation is introduced.
        private FixedList512Bytes<uint> first, second;
        private int count;
        public readonly int Length => count * 2;
        private readonly uint Word(int i) => i < first.Length ? first[i] : second[i - first.Length];
        public readonly int this[int index]
        {
            get
            {
                if (index < 0 || index >= Length) throw new IndexOutOfRangeException();
                for (int r = 0, offset = 0; ; r++)
                {
                    var word = Word(offset++);
                    int start, length;
                    if ((word & 1) == 0) { start = (int)(word >> 17); length = (int)((word >> 1) & 65535); }
                    else { start = (int)(word >> 1); length = (int)Word(offset++); }
                    if (r == index / 2) return index % 2 == 0 ? start : start + length;
                }
            }
        }
        public void Clear() { first.Clear(); second.Clear(); count = 0; }
        public bool TryAdd(int start, int end)
        {
            if (start < 0 || end <= start) throw new ArgumentOutOfRangeException(nameof(start));
            var packed = start <= 32767 && end - start <= 65535;
            if (first.Length + second.Length + (packed ? 1 : 2) > first.Capacity + second.Capacity) return false;
            if (packed) AddWord(((uint)start << 17) | ((uint)(end - start) << 1));
            else { AddWord(((uint)start << 1) | 1); AddWord((uint)(end - start)); }
            count++; return true;
        }
        private void AddWord(uint value) { if (first.Length < first.Capacity) first.Add(value); else second.Add(value); }
        public readonly bool Equals(SourceVertexRanges other) => count == other.count && first.Equals(other.first) && second.Equals(other.second);
        public override readonly bool Equals(object obj) => obj is SourceVertexRanges other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(first, second);
    }
}
