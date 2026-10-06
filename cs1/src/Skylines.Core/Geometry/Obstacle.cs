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
            float meshBottom = y + (centerY - sizeY / 2) * scale, top = y + (centerY + sizeY / 2) * scale;
            if (!(sizeY * scale >= MinPropHeight) || !(top - y >= MinPropHeight)) return 0;
            float hx = sizeX * scale / 2, hz = sizeZ * scale / 2, lcx = centerX * scale, lcz = centerZ * scale;
            float bottom = meshBottom - y > Sink ? meshBottom : Math.Min(meshBottom, y - Sink);
            // A tall prop whose pivot is off its bounds' centre (a street light's arm over the road) keeps only its post.
            if (top - y > TallHeight && (Math.Abs(lcx) > hx / 2 || Math.Abs(lcz) > hz / 2))
            {
                Box.Oriented(x, z, angle, PostWidth / 2, PostWidth / 2, bottom, top, flags, into);
                return 12;
            }
            float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
            Box.Oriented(x + c * lcx - s * lcz, z + s * lcx + c * lcz, angle, hx, hz, bottom, top, flags, into);
            return 12;
        }
    }
}
