using System;

namespace Skylines.Core.Geometry
{
    /// <summary>Caps how many triangles a range of a <see cref="TriangleBuffer"/> may hold, dropping the smallest first.</summary>
    public static class TriangleBudget
    {
        /// <summary>
        /// Keeps the <paramref name="max"/> largest-area triangles of [start, Count) (ties: the earlier), in their original order;
        /// returns how many were dropped.
        /// </summary>
        public static int KeepLargest(TriangleBuffer into, int start, int max)
        {
            if (start < 0 || start > into.Count) throw new ArgumentOutOfRangeException("start");
            if (max < 0) throw new ArgumentOutOfRangeException("max");
            int n = into.Count - start;
            if (n <= max) return 0;
            float[] p = into.Positions;
            ushort[] f = into.Flags;
            var area = new float[n];
            var order = new int[n];
            for (int i = 0; i < n; i++)
            {
                area[i] = Area(p, 9 * (start + i));
                order[i] = i;
            }
            Array.Sort(order, (x, y) => area[x] != area[y] ? area[y].CompareTo(area[x]) : x.CompareTo(y));
            Array.Sort(order, 0, max);
            for (int k = 0; k < max; k++)
            {
                int from = start + order[k], to = start + k;
                if (from == to) continue;
                Array.Copy(p, 9 * from, p, 9 * to, 9);
                f[to] = f[from];
            }
            into.Truncate(start + max);
            return n - max;
        }

        private static float Area(float[] p, int o)
        {
            float ux = p[o + 3] - p[o], uy = p[o + 4] - p[o + 1], uz = p[o + 5] - p[o + 2];
            float vx = p[o + 6] - p[o], vy = p[o + 7] - p[o + 1], vz = p[o + 8] - p[o + 2];
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            return 0.5f * (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
        }
    }
}
