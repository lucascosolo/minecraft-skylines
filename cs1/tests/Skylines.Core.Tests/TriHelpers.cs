using System;
using System.Collections.Generic;
using Skylines.Core.Geometry;

namespace Skylines.Core.Tests
{
    internal struct Tri
    {
        public float[] A, B, C;

        public float[] Normal()
        {
            float ux = B[0] - A[0], uy = B[1] - A[1], uz = B[2] - A[2];
            float vx = C[0] - A[0], vy = C[1] - A[1], vz = C[2] - A[2];
            return new[] { uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx };
        }

        public float[] Centroid()
        {
            return new[] { (A[0] + B[0] + C[0]) / 3, (A[1] + B[1] + C[1]) / 3, (A[2] + B[2] + C[2]) / 3 };
        }

        public IEnumerable<float[]> Verts() { yield return A; yield return B; yield return C; }
    }

    internal static class TriHelpers
    {
        public static List<Tri> All(TriangleBuffer b)
        {
            var list = new List<Tri>();
            for (int i = 0; i < b.Count; i++)
            {
                int o = i * 9;
                var p = b.Positions;
                list.Add(new Tri
                {
                    A = new[] { p[o], p[o + 1], p[o + 2] },
                    B = new[] { p[o + 3], p[o + 4], p[o + 5] },
                    C = new[] { p[o + 6], p[o + 7], p[o + 8] },
                });
            }
            return list;
        }

        // Classifies by dominant normal axis: "up", "down", or "side".
        public static string Kind(Tri t)
        {
            var n = t.Normal();
            if (Math.Abs(n[1]) > Math.Abs(n[0]) + Math.Abs(n[2])) return n[1] > 0 ? "up" : "down";
            return "side";
        }

        public static bool Near(float a, float b, float eps = 1e-4f) { return Math.Abs(a - b) <= eps; }
    }
}
