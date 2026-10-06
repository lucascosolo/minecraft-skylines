using MinecraftSkylines.Mod.Underground;
using Xunit;

namespace MinecraftSkylines.Mod.Tests
{
    public class UndergroundModeTests
    {
        [Fact]
        public void CyclesThroughAllModesAndWraps()
        {
            int m = 0;
            for (int i = 1; i < UndergroundMode.Count; i++)
            {
                m = UndergroundMode.Next(m);
                Assert.Equal(i, m);
            }
            Assert.Equal(0, UndergroundMode.Next(m));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(4)]
        public void InvalidModeGivesDefaultOnNext(int mode)
        {
            Assert.False(UndergroundMode.IsValid(mode));
            Assert.Equal(UndergroundMode.Default, UndergroundMode.Next(mode));
        }

        [Fact]
        public void DefaultIsTwo()
        {
            Assert.Equal(2, UndergroundMode.Default);
        }

        [Theory]
        [InlineData(10f, 10f, false)]
        [InlineData(9.6f, 10f, false)]
        [InlineData(9.5f, 10f, false)]
        [InlineData(9.4f, 10f, true)]
        [InlineData(-30f, 10f, true)]
        [InlineData(50f, 10f, false)]
        public void UndergroundIsEyeMoreThanHalfAMetreBelowTerrain(float eyeY, float terrainY, bool expected)
        {
            Assert.Equal(expected, UndergroundMode.IsUnderground(eyeY, terrainY));
        }

        [Theory]
        [InlineData(0, false, false)]
        [InlineData(0, true, false)]
        [InlineData(1, false, true)]
        [InlineData(1, true, true)]
        [InlineData(2, false, false)]
        [InlineData(2, true, true)]
        [InlineData(3, false, false)]
        [InlineData(3, true, false)]
        public void LayerDecision(int mode, bool underground, bool expected)
        {
            Assert.Equal(expected, UndergroundMode.WantsLayer(mode, underground));
        }

        [Theory]
        [InlineData(0, true, false)]
        [InlineData(1, true, false)]
        [InlineData(2, true, false)]
        [InlineData(3, false, false)]
        [InlineData(3, true, true)]
        public void TunnelsVisibleDecision(int mode, bool underground, bool expected)
        {
            Assert.Equal(expected, UndergroundMode.WantsTunnelsVisible(mode, underground));
        }
    }
}
