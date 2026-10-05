using System;

namespace Skylines.Core.Geometry
{
    /// <summary>Triangulates a height function over a rectangle (x, z plane, y up).</summary>
    public static class Heightfield
    {
        /// <summary>
        /// Samples <paramref name="sampleHeight"/> once per grid vertex (x = minX + i * step, last column and row clamped to
        /// max) and adds two up-facing triangles per cell, split along the diagonal from the low corner to the high corner.
        /// </summary>
        public static void Triangulate(Func<float, float, float> sampleHeight, float minX, float minZ, float maxX, float maxZ, float step, ushort flags, TriangleBuffer into)
        {
            if (!(step > 0)) throw new ArgumentOutOfRangeException("step");
            if (maxX < minX) throw new ArgumentOutOfRangeException("maxX");
            if (maxZ < minZ) throw new ArgumentOutOfRangeException("maxZ");
            int nx = (int)Math.Ceiling((maxX - minX) / step);
            int nz = (int)Math.Ceiling((maxZ - minZ) / step);
            if (nx == 0 || nz == 0) return;

            var xs = new float[nx + 1];
            var zs = new float[nz + 1];
            for (int i = 0; i <= nx; i++) xs[i] = i == nx ? maxX : minX + i * step;
            for (int j = 0; j <= nz; j++) zs[j] = j == nz ? maxZ : minZ + j * step;

            var ys = new float[(nx + 1) * (nz + 1)];
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                    ys[i * (nz + 1) + j] = sampleHeight(xs[i], zs[j]);

            for (int i = 0; i < nx; i++)
            {
                for (int j = 0; j < nz; j++)
                {
                    float x0 = xs[i], x1 = xs[i + 1], z0 = zs[j], z1 = zs[j + 1];
                    float y00 = ys[i * (nz + 1) + j], y01 = ys[i * (nz + 1) + j + 1];
                    float y10 = ys[(i + 1) * (nz + 1) + j], y11 = ys[(i + 1) * (nz + 1) + j + 1];
                    into.Add(x0, y00, z0, x0, y01, z1, x1, y11, z1, flags);
                    into.Add(x0, y00, z0, x1, y11, z1, x1, y10, z0, flags);
                }
            }
        }
    }
}
