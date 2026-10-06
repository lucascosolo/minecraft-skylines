using System;

namespace Skylines.Core.Geometry
{
    /// <summary>
    /// CS1's segment mesh bend (<c>NetSegment.PopulateGroupData</c>; the net shaders do the same on the GPU): a mesh-local
    /// vertex (x across, y up, z along) goes to the point a fraction <c>x * scaleX + 0.5</c> of the way from the left edge
    /// curve to the right one, both taken at <c>t = z * scaleZ + 0.5</c>, raised by y. CS1's scales are
    /// <c>0.5 / m_halfWidth</c> and <c>1 / m_segmentLength</c>, both negated when the segment is drawn turned around.
    /// </summary>
    public static class NetBend
    {
        /// <summary>The world position of mesh-local (x, y, z) bent between <paramref name="left"/> and <paramref name="right"/>.</summary>
        public static void Point(Bezier3D left, Bezier3D right, float scaleX, float scaleZ, float x, float y, float z,
            out float wx, out float wy, out float wz)
        {
            float t = z * scaleZ + 0.5f, s = x * scaleX + 0.5f;
            float u = 1 - t, a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
            float lx = a * left.Ax + b * left.Bx + c * left.Cx + d * left.Dx;
            float ly = a * left.Ay + b * left.By + c * left.Cy + d * left.Dy;
            float lz = a * left.Az + b * left.Bz + c * left.Cz + d * left.Dz;
            float rx = a * right.Ax + b * right.Bx + c * right.Cx + d * right.Dx;
            float ry = a * right.Ay + b * right.By + c * right.Cy + d * right.Dy;
            float rz = a * right.Az + b * right.Bz + c * right.Cz + d * right.Dz;
            wx = lx + (rx - lx) * s;
            wy = ly + (ry - ly) * s + y;
            wz = lz + (rz - lz) * s;
        }

        /// <summary>
        /// Appends every triangle of the mesh whose highest local y is at least <paramref name="minY"/>, bent with
        /// <see cref="Point"/>, vertex order kept. Returns the triangles added.
        /// </summary>
        public static int Triangles(float[] positions, int[] indices, Bezier3D left, Bezier3D right, float scaleX, float scaleZ,
            float minY, ushort flags, TriangleBuffer into)
        {
            int added = 0;
            var w = new float[9];
            for (int t = 0; t + 2 < indices.Length; t += 3)
            {
                float top = Math.Max(positions[3 * indices[t] + 1], Math.Max(positions[3 * indices[t + 1] + 1], positions[3 * indices[t + 2] + 1]));
                if (top < minY) continue;
                for (int k = 0; k < 3; k++)
                {
                    int v = 3 * indices[t + k];
                    Point(left, right, scaleX, scaleZ, positions[v], positions[v + 1], positions[v + 2], out w[3 * k], out w[3 * k + 1], out w[3 * k + 2]);
                }
                into.Add(w[0], w[1], w[2], w[3], w[4], w[5], w[6], w[7], w[8], flags);
                added++;
            }
            return added;
        }
    }
}
