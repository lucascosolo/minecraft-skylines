using System;

namespace Skylines.Core.Geometry
{
    /// <summary>Closed boxes, used for buildings.</summary>
    public static class Box
    {
        /// <summary>
        /// Adds the 12 outward-facing triangles of a box centred at (cx, cz) between <paramref name="yBottom"/> and
        /// <paramref name="yTop"/>. Its local x axis is (cos angle, 0, sin angle), its local z axis (-sin angle, 0, cos angle).
        /// </summary>
        public static void Oriented(float cx, float cz, float angle, float halfX, float halfZ, float yBottom, float yTop, ushort flags, TriangleBuffer into)
        {
            float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
            float xx = c * halfX, xz = s * halfX, zx = -s * halfZ, zz = c * halfZ;
            // Corners counter-clockwise seen from above in local (sx, sz): (-,-) (-,+) (+,+) (+,-) after the handedness check.
            var px = new float[4];
            var pz = new float[4];
            int[] sx = { -1, -1, 1, 1 }, sz = { -1, 1, 1, -1 };
            for (int i = 0; i < 4; i++)
            {
                px[i] = cx + sx[i] * xx + sz[i] * zx;
                pz[i] = cz + sx[i] * xz + sz[i] * zz;
            }
            // Triangle (0, 1, 2) faces up when its normal's y = (z1 - z0)(x2 - x0) - (x1 - x0)(z2 - z0) is positive.
            if ((pz[1] - pz[0]) * (px[2] - px[0]) - (px[1] - px[0]) * (pz[2] - pz[0]) < 0)
            {
                Array.Reverse(px);
                Array.Reverse(pz);
            }
            into.Add(px[0], yTop, pz[0], px[1], yTop, pz[1], px[2], yTop, pz[2], flags);
            into.Add(px[0], yTop, pz[0], px[2], yTop, pz[2], px[3], yTop, pz[3], flags);
            into.Add(px[0], yBottom, pz[0], px[2], yBottom, pz[2], px[1], yBottom, pz[1], flags);
            into.Add(px[0], yBottom, pz[0], px[3], yBottom, pz[3], px[2], yBottom, pz[2], flags);
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                into.Add(px[i], yTop, pz[i], px[i], yBottom, pz[i], px[j], yTop, pz[j], flags);
                into.Add(px[i], yBottom, pz[i], px[j], yBottom, pz[j], px[j], yTop, pz[j], flags);
            }
        }
    }
}
