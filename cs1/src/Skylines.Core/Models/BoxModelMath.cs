using System;

namespace Skylines.Core.Models
{
    /// <summary>A part's triangle mesh in the host's (z-mirrored) frame, metres.</summary>
    public sealed class BoxMesh
    {
        public float[] Positions;
        public float[] Uvs;
        public float[] Normals;
        public int[] Indices;
    }

    /// <summary>
    /// Transforms of a "skinned box model": parts in model units of 1/16 m with a local transform
    /// T(p/16) * Rz * Ry * Rx * S each, chained to their parents. Matrices are float[16], row-major, column vectors.
    /// </summary>
    public static class BoxModelMath
    {
        public const int FloatsPerQuad = 23;

        public static float[] Identity()
        {
            return new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
        }

        public static float[] Translation(float x, float y, float z)
        {
            return new float[] { 1, 0, 0, x, 0, 1, 0, y, 0, 0, 1, z, 0, 0, 0, 1 };
        }

        public static float[] FromAffine(float[] rows12)
        {
            if (rows12 == null || rows12.Length != 12) throw new ArgumentException("an affine matrix has 12 elements");
            var m = new float[16];
            Array.Copy(rows12, m, 12);
            m[15] = 1;
            return m;
        }

        public static float[] Multiply(float[] a, float[] b)
        {
            var m = new float[16];
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    m[r * 4 + c] = a[r * 4] * b[c] + a[r * 4 + 1] * b[4 + c] + a[r * 4 + 2] * b[8 + c] + a[r * 4 + 3] * b[12 + c];
            return m;
        }

        public static float[] PartLocal(float px, float py, float pz, float xRot, float yRot, float zRot, float xScale, float yScale, float zScale)
        {
            float cx = (float)Math.Cos(xRot), sx = (float)Math.Sin(xRot);
            float cy = (float)Math.Cos(yRot), sy = (float)Math.Sin(yRot);
            float cz = (float)Math.Cos(zRot), sz = (float)Math.Sin(zRot);
            // Rz * Ry * Rx, then columns scaled.
            float r00 = cz * cy, r01 = cz * sy * sx - sz * cx, r02 = cz * sy * cx + sz * sx;
            float r10 = sz * cy, r11 = sz * sy * sx + cz * cx, r12 = sz * sy * cx - cz * sx;
            float r20 = -sy, r21 = cy * sx, r22 = cy * cx;
            return new float[]
            {
                r00 * xScale, r01 * yScale, r02 * zScale, px / 16f,
                r10 * xScale, r11 * yScale, r12 * zScale, py / 16f,
                r20 * xScale, r21 * yScale, r22 * zScale, pz / 16f,
                0, 0, 0, 1,
            };
        }

        public static float[] MirrorZ(float[] m)
        {
            var o = (float[])m.Clone();
            o[2] = -o[2]; o[6] = -o[6]; o[8] = -o[8]; o[9] = -o[9]; o[11] = -o[11]; o[14] = -o[14];
            return o;
        }

        public static float[] Lerp(float[] a, float[] b, float t)
        {
            var o = new float[a.Length];
            for (int i = 0; i < a.Length; i++) o[i] = a[i] + (b[i] - a[i]) * t;
            return o;
        }

        public static float LerpAngle(float a, float b, float t)
        {
            double d = (b - a) % (2 * Math.PI);
            if (d > Math.PI) d -= 2 * Math.PI;
            else if (d <= -Math.PI) d += 2 * Math.PI;
            return (float)(a + d * t);
        }

        public static float[][] Chain(int[] parents, float[][] locals)
        {
            var world = new float[locals.Length][];
            for (int i = 0; i < locals.Length; i++)
                world[i] = parents[i] < 0 ? locals[i] : Multiply(world[parents[i]], locals[i]);
            return world;
        }

        public static bool[] Drawn(int[] parents, byte[] flags)
        {
            var hidden = new bool[flags.Length];
            var drawn = new bool[flags.Length];
            for (int i = 0; i < flags.Length; i++)
            {
                hidden[i] = (flags[i] & 1) != 0 || (parents[i] >= 0 && hidden[parents[i]]);
                drawn[i] = !hidden[i] && (flags[i] & 2) == 0;
            }
            return drawn;
        }

        public static BoxMesh BuildMesh(float[] quads)
        {
            int n = quads.Length / FloatsPerQuad;
            var m = new BoxMesh { Positions = new float[n * 12], Uvs = new float[n * 8], Normals = new float[n * 12], Indices = new int[n * 6] };
            for (int q = 0; q < n; q++)
            {
                int s = q * FloatsPerQuad;
                float nx = quads[s + 20], ny = quads[s + 21], nz = -quads[s + 22];
                for (int v = 0; v < 4; v++)
                {
                    int o = q * 4 + v;
                    m.Positions[o * 3] = quads[s + v * 5] / 16f;
                    m.Positions[o * 3 + 1] = quads[s + v * 5 + 1] / 16f;
                    m.Positions[o * 3 + 2] = -quads[s + v * 5 + 2] / 16f;
                    m.Uvs[o * 2] = quads[s + v * 5 + 3];
                    m.Uvs[o * 2 + 1] = 1f - quads[s + v * 5 + 4];
                    m.Normals[o * 3] = nx;
                    m.Normals[o * 3 + 1] = ny;
                    m.Normals[o * 3 + 2] = nz;
                }
                int b = q * 4;
                bool keep = Facing(m.Positions, b, b + 1, b + 2, nx, ny, nz) + Facing(m.Positions, b, b + 2, b + 3, nx, ny, nz) >= 0;
                int[] tri = keep ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 };
                for (int k = 0; k < 6; k++) m.Indices[q * 6 + k] = b + tri[k];
            }
            return m;
        }

        private static float Facing(float[] p, int i0, int i1, int i2, float nx, float ny, float nz)
        {
            float ax = p[i1 * 3] - p[i0 * 3], ay = p[i1 * 3 + 1] - p[i0 * 3 + 1], az = p[i1 * 3 + 2] - p[i0 * 3 + 2];
            float bx = p[i2 * 3] - p[i0 * 3], by = p[i2 * 3 + 1] - p[i0 * 3 + 1], bz = p[i2 * 3 + 2] - p[i0 * 3 + 2];
            return (ay * bz - az * by) * nx + (az * bx - ax * bz) * ny + (ax * by - ay * bx) * nz;
        }
    }
}
