namespace Skylines.Core.Geometry
{
    /// <summary>
    /// Extends a mesh's walls below its ground line, as Cities: Skylines does for a building's foundation
    /// (BuildingInfoBase.InitMeshData: triangles with exactly two vertices within 0.04 m of y = 0 get a quad below that edge).
    /// </summary>
    public static class Skirt
    {
        /// <summary>How close to the ground line a vertex must be to count as on it, in metres.</summary>
        public const float GroundTolerance = 0.04f;

        /// <summary>
        /// For each triangle of [start, Count) with exactly two vertices on <paramref name="groundY"/>, appends the quad from that
        /// edge down to <paramref name="groundY"/> - <paramref name="depth"/>, facing the triangle's side. Returns triangles appended.
        /// </summary>
        public static int Append(TriangleBuffer mesh, int start, float groundY, float depth, ushort flags)
        {
            if (!(depth > 0f)) return 0;
            int end = mesh.Count, added = 0;
            float low = groundY - depth;
            for (int i = start; i < end; i++)
            {
                float[] p = mesh.Positions; // Add may reallocate
                int o = 9 * i;
                bool g0 = OnGround(p[o + 1], groundY), g1 = OnGround(p[o + 4], groundY), g2 = OnGround(p[o + 7], groundY);
                int e;
                if (g0 && g1 && !g2) e = 0;
                else if (g1 && g2 && !g0) e = 1;
                else if (g2 && g0 && !g1) e = 2;
                else continue;
                int pa = o + 3 * e, pb = o + 3 * ((e + 1) % 3);
                float sx = Normal(p, o, 0), sz = Normal(p, o, 2);
                float px = p[pa], pz = p[pa + 2], qx = p[pb], qz = p[pb + 2];
                // Quad q -> p -> p' -> q' continues the edge's reverse direction; its horizontal normal is (pz - qz, qx - px)
                // times depth. Flip it when that faces away from the source triangle.
                if ((pz - qz) * sx + (qx - px) * sz < 0f)
                {
                    float tx = px, tz = pz; px = qx; pz = qz; qx = tx; qz = tz;
                }
                mesh.Add(qx, groundY, qz, px, groundY, pz, px, low, pz, flags);
                mesh.Add(qx, groundY, qz, px, low, pz, qx, low, qz, flags);
                added += 2;
            }
            return added;
        }

        private static bool OnGround(float y, float groundY)
        {
            return y > groundY - GroundTolerance && y < groundY + GroundTolerance;
        }

        private static float Normal(float[] p, int o, int axis)
        {
            float ux = p[o + 3] - p[o], uy = p[o + 4] - p[o + 1], uz = p[o + 5] - p[o + 2];
            float vx = p[o + 6] - p[o], vy = p[o + 7] - p[o + 1], vz = p[o + 8] - p[o + 2];
            return axis == 0 ? uy * vz - uz * vy : ux * vy - uy * vx;
        }
    }
}
