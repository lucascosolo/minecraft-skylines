using System;
using System.Collections.Generic;

namespace Skylines.Core.Streaming
{
    /// <summary>Square regions of a fixed size on the x, z plane; region (0, 0) covers [0, size).</summary>
    public sealed class RegionGrid
    {
        private readonly float _size;

        /// <summary>Creates a grid of square regions of <paramref name="size"/> units.</summary>
        public RegionGrid(float size)
        {
            if (!(size > 0)) throw new ArgumentOutOfRangeException("size");
            _size = size;
        }

        /// <summary>The region containing the point (floor division).</summary>
        public void RegionOf(float x, float z, out int rx, out int rz)
        {
            rx = (int)Math.Floor(x / _size);
            rz = (int)Math.Floor(z / _size);
        }

        /// <summary>The square covered by a region.</summary>
        public void Bounds(int rx, int rz, out float minX, out float minZ, out float maxX, out float maxZ)
        {
            minX = rx * _size; minZ = rz * _size;
            maxX = minX + _size; maxZ = minZ + _size;
        }

        /// <summary>Distance from a point to the region's square (0 inside).</summary>
        internal double DistanceTo(int rx, int rz, float x, float z)
        {
            float a, b, c, d;
            Bounds(rx, rz, out a, out b, out c, out d);
            double dx = Math.Max(Math.Max(a - x, 0), x - c), dz = Math.Max(Math.Max(b - z, 0), z - d);
            return Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Keys of every region whose square is within <paramref name="radius"/> of the point, nearest first, ties by key.</summary>
        public List<long> RegionsWithin(float x, float z, float radius)
        {
            int r0x, r0z, r1x, r1z;
            RegionOf(x - radius, z - radius, out r0x, out r0z);
            RegionOf(x + radius, z + radius, out r1x, out r1z);
            var found = new List<KeyValuePair<double, long>>();
            for (int rx = r0x; rx <= r1x; rx++)
            {
                for (int rz = r0z; rz <= r1z; rz++)
                {
                    double d = DistanceTo(rx, rz, x, z);
                    if (d <= radius) found.Add(new KeyValuePair<double, long>(d, Key(rx, rz)));
                }
            }
            found.Sort(delegate (KeyValuePair<double, long> a, KeyValuePair<double, long> b)
            {
                int c = a.Key.CompareTo(b.Key);
                return c != 0 ? c : a.Value.CompareTo(b.Value);
            });
            var keys = new List<long>(found.Count);
            foreach (var kv in found) keys.Add(kv.Value);
            return keys;
        }

        /// <summary>Packs a region coordinate into one key.</summary>
        public static long Key(int rx, int rz)
        {
            return ((long)rx << 32) | (uint)rz;
        }

        /// <summary>Inverse of <see cref="Key"/>.</summary>
        public static void Unkey(long key, out int rx, out int rz)
        {
            rx = (int)(key >> 32);
            rz = (int)(key & 0xFFFFFFFFL);
        }
    }
}
