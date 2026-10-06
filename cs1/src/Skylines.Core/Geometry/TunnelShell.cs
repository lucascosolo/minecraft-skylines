using System;
using System.Collections.Generic;

namespace Skylines.Core.Geometry
{
    /// <summary>A growable triangle mesh: per-vertex position, normal and uv, and an index list (three per triangle).</summary>
    public sealed class ShellMesh
    {
        /// <summary>x, y, z per vertex.</summary>
        public readonly List<float> Positions = new List<float>();
        /// <summary>Unit normal x, y, z per vertex.</summary>
        public readonly List<float> Normals = new List<float>();
        /// <summary>u, v per vertex.</summary>
        public readonly List<float> Uvs = new List<float>();
        /// <summary>Vertex indices, three per triangle.</summary>
        public readonly List<int> Indices = new List<int>();

        /// <summary>Number of vertices.</summary>
        public int VertexCount { get { return Positions.Count / 3; } }

        /// <summary>Removes everything.</summary>
        public void Clear()
        {
            Positions.Clear();
            Normals.Clear();
            Uvs.Clear();
            Indices.Clear();
        }

        /// <summary>Appends a vertex and returns its index.</summary>
        public int Add(float x, float y, float z, float nx, float ny, float nz, float u, float v)
        {
            Positions.Add(x); Positions.Add(y); Positions.Add(z);
            Normals.Add(nx); Normals.Add(ny); Normals.Add(nz);
            Uvs.Add(u); Uvs.Add(v);
            return VertexCount - 1;
        }

        /// <summary>Appends the two triangles (a, b, c) and (a, c, d).</summary>
        public void Quad(int a, int b, int c, int d)
        {
            Indices.Add(a); Indices.Add(b); Indices.Add(c);
            Indices.Add(a); Indices.Add(c); Indices.Add(d);
        }
    }

    /// <summary>
    /// The visible inside of a road tunnel (walls and ceiling) along the same drawn edges and with the same clearance
    /// rules as the collision tube (Skylines.Host NetGeometry.Tube), and the floor and ceiling of an underground junction.
    /// Every face is one-sided and faces the inside: for each triangle (a, b, c), cross(b - a, c - a) points the same way
    /// as its vertices' normals (Unity draws clockwise front faces, and that cross product points at the viewer).
    /// UVs are in metres divided by the tile size. Unity-free.
    /// </summary>
    public static class TunnelShell
    {
        /// <summary>How far below the drawn edge the walls start, in metres, so no gap shows under them.</summary>
        public const float WallFoot = 0.5f;

