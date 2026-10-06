using System;

namespace Skylines.Core.Geometry
{
    /// <summary>Collision boxes for trees, bushes and props, from their generated mesh bounds.</summary>
    public static class Obstacle
    {
        /// <summary>Trees and props taller than this (metres, scaled) are reduced to a post or trunk where noted.</summary>
        public const float TallHeight = 2.5f;
        /// <summary>Side of a trunk or post box, metres.</summary>
        public const float PostWidth = 0.6f;
        /// <summary>A tall tree's trunk reaches this fraction of its height.</summary>
        public const float TrunkFraction = 0.4f;
        /// <summary>Props flatter than this (metres, scaled) are skipped.</summary>
        public const float MinPropHeight = 0.1f;
        /// <summary>How far below its base a box reaches, so it does not float on slopes, metres.</summary>
        public const float Sink = 0.5f;
        /// <summary>The bottom slice of a prop mesh that counts as its base, as a fraction of the mesh height.</summary>
        public const float BaseBand = 0.15f;
        /// <summary>A tall prop collides only at its base when the base covers at most this fraction of its bounds' footprint.</summary>
        public const float SlenderBaseFraction = 0.5f;

        /// <summary>A prop mesh's horizontal extent within its base slice, in mesh-local units.</summary>
        public struct Footprint
        {
            public float MinX, MaxX, MinZ, MaxZ;
        }

        /// <summary>
        /// The horizontal extent of the vertices in the bottom <see cref="BaseBand"/> of a mesh (interleaved x, y, z;
        /// <paramref name="vertexCount"/> vertices). A street light's base is its pole, a bench's its legs' spread.
        /// False when there are no vertices.
        /// </summary>
        public static bool BaseFootprint(float[] xyz, int vertexCount, out Footprint f)
        {
            f = new Footprint();
            if (xyz == null || vertexCount <= 0 || xyz.Length < vertexCount * 3) return false;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < vertexCount; i++)
            {
                float y = xyz[i * 3 + 1];
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            float band = minY + BaseBand * (maxY - minY);
            f.MinX = f.MinZ = float.MaxValue;
            f.MaxX = f.MaxZ = float.MinValue;
            for (int i = 0; i < vertexCount; i++)
            {
                if (xyz[i * 3 + 1] > band) continue;
                float x = xyz[i * 3], z = xyz[i * 3 + 2];
                if (x < f.MinX) f.MinX = x;
                if (x > f.MaxX) f.MaxX = x;
                if (z < f.MinZ) f.MinZ = z;
                if (z > f.MaxZ) f.MaxZ = z;
            }
            return true;
        }

        /// <summary>Appends a tree's or bush's box; returns the triangles added (12, or 0 when its height is not positive).</summary>
        public static int Tree(float x, float y, float z, float sizeX, float sizeY, float sizeZ, float scale, ushort flags, TriangleBuffer into)
        {
            float h = sizeY * scale;
            if (!(h > 0f)) return 0;
            if (h > TallHeight) Box.Oriented(x, z, 0f, PostWidth / 2, PostWidth / 2, y - Sink, y + TrunkFraction * h, flags, into);
            else Box.Oriented(x, z, 0f, sizeX * scale / 2, sizeZ * scale / 2, y - Sink, y + h, flags, into);
            return 12;
        }

        /// <summary>Appends a prop's box; returns the triangles added (12, or 0 when skipped as flat).</summary>
        public static int Prop(float x, float y, float z, float angle, float centerX, float centerY, float centerZ, float sizeX, float sizeY, float sizeZ, float scale, ushort flags, TriangleBuffer into)
        {
            return Prop(x, y, z, angle, centerX, centerY, centerZ, sizeX, sizeY, sizeZ, scale, null, flags, into);
        }

        /// <summary>
        /// As above, with the mesh's <see cref="BaseFootprint"/> when known: a tall prop whose base covers at most
        /// <see cref="SlenderBaseFraction"/> of its bounds' footprint (a street light: pole and arm) collides only as its
        /// base extruded to its full height, at least <see cref="PostWidth"/> wide. Without a footprint, a tall prop whose
        /// pivot is off its bounds' centre keeps only a post at the pivot.
        /// </summary>
        public static int Prop(float x, float y, float z, float angle, float centerX, float centerY, float centerZ, float sizeX, float sizeY, float sizeZ, float scale, Footprint? baseFootprint, ushort flags, TriangleBuffer into)
        {
            float meshBottom = y + (centerY - sizeY / 2) * scale, top = y + (centerY + sizeY / 2) * scale;
            if (!(sizeY * scale >= MinPropHeight) || !(top - y >= MinPropHeight)) return 0;
            float hx = sizeX * scale / 2, hz = sizeZ * scale / 2, lcx = centerX * scale, lcz = centerZ * scale;
            float bottom = meshBottom - y > Sink ? meshBottom : Math.Min(meshBottom, y - Sink);
            float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
            if (top - y > TallHeight && baseFootprint.HasValue)
            {
                Footprint b = baseFootprint.Value;
                float bw = (b.MaxX - b.MinX) * scale, bd = (b.MaxZ - b.MinZ) * scale;
                if (bw >= 0f && bd >= 0f && bw * bd <= SlenderBaseFraction * (2 * hx) * (2 * hz))
                {
                    float bcx = (b.MinX + b.MaxX) / 2 * scale, bcz = (b.MinZ + b.MaxZ) / 2 * scale;
                    Box.Oriented(x + c * bcx - s * bcz, z + s * bcx + c * bcz, angle, Math.Max(bw / 2, PostWidth / 2), Math.Max(bd / 2, PostWidth / 2), bottom, top, flags, into);
                    return 12;
                }
            }
            // A tall prop whose pivot is off its bounds' centre (a street light's arm over the road) keeps only its post.
            else if (top - y > TallHeight && (Math.Abs(lcx) > hx / 2 || Math.Abs(lcz) > hz / 2))
            {
                Box.Oriented(x, z, angle, PostWidth / 2, PostWidth / 2, bottom, top, flags, into);
                return 12;
            }
            Box.Oriented(x + c * lcx - s * lcz, z + s * lcx + c * lcz, angle, hx, hz, bottom, top, flags, into);
            return 12;
        }
    }
}
