using System;
using System.Collections.Generic;

namespace Skylines.Core.Geometry
{
    /// <summary>
    /// Removes convex xz areas (all heights) from triangles exactly: what lies outside every area is kept, split along
    /// the areas' edges, with heights interpolated on the source triangle. Used to take terrain out from under sunken
    /// road surfaces whose own collision covers the cut.
    /// </summary>
    public sealed class ConvexCut
    {
        private const float MinCross = 2e-6f; // as RectClip: pieces under 1e-6 m^2 are dropped
        private const int MaxVerts = 16;

        private readonly List<float[]> _areas = new List<float[]>(); // x, z pairs, counter-clockwise seen from above (+y)
        private readonly List<float[]> _bounds = new List<float[]>(); // minX, minZ, maxX, maxZ per area
        private float[] _pieceA = new float[3 * MaxVerts * 4], _pieceB = new float[3 * MaxVerts * 4];

        /// <summary>Number of areas added since the last <see cref="Clear"/>.</summary>
        public int Count { get { return _areas.Count; } }

        /// <summary>Forgets every area.</summary>
        public void Clear()
        {
            _areas.Clear();
            _bounds.Clear();
        }

        /// <summary>
        /// Adds a convex area given as <paramref name="n"/> xz points (<paramref name="xz"/>: x0, z0, x1, z1, ...) in either
        /// winding. Degenerate areas (fewer than 3 points or no area) are ignored.
        /// </summary>
        public void Add(float[] xz, int n)
        {
            if (n < 3 || n > MaxVerts || xz == null || xz.Length < 2 * n) return;
            double area2 = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area2 += (double)xz[2 * i] * xz[2 * j + 1] - (double)xz[2 * j] * xz[2 * i + 1];
            }
            if (Math.Abs(area2) < 1e-6) return;
            var p = new float[2 * n];
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                // Stored so that the inside lies to the left of each edge in (x, z) with the shoelace sign positive.
                int s = area2 > 0 ? i : n - 1 - i;
                p[2 * i] = xz[2 * s];
                p[2 * i + 1] = xz[2 * s + 1];
                x0 = Math.Min(x0, p[2 * i]); x1 = Math.Max(x1, p[2 * i]);
                z0 = Math.Min(z0, p[2 * i + 1]); z1 = Math.Max(z1, p[2 * i + 1]);
            }
            _areas.Add(p);
            _bounds.Add(new[] { x0, z0, x1, z1 });
        }

        /// <summary>Adds a triangle area.</summary>
        public void AddTriangle(float ax, float az, float bx, float bz, float cx, float cz)
        {
            Add(new[] { ax, az, bx, bz, cx, cz }, 3);
        }

        /// <summary>
        /// Appends to <paramref name="into"/> the parts of <paramref name="from"/>'s triangles [<paramref name="start"/>,
        /// <paramref name="end"/>) outside every area, fan-triangulated with the source winding and flags. A triangle no
        /// area's bounds touch is appended unchanged. Returns the number of triangles appended.
        /// </summary>
        public int Apply(TriangleBuffer from, int start, int end, TriangleBuffer into)
        {
            if (from == into) throw new ArgumentException("from and into must differ");
            float[] p = from.Positions;
            ushort[] f = from.Flags;
            int added = 0;
            var pieces = new List<float[]>();
            var next = new List<float[]>();
            for (int t = start; t < end; t++)
            {
                int o = t * 9;
                float tx0 = Math.Min(p[o], Math.Min(p[o + 3], p[o + 6])), tx1 = Math.Max(p[o], Math.Max(p[o + 3], p[o + 6]));
                float tz0 = Math.Min(p[o + 2], Math.Min(p[o + 5], p[o + 8])), tz1 = Math.Max(p[o + 2], Math.Max(p[o + 5], p[o + 8]));
                pieces.Clear();
                pieces.Add(new[] { p[o], p[o + 1], p[o + 2], p[o + 3], p[o + 4], p[o + 5], p[o + 6], p[o + 7], p[o + 8] });
                for (int a = 0; a < _areas.Count && pieces.Count > 0; a++)
                {
                    float[] b = _bounds[a];
                    if (b[2] < tx0 || b[0] > tx1 || b[3] < tz0 || b[1] > tz1) continue;
                    next.Clear();
                    foreach (float[] piece in pieces) Subtract(piece, _areas[a], next);
                    var swap = pieces; pieces = next; next = swap;
                }
                foreach (float[] piece in pieces)
                {
                    int n = piece.Length / 3;
                    for (int i = 1; i + 1 < n; i++)
                    {
                        int j = 3 * i, k = 3 * (i + 1);
                        if (Cross(piece, 0, j, k) <= MinCross) continue;
                        into.Add(piece[0], piece[1], piece[2], piece[j], piece[j + 1], piece[j + 2], piece[k], piece[k + 1], piece[k + 2], f[t]);
                        added++;
                    }
                }
            }
            return added;
        }

        // Splits convex polygon `piece` (x, y, z triples) by each edge of convex `area`: the part outside an edge is kept,
        // the part inside goes on to the next edge; what is inside every edge is the overlap and is dropped.
        private void Subtract(float[] piece, float[] area, List<float[]> outside)
        {
            int n = piece.Length / 3;
            Array.Copy(piece, _pieceA, piece.Length);
            int m = area.Length / 2;
            for (int e = 0; e < m && n > 0; e++)
            {
                float ex = area[2 * e], ez = area[2 * e + 1];
                float fx = area[2 * ((e + 1) % m)], fz = area[2 * ((e + 1) % m) + 1];
                // Signed side: positive = inside (left of e->f with the area's positive shoelace orientation).
                float nx = -(fz - ez), nz = fx - ex;
                int outN = Clip(_pieceA, n, _pieceB, ex, ez, -nx, -nz);
                if (outN >= 3)
                {
                    var kept = new float[3 * outN];
                    Array.Copy(_pieceB, kept, kept.Length);
                    outside.Add(kept);
                }
                n = Clip(_pieceA, n, _pieceB, ex, ez, nx, nz);
                var swap = _pieceA; _pieceA = _pieceB; _pieceB = swap;
            }
        }

        // Sutherland-Hodgman against the half-plane (x - px) * nx + (z - pz) * nz >= 0; returns the vertex count.
        private static int Clip(float[] src, int n, float[] dst, float px, float pz, float nx, float nz)
        {
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float si = (src[3 * i] - px) * nx + (src[3 * i + 2] - pz) * nz;
                float sj = (src[3 * j] - px) * nx + (src[3 * j + 2] - pz) * nz;
                if (si >= 0)
                {
                    dst[3 * count] = src[3 * i]; dst[3 * count + 1] = src[3 * i + 1]; dst[3 * count + 2] = src[3 * i + 2];
                    count++;
                }
                if ((si >= 0) != (sj >= 0))
                {
                    float t = si / (si - sj);
                    for (int k = 0; k < 3; k++) dst[3 * count + k] = src[3 * i + k] + t * (src[3 * j + k] - src[3 * i + k]);
                    count++;
                }
            }
            return count;
        }

        private static float Cross(float[] q, int a, int b, int c)
        {
            float ux = q[b] - q[a], uy = q[b + 1] - q[a + 1], uz = q[b + 2] - q[a + 2];
            float vx = q[c] - q[a], vy = q[c + 1] - q[a + 1], vz = q[c + 2] - q[a + 2];
            float cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
            return (float)Math.Sqrt(cx * cx + cy * cy + cz * cz);
        }
    }
}
