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
    /// The visible inside of a road tunnel (walls and a level ceiling) over the same <see cref="TunnelProfile"/> sections
    /// as the collision tube (Skylines.Host NetGeometry.Tube), and the floor, ceiling and walls of an underground junction.
    /// Every face is one-sided and faces the inside: for each triangle (a, b, c), cross(b - a, c - a) points the same way
    /// as its vertices' normals (Unity draws clockwise front faces, and that cross product points at the viewer).
    /// UVs are in metres divided by the tile size. Unity-free.
    /// </summary>
    public static class TunnelShell
    {
        /// <summary>How far below the drawn edge the walls start, in metres, so no gap shows under them.</summary>
        public const float WallFoot = 0.5f;

        /// <summary>
        /// Appends the shell over the covered intervals of <paramref name="sections"/> (<see cref="TunnelProfile.Build"/>):
        /// per side a vertical wall quad from each section's foot point minus <see cref="WallFoot"/> up to its Top minus
        /// <paramref name="ceilingThickness"/>, normal horizontal and towards the other side; and a ceiling quad between the
        /// four foot points at their section's Top minus ceilingThickness (level across the width), normal (0, -1, 0).
        /// Returns the number of covered intervals.
        /// </summary>
        public static int Segment(IList<TunnelSection> sections, float ceilingThickness, float tileMetres, ShellMesh into)
        {
            float run = 0f;
            int covered = 0;
            for (int k = 0; k + 1 < sections.Count; k++)
            {
                TunnelSection a = sections[k], b = sections[k + 1];
                float len = (float)Math.Sqrt(Sq(b.Lx - a.Lx) + Sq(b.Lz - a.Lz));
                if (!a.Covered) { run += len; continue; }
                float ya = a.Top - ceilingThickness, yb = b.Top - ceilingThickness;
                float ox = (a.Rx + b.Rx - a.Lx - b.Lx) * 0.5f, oz = (a.Rz + b.Rz - a.Lz - b.Lz) * 0.5f;
                Wall(a.Lx, a.Ly, a.Lz, ya, b.Lx, b.Ly, b.Lz, yb, ox, oz, run, len, tileMetres, into);
                Wall(a.Rx, a.Ry, a.Rz, ya, b.Rx, b.Ry, b.Rz, yb, -ox, -oz, run, len, tileMetres, into);
                float w = (float)Math.Sqrt(Sq(a.Rx - a.Lx) + Sq(a.Rz - a.Lz)) / tileMetres;
                int p = into.Add(a.Lx, ya, a.Lz, 0f, -1f, 0f, run / tileMetres, 0f);
                int q = into.Add(b.Lx, yb, b.Lz, 0f, -1f, 0f, (run + len) / tileMetres, 0f);
                int r = into.Add(b.Rx, yb, b.Rz, 0f, -1f, 0f, (run + len) / tileMetres, w);
                int s = into.Add(a.Rx, ya, a.Rz, 0f, -1f, 0f, run / tileMetres, w);
                Face(into, p, q, r, s, 0f, -1f, 0f);
                run += len;
                covered++;
            }
            return covered;
        }

        private static float Sq(float v)
        {
            return v * v;
        }

        // A vertical quad from (x0, y0 - WallFoot, z0)-(x1, ...) up to tops t0, t1, facing the horizontal direction (ox, oz)
        // as far as it is perpendicular to the wall.
        private static void Wall(float x0, float y0, float z0, float t0, float x1, float y1, float z1, float t1, float ox, float oz,
            float run, float len, float tile, ShellMesh into)
        {
            float nx = -(z1 - z0), nz = x1 - x0;
            if (nx * ox + nz * oz < 0f) { nx = -nx; nz = -nz; }
            float m = (float)Math.Sqrt(nx * nx + nz * nz);
            if (m < 1e-6f) return;
            nx /= m; nz /= m;
            int a = into.Add(x0, y0 - WallFoot, z0, nx, 0f, nz, run / tile, 0f);
            int b = into.Add(x1, y1 - WallFoot, z1, nx, 0f, nz, (run + len) / tile, 0f);
            int c = into.Add(x1, t1, z1, nx, 0f, nz, (run + len) / tile, (t1 - y1 + WallFoot) / tile);
            int d = into.Add(x0, t0, z0, nx, 0f, nz, run / tile, (t0 - y0 + WallFoot) / tile);
            Face(into, a, b, c, d, nx, 0f, nz);
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
        /// Appends the floor, ceiling and walls of an underground junction of at least three segments, each given by its
        /// section at the node (<paramref name="mouths"/>). The ring is every mouth's L and R, visited in order of angle
        /// around (cx, cz). Per consecutive ring pair: a floor triangle from the centre at the points' own heights, normal
        /// (0, 1, 0); a ceiling triangle at each point's mouth Top minus ceilingThickness, normal (0, -1, 0) (centre at the
        /// mean of each); and between points of different mouths a wall from floor minus <see cref="WallFoot"/> up to the
        /// ceiling, facing the centre. Returns the triangles appended; 0 when there are fewer than three mouths.
        /// </summary>
        public static int Junction(float cx, float cz, IList<TunnelSection> mouths, float ceilingThickness, float tileMetres, ShellMesh into)
        {
            int count = mouths.Count;
            if (count < 3) return 0;
            int ring = 2 * count;
            var x = new float[ring];
            var y = new float[ring];
            var z = new float[ring];
            var top = new float[ring];
            var order = new int[ring];
            var angle = new double[ring];
            float cy = 0f, ctop = 0f;
            for (int i = 0; i < count; i++)
            {
                TunnelSection m = mouths[i];
                x[2 * i] = m.Lx; y[2 * i] = m.Ly; z[2 * i] = m.Lz;
                x[2 * i + 1] = m.Rx; y[2 * i + 1] = m.Ry; z[2 * i + 1] = m.Rz;
                top[2 * i] = top[2 * i + 1] = m.Top - ceilingThickness;
            }
            for (int i = 0; i < ring; i++)
            {
                order[i] = i;
                angle[i] = Math.Atan2(z[i] - cz, x[i] - cx);
                cy += y[i] / ring;
                ctop += top[i] / ring;
            }
            Array.Sort(angle, order);
            int triangles = 0;
            for (int i = 0; i < ring; i++)
            {
                int p = order[i], q = order[(i + 1) % ring];
                Triangle(into, cx, cy, cz, x[p], y[p], z[p], x[q], y[q], z[q], 1f, tileMetres);
                Triangle(into, cx, ctop, cz, x[p], top[p], z[p], x[q], top[q], z[q], -1f, tileMetres);
                triangles += 2;
                if (p / 2 == q / 2) continue;
                float len = (float)Math.Sqrt(Sq(x[q] - x[p]) + Sq(z[q] - z[p]));
                Wall(x[p], y[p], z[p], top[p], x[q], y[q], z[q], top[q], cx - (x[p] + x[q]) * 0.5f, cz - (z[p] + z[q]) * 0.5f,
                    0f, len, tileMetres, into);
                triangles += 2;
            }
            return triangles;
        }

        private static void Triangle(ShellMesh m, float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz,
            float ny, float tile)
        {
            int a = m.Add(ax, ay, az, 0f, ny, 0f, ax / tile, az / tile);
            int b = m.Add(bx, by, bz, 0f, ny, 0f, bx / tile, bz / tile);
            int c = m.Add(cx, cy, cz, 0f, ny, 0f, cx / tile, cz / tile);
            if (Facing(m, a, b, c, 0f, ny, 0f)) { m.Indices.Add(a); m.Indices.Add(b); m.Indices.Add(c); }
            else { m.Indices.Add(a); m.Indices.Add(c); m.Indices.Add(b); }
        }
    }
}
