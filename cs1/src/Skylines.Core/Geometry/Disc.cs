using System;

namespace Skylines.Core.Geometry
{
    /// <summary>A flat disc, used for junctions.</summary>
    public static class Disc
    {
        /// <summary>
        /// Adds an up-facing triangle fan of <paramref name="segments"/> triangles around (cx, y, cz). A positive
        /// <paramref name="thickness"/> adds a down-facing bottom fan and outward walls.
        /// </summary>
        public static void Fan(float cx, float y, float cz, float radius, int segments, float thickness, ushort flags, TriangleBuffer into)
        {
            if (segments < 3) throw new ArgumentOutOfRangeException("segments");
            float yb = y - thickness;
            for (int i = 0; i < segments; i++)
            {
                double a0 = 2 * Math.PI * i / segments, a1 = 2 * Math.PI * (i + 1) / segments;
                float x0 = cx + radius * (float)Math.Cos(a0), z0 = cz + radius * (float)Math.Sin(a0);
                float x1 = cx + radius * (float)Math.Cos(a1), z1 = cz + radius * (float)Math.Sin(a1);
                into.Add(cx, y, cz, x1, y, z1, x0, y, z0, flags);
                if (!(thickness > 0)) continue;
                into.Add(cx, yb, cz, x0, yb, z0, x1, yb, z1, flags);
                into.Add(x0, y, z0, x1, y, z1, x0, yb, z0, flags);
                into.Add(x0, yb, z0, x1, y, z1, x1, yb, z1, flags);
            }
        }

        /// <summary>
        /// Adds an up-facing fan from (cx, cy, cz) over <paramref name="count"/> ring vertices (x, y, z triples in any
        /// order; sorted by angle around the centre, the array is not modified). A positive <paramref name="thickness"/>
        /// adds a down-facing copy lowered by it and outward walls along every ring edge. Fewer than three vertices add nothing.
        /// </summary>
        public static void Polygon(float cx, float cy, float cz, float[] ring, int count, float thickness, ushort flags, TriangleBuffer into)
        {
            if (ring == null) throw new ArgumentNullException("ring");
            if (count < 0 || ring.Length < 3 * count) throw new ArgumentException("ring holds fewer than count vertices", "ring");
            if (count < 3) return;
            var order = new int[count];
            var angle = new double[count];
            for (int i = 0; i < count; i++)
            {
                order[i] = 3 * i;
                angle[i] = Math.Atan2(ring[3 * i + 2] - cz, ring[3 * i] - cx);
            }
            Array.Sort(angle, order);
            float yb = cy - thickness;
            for (int i = 0; i < count; i++)
            {
                // Increasing angle in x-z is clockwise seen from above (+y), so (centre, next, this) faces up.
                int p = order[i], q = order[(i + 1) % count];
                float x0 = ring[p], y0 = ring[p + 1], z0 = ring[p + 2];
                float x1 = ring[q], y1 = ring[q + 1], z1 = ring[q + 2];
                into.Add(cx, cy, cz, x1, y1, z1, x0, y0, z0, flags);
                if (!(thickness > 0)) continue;
                into.Add(cx, yb, cz, x0, y0 - thickness, z0, x1, y1 - thickness, z1, flags);
                into.Add(x0, y0, z0, x1, y1, z1, x0, y0 - thickness, z0, flags);
                into.Add(x0, y0 - thickness, z0, x1, y1, z1, x1, y1 - thickness, z1, flags);
            }
        }
    }
}
