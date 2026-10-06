using System;

namespace Skylines.Core.Geometry
{
    /// <summary>Distance from a point to triangles (closest-point test of Ericson, Real-Time Collision Detection 5.1.5).</summary>
    public static class PointTriangle
    {
        /// <summary>Squared distance from (px, py, pz) to the triangle (a, b, c), edges and corners included.</summary>
        public static float DistanceSquared(
            float px, float py, float pz,
            float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz)
        {
            float abx = bx - ax, aby = by - ay, abz = bz - az;
            float acx = cx - ax, acy = cy - ay, acz = cz - az;
            float apx = px - ax, apy = py - ay, apz = pz - az;
            float d1 = abx * apx + aby * apy + abz * apz;
            float d2 = acx * apx + acy * apy + acz * apz;
            if (d1 <= 0f && d2 <= 0f) return Sq(apx, apy, apz);

            float bpx = px - bx, bpy = py - by, bpz = pz - bz;
            float d3 = abx * bpx + aby * bpy + abz * bpz;
            float d4 = acx * bpx + acy * bpy + acz * bpz;
            if (d3 >= 0f && d4 <= d3) return Sq(bpx, bpy, bpz);

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return Sq(apx - v * abx, apy - v * aby, apz - v * abz);
            }

            float cpx = px - cx, cpy = py - cy, cpz = pz - cz;
            float d5 = abx * cpx + aby * cpy + abz * cpz;
            float d6 = acx * cpx + acy * cpy + acz * cpz;
            if (d6 >= 0f && d5 <= d6) return Sq(cpx, cpy, cpz);

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return Sq(apx - w * acx, apy - w * acy, apz - w * acz);
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return Sq(bpx - w * (cx - bx), bpy - w * (cy - by), bpz - w * (cz - bz));
            }

            float denom = 1f / (va + vb + vc);
            float sv = vb * denom, sw = vc * denom;
            return Sq(apx - sv * abx - sw * acx, apy - sv * aby - sw * acy, apz - sv * abz - sw * acz);
        }

        /// <summary>
        /// Smallest distance from the point to any triangle of a flat list (9 floats per triangle, first
        /// <paramref name="triangles"/> of them), or <see cref="float.PositiveInfinity"/> if none is within
        /// <paramref name="maxDistance"/>.
        /// </summary>
        public static float Nearest(float[] positions, int triangles, float px, float py, float pz, float maxDistance)
        {
            float best = maxDistance * maxDistance;
            bool found = false;
            for (int i = 0; i < triangles; i++)
            {
                int o = 9 * i;
                float d = DistanceSquared(px, py, pz, positions[o], positions[o + 1], positions[o + 2],
                    positions[o + 3], positions[o + 4], positions[o + 5], positions[o + 6], positions[o + 7], positions[o + 8]);
                if (d <= best) { best = d; found = true; }
            }
            return found ? (float)Math.Sqrt(best) : float.PositiveInfinity;
        }

        /// <summary>As <see cref="Nearest(float[], int, float, float, float, float)"/> for an indexed list (3 floats per vertex, 3 indices per triangle).</summary>
        public static float NearestIndexed(float[] positions, int[] indices, float px, float py, float pz, float maxDistance)
        {
            float best = maxDistance * maxDistance;
            bool found = false;
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int a = 3 * indices[i], b = 3 * indices[i + 1], c = 3 * indices[i + 2];
                float d = DistanceSquared(px, py, pz, positions[a], positions[a + 1], positions[a + 2],
                    positions[b], positions[b + 1], positions[b + 2], positions[c], positions[c + 1], positions[c + 2]);
                if (d <= best) { best = d; found = true; }
            }
            return found ? (float)Math.Sqrt(best) : float.PositiveInfinity;
        }

        private static float Sq(float x, float y, float z)
        {
            return x * x + y * y + z * z;
        }
    }
}
