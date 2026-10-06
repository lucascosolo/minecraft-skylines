namespace Skylines.Core.Geometry
{
    /// <summary>Clips triangles to an axis-aligned xz rectangle (all heights), so no piece reaches outside a collision region.</summary>
    public static class RectClip
    {
        private const float MinCross = 2e-6f; // |(b-a)x(c-a)| below this (area 1e-6 m^2) is dropped

        /// <summary>
        /// Appends the part of triangle (a, b, c) with minX &lt;= x &lt;= maxX and minZ &lt;= z &lt;= maxZ, fan-triangulated with the
        /// source winding; a triangle wholly inside is appended unchanged. Returns the number of triangles appended.
        /// </summary>
        public static int Triangle(float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz, ushort flags, float minX, float minZ, float maxX, float maxZ, TriangleBuffer into)
        {
            if (Inside(ax, az, minX, minZ, maxX, maxZ) && Inside(bx, bz, minX, minZ, maxX, maxZ) && Inside(cx, cz, minX, minZ, maxX, maxZ))
            {
                into.Add(ax, ay, az, bx, by, bz, cx, cy, cz, flags);
                return 1;
            }
            var poly = new float[3 * 8];
            var next = new float[3 * 8];
            poly[0] = ax; poly[1] = ay; poly[2] = az;
            poly[3] = bx; poly[4] = by; poly[5] = bz;
            poly[6] = cx; poly[7] = cy; poly[8] = cz;
            int n = 3;
            n = ClipPlane(poly, n, next, 0, minX, 1f); Swap(ref poly, ref next);
            n = ClipPlane(poly, n, next, 0, maxX, -1f); Swap(ref poly, ref next);
            n = ClipPlane(poly, n, next, 2, minZ, 1f); Swap(ref poly, ref next);
            n = ClipPlane(poly, n, next, 2, maxZ, -1f); Swap(ref poly, ref next);
            int added = 0;
            for (int i = 1; i + 1 < n; i++)
            {
                int j = 3 * i, k = 3 * (i + 1);
                if (Cross(poly, 0, j, k) <= MinCross) continue;
                into.Add(poly[0], poly[1], poly[2], poly[j], poly[j + 1], poly[j + 2], poly[k], poly[k + 1], poly[k + 2], flags);
                added++;
            }
            return added;
        }

        private static bool Inside(float x, float z, float minX, float minZ, float maxX, float maxZ)
        {
            return x >= minX && x <= maxX && z >= minZ && z <= maxZ;
        }

        private static void Swap(ref float[] a, ref float[] b)
        {
            float[] t = a; a = b; b = t;
        }

        // Sutherland-Hodgman against the half-space sign * (p[axis] - bound) >= 0.
        private static int ClipPlane(float[] src, int n, float[] dst, int axis, float bound, float sign)
        {
            int m = 0;
            for (int i = 0; i < n; i++)
            {
                int p = 3 * i, q = 3 * ((i + 1) % n);
                float dp = sign * (src[p + axis] - bound), dq = sign * (src[q + axis] - bound);
                if (dp >= 0f)
                {
                    dst[3 * m] = src[p]; dst[3 * m + 1] = src[p + 1]; dst[3 * m + 2] = src[p + 2];
                    m++;
                }
                if ((dp >= 0f) != (dq >= 0f))
                {
                    float t = dp / (dp - dq);
                    for (int c = 0; c < 3; c++) dst[3 * m + c] = src[p + c] + t * (src[q + c] - src[p + c]);
                    dst[3 * m + axis] = bound;
                    m++;
                }
            }
            return m;
        }

        private static float Cross(float[] v, int a, int b, int c)
        {
            float ux = v[b] - v[a], uy = v[b + 1] - v[a + 1], uz = v[b + 2] - v[a + 2];
            float wx = v[c] - v[a], wy = v[c + 1] - v[a + 1], wz = v[c + 2] - v[a + 2];
            float nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
            return (float)System.Math.Sqrt(nx * nx + ny * ny + nz * nz);
        }
    }
}
