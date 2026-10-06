using System;
using System.Collections.Generic;

namespace Skylines.Core.Voxels
{
    /// <summary>An integer block position.</summary>
    public struct VoxelPos : IEquatable<VoxelPos>, IComparable<VoxelPos>
    {
        /// <summary>Coordinates.</summary>
        public readonly int X, Y, Z;

        /// <summary>Creates a position.</summary>
        public VoxelPos(int x, int y, int z)
        {
            X = x; Y = y; Z = z;
        }

        /// <summary>Orders by x, then z, then y (the record's order).</summary>
        public int CompareTo(VoxelPos o)
        {
            if (X != o.X) return X < o.X ? -1 : 1;
            if (Z != o.Z) return Z < o.Z ? -1 : 1;
            if (Y != o.Y) return Y < o.Y ? -1 : 1;
            return 0;
        }

        /// <inheritdoc />
        public bool Equals(VoxelPos o) { return X == o.X && Y == o.Y && Z == o.Z; }

        /// <inheritdoc />
        public override bool Equals(object obj) { return obj is VoxelPos && Equals((VoxelPos)obj); }

        /// <inheritdoc />
        public override int GetHashCode() { return unchecked((X * 73856093) ^ (Y * 19349663) ^ (Z * 83492791)); }

        /// <inheritdoc />
        public override string ToString() { return X + " " + Y + " " + Z; }
    }

    /// <summary>One position and its block state.</summary>
    public struct VoxelEdit
    {
        /// <summary>Where.</summary>
        public readonly VoxelPos Pos;
        /// <summary>The opaque state string (never <see cref="VoxelEditSet.Air"/> inside a set).</summary>
        public readonly string State;

        /// <summary>Creates an edit.</summary>
        public VoxelEdit(VoxelPos pos, string state)
        {
            Pos = pos; State = state;
        }
    }

    /// <summary>A batch of edits with its own palette, as BLOCK_EDITS carries them.</summary>
    public sealed class VoxelBatch
    {
        /// <summary>Distinct states used in this batch.</summary>
        public readonly List<string> Palette = new List<string>();
        /// <summary>Positions, in the order they were batched.</summary>
        public readonly List<VoxelPos> Positions = new List<VoxelPos>();
        /// <summary>Palette index per position.</summary>
        public readonly List<ushort> States = new List<ushort>();
    }

    /// <summary>
    /// Every block a player changed in one city: position to block state. Setting <see cref="Air"/> removes the
    /// position (back to the world's base). Not thread-safe; callers lock.
    /// </summary>
    public sealed class VoxelEditSet
    {
        /// <summary>The state that means "no edit here".</summary>
        public const string Air = "minecraft:air";
        /// <summary>Most edits in one BLOCK_EDITS batch.</summary>
        public const int MaxBatchEdits = 65536;

        private readonly Dictionary<VoxelPos, string> _edits = new Dictionary<VoxelPos, string>();

        /// <summary>Number of positions with an edit.</summary>
        public int Count { get { return _edits.Count; } }

        /// <summary>Records a state at a position; <see cref="Air"/> removes it.</summary>
        public void Set(int x, int y, int z, string state)
        {
            if (state == null) throw new ArgumentNullException("state");
            var p = new VoxelPos(x, y, z);
            if (state == Air) _edits.Remove(p);
            else _edits[p] = state;
        }

        /// <summary>The state at a position, if it has an edit.</summary>
        public bool TryGet(int x, int y, int z, out string state)
        {
            return _edits.TryGetValue(new VoxelPos(x, y, z), out state);
        }

        /// <summary>Removes every edit.</summary>
        public void Clear()
        {
            _edits.Clear();
        }

        /// <summary>All edits sorted by (x, z, y).</summary>
        public List<VoxelEdit> Sorted()
        {
            var list = new List<VoxelEdit>(_edits.Count);
            foreach (KeyValuePair<VoxelPos, string> e in _edits) list.Add(new VoxelEdit(e.Key, e.Value));
            list.Sort((a, b) => a.Pos.CompareTo(b.Pos));
            return list;
        }

        /// <summary>
        /// Splits edits, in order, into batches of at most <paramref name="maxPerBatch"/> edits and at most 65535
        /// palette entries. No edits gives no batches.
        /// </summary>
        public static List<VoxelBatch> Batches(IList<VoxelEdit> edits, int maxPerBatch)
        {
            if (maxPerBatch < 1 || maxPerBatch > MaxBatchEdits) throw new ArgumentOutOfRangeException("maxPerBatch");
            var batches = new List<VoxelBatch>();
            VoxelBatch b = null;
            var index = new Dictionary<string, ushort>();
            foreach (VoxelEdit e in edits)
            {
                ushort i;
                bool known = b != null && index.TryGetValue(e.State, out i);
                if (b == null || b.Positions.Count == maxPerBatch || (!known && b.Palette.Count == ushort.MaxValue))
                {
                    b = new VoxelBatch();
                    batches.Add(b);
                    index.Clear();
                }
                if (!index.TryGetValue(e.State, out i))
                {
                    i = (ushort)b.Palette.Count;
                    index[e.State] = i;
                    b.Palette.Add(e.State);
                }
                b.Positions.Add(e.Pos);
                b.States.Add(i);
            }
            return batches;
        }
    }
}
