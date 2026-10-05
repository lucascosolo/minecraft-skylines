using Skylines.Core.Geometry;

namespace MinecraftSkylines.Protocol
{
    /// <summary>Turns triangles in CS1 coordinates into a <see cref="CollisionRegion"/> in Minecraft coordinates.</summary>
    public static class CollisionConversion
    {
        /// <summary>
        /// Converts every vertex from CS1 to Minecraft (x, y, -z) in place. Mirroring z flips every triangle's
        /// orientation, so the winding is reversed to keep each outward normal pointing the same way (up stays up).
        /// </summary>
        public static void CsToMcInPlace(TriangleBuffer triangles)
        {
            float[] p = triangles.Positions;
            int n = triangles.Count * 9;
            for (int i = 2; i < n; i += 3) p[i] = -p[i];
            triangles.ReverseWinding();
        }

        /// <summary>Converts <paramref name="csTriangles"/> in place (see <see cref="CsToMcInPlace"/>) and copies it into a region message.</summary>
        public static CollisionRegion ToRegion(TriangleBuffer csTriangles, uint epoch, int regionX, int regionZ)
        {
            CsToMcInPlace(csTriangles);
            int n = csTriangles.Count;
            var region = new CollisionRegion { Epoch = epoch, RegionX = regionX, RegionZ = regionZ, Vertices = new float[9 * n], Flags = new ushort[n] };
            System.Array.Copy(csTriangles.Positions, region.Vertices, 9 * n);
            System.Array.Copy(csTriangles.Flags, region.Flags, n);
            return region;
        }
    }
}
