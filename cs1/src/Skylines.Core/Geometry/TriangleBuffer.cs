using System;

namespace Skylines.Core.Geometry
{
    /// <summary>Growable triangle list: nine floats (a, b, c) and one flags value per triangle.</summary>
    public sealed class TriangleBuffer
    {
        private float[] _positions = new float[9 * 64];
        private ushort[] _flags = new ushort[64];

        /// <summary>Number of triangles.</summary>
        public int Count { get; private set; }

        /// <summary>Backing array, nine floats per triangle; only the first <c>9 * Count</c> are valid.</summary>
        public float[] Positions { get { return _positions; } }

        /// <summary>Backing array, one value per triangle; only the first <c>Count</c> are valid.</summary>
        public ushort[] Flags { get { return _flags; } }

        /// <summary>Appends a triangle.</summary>
        public void Add(float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz, ushort flags)
        {
            if (Count == _flags.Length)
            {
                Array.Resize(ref _flags, _flags.Length * 2);
                Array.Resize(ref _positions, _positions.Length * 2);
            }
            int o = Count * 9;
            float[] p = _positions;
            p[o] = ax; p[o + 1] = ay; p[o + 2] = az;
            p[o + 3] = bx; p[o + 4] = by; p[o + 5] = bz;
            p[o + 6] = cx; p[o + 7] = cy; p[o + 8] = cz;
            _flags[Count++] = flags;
        }

        /// <summary>Removes all triangles, keeping the capacity.</summary>
        public void Clear()
        {
            Count = 0;
        }

        /// <summary>Swaps b and c of every triangle, which flips its normal.</summary>
        public void ReverseWinding()
        {
            for (int i = 0; i < Count; i++)
            {
                int o = i * 9;
                for (int k = 0; k < 3; k++)
                {
                    float t = _positions[o + 3 + k];
                    _positions[o + 3 + k] = _positions[o + 6 + k];
                    _positions[o + 6 + k] = t;
                }
            }
        }
    }
}
