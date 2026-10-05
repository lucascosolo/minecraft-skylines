using System;

namespace Skylines.Core.Geometry
{
    /// <summary>A road ribbon along a bezier.</summary>
    public static class Strip
    {
        private const int LengthSamples = 16;

        /// <summary>
        /// Adds a ribbon of half-width <paramref name="halfWidth"/> along the curve, perpendicular to the horizontal tangent,
        /// with n = max(1, ceil(length / step)) intervals (length measured on a 16-segment polyline). The top surface faces
        /// up; a positive <paramref name="thickness"/> adds a bottom face and two outward walls. A zero-length curve adds nothing.
        /// </summary>
        public static void Road(Bezier3D c, float halfWidth, float step, float thickness, ushort flags, TriangleBuffer into)
        {
            if (!(step > 0)) throw new ArgumentOutOfRangeException("step");
            double len = 0, px = c.Ax, py = c.Ay, pz = c.Az;
            for (int i = 1; i <= LengthSamples; i++)
            {
                double qx, qy, qz;
                At(c, i / (double)LengthSamples, out qx, out qy, out qz);
                len += Math.Sqrt((qx - px) * (qx - px) + (qy - py) * (qy - py) + (qz - pz) * (qz - pz));
                px = qx; py = qy; pz = qz;
            }
            if (len < 1e-4) return;

            int n = Math.Max(1, (int)Math.Ceiling(len / step));
            var lx = new float[n + 1]; var ly = new float[n + 1]; var lz = new float[n + 1];
            var rx = new float[n + 1]; var rz = new float[n + 1];
            double lastTx = 1, lastTz = 0;
            for (int k = 0; k <= n; k++)
            {
                double t = k / (double)n, x, y, z, tx, tz;
                At(c, t, out x, out y, out z);
                Tangent(c, t, out tx, out tz);
                double tl = Math.Sqrt(tx * tx + tz * tz);
                if (tl < 1e-6) { tx = lastTx; tz = lastTz; }
                else { tx /= tl; tz /= tl; lastTx = tx; lastTz = tz; }
                lx[k] = (float)(x - tz * halfWidth); lz[k] = (float)(z + tx * halfWidth);
                rx[k] = (float)(x + tz * halfWidth); rz[k] = (float)(z - tx * halfWidth);
                ly[k] = (float)y;
            }

            for (int k = 0; k < n; k++)
            {
                int j = k + 1;
                into.Add(rx[k], ly[k], rz[k], lx[k], ly[k], lz[k], lx[j], ly[j], lz[j], flags);
                into.Add(rx[k], ly[k], rz[k], lx[j], ly[j], lz[j], rx[j], ly[j], rz[j], flags);
                if (!(thickness > 0)) continue;
                float bk = ly[k] - thickness, bj = ly[j] - thickness;
                into.Add(rx[k], bk, rz[k], lx[j], bj, lz[j], lx[k], bk, lz[k], flags);
                into.Add(rx[k], bk, rz[k], rx[j], bj, rz[j], lx[j], bj, lz[j], flags);
                into.Add(lx[k], ly[k], lz[k], lx[k], bk, lz[k], lx[j], ly[j], lz[j], flags);
                into.Add(lx[k], bk, lz[k], lx[j], bj, lz[j], lx[j], ly[j], lz[j], flags);
                into.Add(rx[k], ly[k], rz[k], rx[j], ly[j], rz[j], rx[k], bk, rz[k], flags);
                into.Add(rx[k], bk, rz[k], rx[j], ly[j], rz[j], rx[j], bj, rz[j], flags);
            }
        }

        private static void At(Bezier3D c, double t, out double x, out double y, out double z)
        {
            double u = 1 - t, a = u * u * u, b = 3 * u * u * t, d = 3 * u * t * t, e = t * t * t;
            x = a * c.Ax + b * c.Bx + d * c.Cx + e * c.Dx;
            y = a * c.Ay + b * c.By + d * c.Cy + e * c.Dy;
            z = a * c.Az + b * c.Bz + d * c.Cz + e * c.Dz;
        }

        private static void Tangent(Bezier3D c, double t, out double x, out double z)
        {
            double u = 1 - t, a = 3 * u * u, b = 6 * u * t, d = 3 * t * t;
            x = a * (c.Bx - c.Ax) + b * (c.Cx - c.Bx) + d * (c.Dx - c.Cx);
            z = a * (c.Bz - c.Az) + b * (c.Cz - c.Bz) + d * (c.Dz - c.Cz);
        }
    }
}
