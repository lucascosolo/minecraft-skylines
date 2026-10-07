using System;

namespace Skylines.Core.Geometry
{
    /// <summary>
    /// A vehicle mesh's height along its length: equal slices along mesh z, each the highest point of the mesh inside it,
    /// so a collision shape can follow the hood, windshield, roof and trailer bed instead of one box.
    /// </summary>
    public static class VehicleProfile
    {
        public const float SliceLength = 0.25f;
        public const int MaxSlices = 128;

        public static int SliceCount(float length)
        {
            if (!(length > 0)) return 1;
            int n = (int)Math.Ceiling(length / SliceLength - 1e-4);
            return Math.Max(1, Math.Min(MaxSlices, n));
        }

        /// <summary>Per slice of [minZ, maxZ] the highest y of any triangle clipped to it, above <paramref name="minY"/>; 0 where none reaches.</summary>
        public static float[] Heights(float[] xyz, int[] indices, float minZ, float maxZ, float minY, int slices)
        {
            var h = new float[slices];
            float step = (maxZ - minZ) / slices;
            if (!(step > 0)) return h;
            for (int t = 0; t + 2 < indices.Length; t += 3)
            {
                int i0 = indices[t] * 3, i1 = indices[t + 1] * 3, i2 = indices[t + 2] * 3;
                float lo = Math.Min(xyz[i0 + 2], Math.Min(xyz[i1 + 2], xyz[i2 + 2]));
                float hi = Math.Max(xyz[i0 + 2], Math.Max(xyz[i1 + 2], xyz[i2 + 2]));
                int s0 = Math.Max(0, (int)Math.Floor((lo - minZ) / step - 1e-4));
                int s1 = Math.Min(slices - 1, (int)Math.Floor((hi - minZ) / step + 1e-4));
                for (int s = s0; s <= s1; s++)
                {
                    float a = minZ + s * step, b = a + step;
                    float top = Math.Max(Clip(xyz, i0, i1, a, b), Math.Max(Clip(xyz, i1, i2, a, b), Clip(xyz, i2, i0, a, b)));
                    if (top - minY > h[s]) h[s] = top - minY;
                }
            }
            return h;
        }

        // Highest y of the edge p..q inside z in [a, b]; -inf when it misses the interval.
        private static float Clip(float[] xyz, int p, int q, float a, float b)
        {
            const float eps = 1e-5f;
            float zp = xyz[p + 2], zq = xyz[q + 2], yp = xyz[p + 1], yq = xyz[q + 1];
            if (zp > zq)
            {
                float t = zp; zp = zq; zq = t;
                t = yp; yp = yq; yq = t;
            }
            if (zq < a - eps || zp > b + eps) return float.NegativeInfinity;
            if (zq - zp < eps) return Math.Max(yp, yq);
            float za = Math.Max(zp, a), zb = Math.Min(zq, b);
            return Math.Max(yp + (yq - yp) * (za - zp) / (zq - zp), yp + (yq - yp) * (zb - zp) / (zq - zp));
        }

        /// <summary>Heights as fractions of <paramref name="fullHeight"/> in 1/255, rounded up so the shape never shrinks.</summary>
        public static byte[] Quantize(float[] heights, float fullHeight)
        {
            var q = new byte[heights.Length];
            if (!(fullHeight > 0)) return q;
            for (int i = 0; i < q.Length; i++)
                q[i] = (byte)Math.Max(0.0, Math.Min(255.0, Math.Ceiling(heights[i] / fullHeight * 255.0 - 1e-4)));
            return q;
        }
    }
}
