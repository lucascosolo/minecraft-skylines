namespace MinecraftSkylines.Protocol
{
    /// <summary>A section mesh in CS1 terms: Unity-ready arrays plus the CS position of its origin.</summary>
    public sealed class CsMeshData
    {
        /// <summary>3 per vertex, CS metres relative to the origin.</summary>
        public float[] Positions;
        /// <summary>2 per vertex, Unity convention (v = 0 at the image's bottom row).</summary>
        public float[] Uvs;
        /// <summary>4 per vertex: R, G, B, A.</summary>
        public byte[] Colors;
        /// <summary>Triangle list.</summary>
        public int[] Indices;
        /// <summary>CS position of the section origin.</summary>
        public double OriginX, OriginY, OriginZ;
    }

    /// <summary>
    /// SECTION_MESH to CS1: z mirrored (x, y, -z), winding reversed (the mirror flips handedness; Unity's front
    /// faces are those whose (b-a)x(c-a) points at the viewer), v flipped (the atlas has v = 0 at its top row,
    /// a texture loaded by Unity's LoadImage has the top row at v = 1).
    /// </summary>
    public static class BlockMeshConversion
    {
        /// <summary>Converts one section mesh.</summary>
        public static CsMeshData Convert(SectionMesh m)
        {
            int n = m.VertexCount;
            var d = new CsMeshData
            {
                Positions = new float[3 * n],
                Uvs = new float[2 * n],
                Colors = new byte[4 * n],
                Indices = new int[n],
                OriginX = 16.0 * m.Sx,
                OriginY = 16.0 * m.Sy - MinecraftFrame.YOffset,
                OriginZ = -16.0 * m.Sz,
            };
            byte[] raw = m.VertexData;
            for (int i = 0; i < n; i++)
            {
                d.Positions[3 * i] = m.X(i);
                d.Positions[3 * i + 1] = m.Y(i);
                d.Positions[3 * i + 2] = -m.Z(i);
                d.Uvs[2 * i] = m.U(i);
                d.Uvs[2 * i + 1] = 1f - m.V(i);
                System.Array.Copy(raw, i * SectionMesh.BytesPerVertex + 20, d.Colors, 4 * i, 4);
            }
            for (int t = 0; t < n; t += 3)
            {
                d.Indices[t] = t;
                d.Indices[t + 1] = t + 2;
                d.Indices[t + 2] = t + 1;
            }
            return d;
        }
    }
}
