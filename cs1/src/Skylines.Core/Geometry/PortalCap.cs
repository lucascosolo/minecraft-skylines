using System;
using System.Collections.Generic;

namespace Skylines.Core.Geometry
{
    /// <summary>
    /// Finds the black end wall of a CS1 tunnel slope (portal) mesh: the cross-section caps at its deep end that close the
    /// passage. In mesh-local space (x across, y up from the road surface, z along) the deep end is the end whose structure
    /// stands at least <see cref="MinRise"/> higher (the portal roof); a cap is a group of triangles in one plane of
    /// constant z within <see cref="EndReach"/> of that end, at least one of which covers the road at
    /// (<c>probeX</c>, <see cref="ProbeHeight"/>). Lintels above the opening and parapet ends beside it do not cover that
    /// point and are kept.
    /// </summary>
    public static class PortalCap
    {
        /// <summary>Height above the road surface a cap must cover, in metres.</summary>
        public const float ProbeHeight = 1.5f;
        /// <summary>How far z may vary within one cap plane, in metres.</summary>
        public const float PlaneTolerance = 0.01f;
        /// <summary>How far from the deep end a cap may lie, and the depth of each end's height sample, in metres.</summary>
        public const float EndReach = 4f;
        /// <summary>How much higher the deep end's structure stands than the other end's, in metres.</summary>
        public const float MinRise = 1f;

        /// <summary>The triangle list without the deep-end caps, remaining triangles in order.</summary>
        public static int[] Remove(float[] positions, int[] indices, float probeX, out int capTriangles)
        {
            capTriangles = 0;
            int n = positions.Length / 3;
            if (n == 0) return (int[])indices.Clone();
            float zMin = float.MaxValue, zMax = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                zMin = Math.Min(zMin, positions[3 * i + 2]);
                zMax = Math.Max(zMax, positions[3 * i + 2]);
            }
            float lo = float.MinValue, hi = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                float y = positions[3 * i + 1], z = positions[3 * i + 2];
                if (z <= zMin + EndReach) lo = Math.Max(lo, y);
                if (z >= zMax - EndReach) hi = Math.Max(hi, y);
            }
            float deep;
            if (lo >= hi + MinRise) deep = zMin;
            else if (hi >= lo + MinRise) deep = zMax;
            else return (int[])indices.Clone();

            // Planes are keyed by the z of the first triangle found in them.
            var planes = new List<float>();
            var planeOf = new int[indices.Length / 3];
            var isCap = new List<bool>();
            for (int t = 0; t < planeOf.Length; t++)
            {
                planeOf[t] = -1;
                int a = 3 * indices[3 * t], b = 3 * indices[3 * t + 1], c = 3 * indices[3 * t + 2];
                float za = positions[a + 2], zb = positions[b + 2], zc = positions[c + 2];
                if (Math.Max(za, Math.Max(zb, zc)) - Math.Min(za, Math.Min(zb, zc)) > PlaneTolerance) continue;
                if (Math.Abs(za - deep) > EndReach) continue;
                int p = planes.FindIndex(z => Math.Abs(z - za) <= PlaneTolerance);
                if (p < 0)
                {
                    p = planes.Count;
                    planes.Add(za);
                    isCap.Add(false);
                }
                planeOf[t] = p;
                if (Covers(probeX, ProbeHeight, positions[a], positions[a + 1], positions[b], positions[b + 1], positions[c], positions[c + 1])) isCap[p] = true;
            }
            var kept = new List<int>(indices.Length);
            for (int t = 0; t < planeOf.Length; t++)
            {
                if (planeOf[t] >= 0 && isCap[planeOf[t]])
                {
                    capTriangles++;
                    continue;
                }
                kept.Add(indices[3 * t]);
                kept.Add(indices[3 * t + 1]);
                kept.Add(indices[3 * t + 2]);
            }
            return kept.ToArray();
        }

        // Whether (px, py) lies in the triangle, edges included.
        private static bool Covers(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
            float d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
            float d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }
}
