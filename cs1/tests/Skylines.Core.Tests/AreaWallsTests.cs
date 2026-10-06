using System;
using Skylines.Core.Geometry;
using Xunit;

namespace Skylines.Core.Tests
{
    public class AreaWallsTests
    {
        private const ushort F = 1 << 8;
        // CS1's layout: 1920 m tiles with borders at 960 + k * 1920.
        private const float Origin = 960f, Size = 1920f;

        // Only the centre tile (-960..960 on both axes) is playable.
        private static bool CentreOnly(float x, float z)
        {
            return Math.Abs(x) > 960f || Math.Abs(z) > 960f;
        }

        private static float[] Extents(TriangleBuffer b)
        {
            var e = new[] { float.MaxValue, float.MaxValue, float.MaxValue, float.MinValue, float.MinValue, float.MinValue };
            for (int i = 0; i < b.Count * 3; i++)
                for (int k = 0; k < 3; k++)
                {
                    e[k] = Math.Min(e[k], b.Positions[i * 3 + k]);
                    e[3 + k] = Math.Max(e[3 + k], b.Positions[i * 3 + k]);
                }
            return e;
        }

        [Fact]
        public void RegionAwayFromBordersHasNoWalls()
        {
            var b = new TriangleBuffer();
            Assert.Equal(0, AreaWalls.Emit(CentreOnly, Origin, Size, 0, 0, 16, 16, 0.05f, -100, 2000, F, b));
            Assert.Equal(0, b.Count);
        }

        [Fact]
        public void RegionOnTheEastBorderGetsOneClippedWall()
        {
            var b = new TriangleBuffer();
            // Region 944..960 x 0..16 touches the border x = 960 at its east edge.
            Assert.Equal(1, AreaWalls.Emit(CentreOnly, Origin, Size, 944, 0, 960, 16, 0.05f, -100, 2000, F, b));
            float[] e = Extents(b);
            Assert.Equal(959.9f, e[0], 3); Assert.Equal(960.05f, e[3], 3);
            Assert.Equal(-0.05f, e[2], 3); Assert.Equal(16.05f, e[5], 3);
            Assert.Equal(-100f, e[1], 3); Assert.Equal(2000f, e[4], 3);
            for (int t = 0; t < b.Count; t++) Assert.Equal(F, b.Flags[t]);
        }

        [Fact]
        public void BorderBetweenTwoPlayableTilesHasNoWall()
        {
            var b = new TriangleBuffer();
            Func<float, float, bool> twoTiles = (x, z) => x < -960f || x > 2880f || Math.Abs(z) > 960f;
            Assert.Equal(0, AreaWalls.Emit(twoTiles, Origin, Size, 952, 0, 968, 16, 0.05f, -100, 2000, F, b));
        }

        [Fact]
        public void CornerRegionGetsBothWallsSplitAtTheCorner()
        {
            var b = new TriangleBuffer();
            // Region straddling the north-east corner (960, 960): walls along x = 960 for z < 960 and along z = 960 for x < 960.
            Assert.Equal(2, AreaWalls.Emit(CentreOnly, Origin, Size, 952, 952, 968, 968, 0f, 0, 10, F, b));
            float[] e = Extents(b);
            Assert.Equal(952f, e[0], 3); Assert.Equal(960.1f, e[3], 3);
            Assert.Equal(952f, e[2], 3); Assert.Equal(960.1f, e[5], 3);
        }

        [Fact]
        public void MapEdgeIsAWallWhenEverythingBeyondIsOutside()
        {
            var b = new TriangleBuffer();
            Func<float, float, bool> wholeMap = (x, z) => Math.Abs(x) > 8640f || Math.Abs(z) > 8640f;
            Assert.Equal(1, AreaWalls.Emit(wholeMap, Origin, Size, 8624, 0, 8640, 16, 0.05f, 0, 10, F, b));
        }
    }
}