        /// <summary>
        /// Appends the shell of one tunnel or slope segment between its drawn edges <paramref name="left"/> and
        /// <paramref name="right"/> (both running start to end). The segment is split into n = max(1, ceil(chord / step))
        /// intervals, chord = straight distance from left.A to left.D, interval k covering t in [k/n, (k+1)/n] of both
        /// edges (Bezier3D.Cut). An interval is covered when <paramref name="tunnel"/> is true, or when
        /// floorTop + clearance &lt;= ground, where floorTop = max of the mean edge y at the interval's two ends and
        /// ground = max of the mean edge y at t = 0 and t = 1 (the slope's portal level). Uncovered intervals get nothing.
        /// A covered interval gets: per side, a vertical wall quad on the edge inset by <paramref name="wallInset"/>
        /// towards the other edge (Strip.Inset with lift 0 on the cut curves), from the inset curve's end points' y minus
        /// <see cref="WallFoot"/> up to y + clearance - ceilingThickness, its normal horizontal, perpendicular to the
        /// interval's chord and pointing towards the other edge; and a ceiling quad between the two inset curves' end points
        /// raised by clearance - ceilingThickness, normal (0, -1, 0). Returns the number of covered intervals; 0 and nothing
        /// appended when the edges are not more than 2 * wallInset apart.
        /// </summary>
        public static int Segment(Bezier3D left, Bezier3D right, float clearance, bool tunnel, float step, float wallInset,
            float ceilingThickness, float tileMetres, ShellMesh into)
        {
            float wx = (right.Ax + right.Dx - left.Ax - left.Dx) * 0.5f, wy = (right.Ay + right.Dy - left.Ay - left.Dy) * 0.5f;
            float wz = (right.Az + right.Dz - left.Az - left.Dz) * 0.5f;
            if (!(Math.Sqrt(wx * wx + wy * wy + wz * wz) > 2 * wallInset)) return 0;
            float dx = left.Dx - left.Ax, dy = left.Dy - left.Ay, dz = left.Dz - left.Az;
            int n = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy + dz * dz) / step));
            float ground = Math.Max((left.Ay + right.Ay) * 0.5f, (left.Dy + right.Dy) * 0.5f);
            float top = clearance - ceilingThickness;
            float run = 0f;
            int covered = 0;
            for (int k = 0; k < n; k++)
            {
                float t0 = k / (float)n, t1 = (k + 1) / (float)n;
                Bezier3D lk = left.Cut(t0, t1), rk = right.Cut(t0, t1), il, ir;
                float floorTop = Math.Max((lk.Ay + rk.Ay) * 0.5f, (lk.Dy + rk.Dy) * 0.5f);
                if (!(tunnel || floorTop + clearance <= ground)) continue;
                if (!Strip.Inset(lk, rk, wallInset, 0f, out il) || !Strip.Inset(rk, lk, wallInset, 0f, out ir)) return covered;
                float len = (float)Math.Sqrt(Sq(il.Dx - il.Ax) + Sq(il.Dz - il.Az));
                Wall(il, ir, top, run, len, tileMetres, into);
                Wall(ir, il, top, run, len, tileMetres, into);
                Ceiling(il, ir, top, run, len, tileMetres, into);
                run += len;
                covered++;
            }
            return covered;
        }

        private static float Sq(float v)
        {
            return v * v;
        }

        // A vertical quad on `edge` (its end points), facing `other`.
        private static void Wall(Bezier3D edge, Bezier3D other, float top, float run, float len, float tile, ShellMesh into)
        {
            float ex = edge.Dx - edge.Ax, ez = edge.Dz - edge.Az;
            float nx = -ez, nz = ex;
            float ox = (other.Ax + other.Dx - edge.Ax - edge.Dx) * 0.5f, oz = (other.Az + other.Dz - edge.Az - edge.Dz) * 0.5f;
            if (nx * ox + nz * oz < 0f) { nx = -nx; nz = -nz; }
            float m = (float)Math.Sqrt(nx * nx + nz * nz);
            if (m < 1e-6f) return;
            nx /= m; nz /= m;
            float h = top + WallFoot;
            int a = into.Add(edge.Ax, edge.Ay - WallFoot, edge.Az, nx, 0f, nz, run / tile, 0f);
            int b = into.Add(edge.Dx, edge.Dy - WallFoot, edge.Dz, nx, 0f, nz, (run + len) / tile, 0f);
            int c = into.Add(edge.Dx, edge.Dy + top, edge.Dz, nx, 0f, nz, (run + len) / tile, h / tile);
            int d = into.Add(edge.Ax, edge.Ay + top, edge.Az, nx, 0f, nz, run / tile, h / tile);
            Face(into, a, b, c, d, nx, 0f, nz);
        }

        private static void Ceiling(Bezier3D l, Bezier3D r, float top, float run, float len, float tile, ShellMesh into)
        {
            float w = (float)Math.Sqrt(Sq(r.Ax - l.Ax) + Sq(r.Az - l.Az)) / tile;
            int a = into.Add(l.Ax, l.Ay + top, l.Az, 0f, -1f, 0f, run / tile, 0f);
            int b = into.Add(l.Dx, l.Dy + top, l.Dz, 0f, -1f, 0f, (run + len) / tile, 0f);
            int c = into.Add(r.Dx, r.Dy + top, r.Dz, 0f, -1f, 0f, (run + len) / tile, w);
            int d = into.Add(r.Ax, r.Ay + top, r.Az, 0f, -1f, 0f, run / tile, w);
            Face(into, a, b, c, d, 0f, -1f, 0f);
        }

        // Adds quad a-b-c-d with the winding whose cross product points along (nx, ny, nz).
        private static void Face(ShellMesh m, int a, int b, int c, int d, float nx, float ny, float nz)
        {
            if (Facing(m, a, b, c, nx, ny, nz)) m.Quad(a, b, c, d);
            else m.Quad(a, d, c, b);
        }

        private static bool Facing(ShellMesh m, int a, int b, int c, float nx, float ny, float nz)
        {
            List<float> p = m.Positions;
            float ux = p[3 * b] - p[3 * a], uy = p[3 * b + 1] - p[3 * a + 1], uz = p[3 * b + 2] - p[3 * a + 2];
            float vx = p[3 * c] - p[3 * a], vy = p[3 * c + 1] - p[3 * a + 1], vz = p[3 * c + 2] - p[3 * a + 2];
            float cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
            return cx * nx + cy * ny + cz * nz > 0f;
        }

        /// <summary>
        /// Appends the floor and ceiling of an underground junction: a fan from (cx, cy, cz) over the <paramref name="count"/>
        /// ring points in <paramref name="ring"/> (x, y, z each), visited in order of their angle around the centre in xz
        /// (whatever order they are given in). Floor triangles (centre, p_i, p_i+1 at the ring points' own heights, centre at
        /// cy) face up, normal (0, 1, 0); ceiling triangles are the same points raised by clearance - ceilingThickness and
        /// face down, normal (0, -1, 0). Returns the number of triangles appended (2 * count); 0 when count &lt; 3.
        /// </summary>
        public static int Junction(float cx, float cy, float cz, float[] ring, int count, float clearance, float ceilingThickness,
            float tileMetres, ShellMesh into)
        {
            if (count < 3) return 0;
            var order = new int[count];
            var angle = new double[count];
            for (int i = 0; i < count; i++)
            {
                order[i] = i;
                angle[i] = Math.Atan2(ring[3 * i + 2] - cz, ring[3 * i] - cx);
            }
            Array.Sort(angle, order);
            float top = clearance - ceilingThickness;
            for (int i = 0; i < count; i++)
            {
                int p = order[i], q = order[(i + 1) % count];
                FanTriangle(into, cx, cy, cz, ring, p, q, 0f, 1f, tileMetres);
                FanTriangle(into, cx, cy, cz, ring, p, q, top, -1f, tileMetres);
            }
            return 2 * count;
        }

        private static void FanTriangle(ShellMesh m, float cx, float cy, float cz, float[] ring, int p, int q, float lift, float ny, float tile)
        {
            int a = m.Add(cx, cy + lift, cz, 0f, ny, 0f, cx / tile, cz / tile);
            int b = m.Add(ring[3 * p], ring[3 * p + 1] + lift, ring[3 * p + 2], 0f, ny, 0f, ring[3 * p] / tile, ring[3 * p + 2] / tile);
            int c = m.Add(ring[3 * q], ring[3 * q + 1] + lift, ring[3 * q + 2], 0f, ny, 0f, ring[3 * q] / tile, ring[3 * q + 2] / tile);
            if (Facing(m, a, b, c, 0f, ny, 0f)) { m.Indices.Add(a); m.Indices.Add(b); m.Indices.Add(c); }
            else { m.Indices.Add(a); m.Indices.Add(c); m.Indices.Add(b); }
        }
    }
}
