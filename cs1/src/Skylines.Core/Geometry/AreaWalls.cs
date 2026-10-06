using System;

namespace Skylines.Core.Geometry
{
    /// <summary>
    /// Thin vertical walls along the borders between square tiles where one side is playable and the other is not, so a
    /// first-person player cannot leave the playable area. Tile borders lie at <c>origin + k * size</c> on both axes.
    /// </summary>
    public static class AreaWalls
    {
        /// <summary>Wall thickness, metres, centred on the border.</summary>
        public const float Thickness = 0.2f;
        /// <summary>How far across a border ownership is sampled, metres.</summary>
        public const float Probe = 0.5f;

        /// <summary>
        /// Appends the walls overlapping the rectangle, each clipped to it (plus <paramref name="margin"/>), from
        /// <paramref name="bottom"/> to <paramref name="top"/>. <paramref name="outside"/>(x, z) tells whether a point is
        /// outside the playable area. Returns the number of wall pieces emitted.
        /// </summary>
        public static int Emit(Func<float, float, bool> outside, float origin, float size, float minX, float minZ, float maxX, float maxZ,
            float margin, float bottom, float top, ushort flags, TriangleBuffer into)
        {
            if (outside == null) throw new ArgumentNullException("outside");
            if (!(size > 0f)) throw new ArgumentOutOfRangeException("size");
            int pieces = 0;
            float x0 = minX - margin, x1 = maxX + margin, z0 = minZ - margin, z1 = maxZ + margin;
            // Borders x = const, split at the z borders so each piece separates exactly two tiles.
            for (int k = (int)Math.Ceiling((x0 - Thickness - origin) / size); origin + k * size <= x1 + Thickness; k++)
            {
                float bx = origin + k * size;
                foreach (float[] span in Spans(origin, size, z0, z1))
                {
                    float mz = (span[0] + span[1]) / 2;
                    if (outside(bx - Probe, mz) == outside(bx + Probe, mz)) continue;
                    pieces += Wall(Math.Max(bx - Thickness / 2, x0), Math.Min(bx + Thickness / 2, x1), span[0], span[1], bottom, top, flags, into);
                }
            }
            for (int k = (int)Math.Ceiling((z0 - Thickness - origin) / size); origin + k * size <= z1 + Thickness; k++)
            {
                float bz = origin + k * size;
                foreach (float[] span in Spans(origin, size, x0, x1))
                {
                    float mx = (span[0] + span[1]) / 2;
                    if (outside(mx, bz - Probe) == outside(mx, bz + Probe)) continue;
                    pieces += Wall(span[0], span[1], Math.Max(bz - Thickness / 2, z0), Math.Min(bz + Thickness / 2, z1), bottom, top, flags, into);
                }
            }
            return pieces;
        }

        // [a, b] cut at every tile border inside it.
        private static System.Collections.Generic.IEnumerable<float[]> Spans(float origin, float size, float a, float b)
        {
            float s = a;
            for (int k = (int)Math.Floor((a - origin) / size) + 1; origin + k * size < b; k++)
            {
                float e = origin + k * size;
                if (e > s) yield return new[] { s, e };
                s = e;
            }
            if (b > s) yield return new[] { s, b };
        }

        // An axis-aligned box [xa, xb] x [za, zb] x [bottom, top] as 12 triangles; 0 when it is empty.
        private static int Wall(float xa, float xb, float za, float zb, float bottom, float top, ushort flags, TriangleBuffer into)
        {
            if (!(xb > xa) || !(zb > za) || !(top > bottom)) return 0;
            Box.Oriented((xa + xb) / 2, (za + zb) / 2, 0f, (xb - xa) / 2, (zb - za) / 2, bottom, top, flags, into);
            return 1;
        }
    }
}
