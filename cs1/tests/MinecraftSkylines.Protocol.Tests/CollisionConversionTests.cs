using MinecraftSkylines.Protocol;
using Skylines.Core.Geometry;
using Xunit;

namespace MinecraftSkylines.Protocol.Tests
{
    public class CollisionConversionTests
    {
        private static float[] Normal(float[] p, int tri)
        {
            int o = tri * 9;
            float ux = p[o + 3] - p[o], uy = p[o + 4] - p[o + 1], uz = p[o + 5] - p[o + 2];
            float vx = p[o + 6] - p[o], vy = p[o + 7] - p[o + 1], vz = p[o + 8] - p[o + 2];
            return new[] { uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx };
        }

        private static TriangleBuffer Terrain()
        {
            var b = new TriangleBuffer();
            Heightfield.Triangulate((x, z) => 0.3f * x - 0.2f * z, 0, -16, 16, 0, 2, CollisionRegion.Terrain, b);
            return b;
        }

        [Fact]
        public void UpNormalsStayUpAfterTheZMirror()
        {
            var b = Terrain();
            Assert.Equal(128, b.Count);
            for (int i = 0; i < b.Count; i++) Assert.True(Normal(b.Positions, i)[1] > 0);

            CollisionConversion.CsToMcInPlace(b);

            for (int i = 0; i < b.Count; i++) Assert.True(Normal(b.Positions, i)[1] > 0, "triangle " + i);
        }

        [Fact]
        public void MirroringWithoutReversingWindingWouldFlipThem()
        {
            var b = Terrain();
            for (int i = 2; i < b.Count * 9; i += 3) b.Positions[i] = -b.Positions[i];
            for (int i = 0; i < b.Count; i++) Assert.True(Normal(b.Positions, i)[1] < 0);
        }

        [Fact]
        public void VerticesBecomeXYMinusZAndRegionRoundTrips()
        {
            var b = new TriangleBuffer();
            b.Add(1, 2, -3, 4, 5, -6, 7, 8, -9, CollisionRegion.RoadSurface);
            CollisionRegion r = CollisionRegion.Decode(CollisionConversion.ToRegion(b, 7, -1, 0).Encode());
            Assert.Equal(7u, r.Epoch);
            Assert.Equal(-1, r.RegionX);
            Assert.Equal(0, r.RegionZ);
            Assert.Equal(new float[] { 1, 2, 3, 7, 8, 9, 4, 5, 6 }, r.Vertices);
            Assert.Equal(new[] { CollisionRegion.RoadSurface }, r.Flags);
        }
    }
}
