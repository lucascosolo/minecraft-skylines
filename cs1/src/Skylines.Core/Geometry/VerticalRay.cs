using System;

namespace Skylines.Core.Geometry
{
    /// <summary>A vertical line (x, z fixed, y up) against a triangle list, for picking a surface to stand on.</summary>
    public static class VerticalRay
    {
        /// <summary>
        /// Where the vertical line through (x, z) meets triangle <paramref name="index"/>: its height <paramref name="y"/>
        /// and the y of its unit normal <paramref name="ny"/> ((b - a) x (c - a), normalised; up-facing terrain is +1).
        /// False when (x, z) is outside the triangle's xz projection (edges count as inside, within 1e-5) or the triangle
        /// is vertical.
        /// </summary>
        public static bool Hit(TriangleBuffer tris, int index, float x, float z, out float y, out float ny)
        {
            const float Eps = 1e-5f;
            y = ny = 0f;
            float[] p = tris.Positions;
            int o = 9 * index;
            float ax = p[o], ay = p[o + 1], az = p[o + 2];
            float ux = p[o + 3] - ax, uy = p[o + 4] - ay, uz = p[o + 5] - az;
            float vx = p[o + 6] - ax, vy = p[o + 7] - ay, vz = p[o + 8] - az;
            float det = uz * vx - ux * vz; // normal y before normalising
            if (Math.Abs(det) < 1e-9f) return false;
            float qx = x - ax, qz = z - az;
            float s = (qz * vx - qx * vz) / det, t = (uz * qx - ux * qz) / det;
            if (s < -Eps || t < -Eps || s + t > 1 + Eps) return false;
            y = ay + s * uy + t * vy;
            float nx = uy * vz - uz * vy, nz = ux * vy - uy * vx;
            ny = det / (float)Math.Sqrt(nx * nx + det * det + nz * nz);
            return true;
        }

        /// <summary>
        /// The surface to stand on at (x, z): the highest hit at or below <paramref name="fromY"/> whose normal y is at least
        /// <paramref name="minNy"/> and that is not inside a solid. Solids are closed meshes with outward normals: a point is
        /// inside one when, among triangles whose flags share a bit with <paramref name="solidFlags"/>, the lowest hit more
        /// than 0.01 above it faces up (ny &gt; 0, its back seen from inside).
        /// When no hit at or below <paramref name="fromY"/> qualifies, the lowest qualifying hit above it is used (a roof
        /// taller than the probe start). False when nothing qualifies.
        /// </summary>
        public static bool HighestWalkable(TriangleBuffer tris, float x, float z, float fromY, float minNy, ushort solidFlags, out float y)
        {
            float below = float.NegativeInfinity, above = float.PositiveInfinity;
            for (int i = 0; i < tris.Count; i++)
            {
                float hy, ny;
                if (!Hit(tris, i, x, z, out hy, out ny) || ny < minNy || InsideSolid(tris, x, z, hy, solidFlags)) continue;
                if (hy <= fromY) below = Math.Max(below, hy);
                else above = Math.Min(above, hy);
            }
            y = below > float.NegativeInfinity ? below : above;
            return !float.IsInfinity(y);
        }

        private static bool InsideSolid(TriangleBuffer tris, float x, float z, float h, ushort solidFlags)
        {
            float lowest = float.PositiveInfinity, lowestNy = 0f;
            for (int i = 0; i < tris.Count; i++)
            {
                float hy, ny;
                if ((tris.Flags[i] & solidFlags) == 0 || !Hit(tris, i, x, z, out hy, out ny) || hy <= h + 0.01f || hy >= lowest) continue;
                lowest = hy;
                lowestNy = ny;
            }
            return lowestNy > 0f;
        }
    }
}
