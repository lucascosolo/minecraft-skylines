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
    }
}
