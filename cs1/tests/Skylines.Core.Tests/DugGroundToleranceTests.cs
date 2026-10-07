using System.Linq;
using Skylines.Core.Voxels;
using Xunit;

namespace Skylines.Core.Tests
{
    public class DugGroundToleranceTests
    {
        private static DugGround Flat(float surface) { return new DugGround((x, z) => surface); }

        // Host samples float, guest samples double: a surface a hair above an integer + 0.5.
        private const float HairAbove = 221.5f + 0.0004f;

        [Fact]
        public void HairAboveBoundaryTakesLowerTopButStaticRuleIsUnchanged()
        {
            Assert.Equal(220, Flat(HairAbove).TopOf(0, 0));
            Assert.Equal(221, DugGround.SolidTop(HairAbove));
        }

        [Fact]
        public void CaveAirAtGuestTopPlusOneIsNotDugGround()
        {
            DugGround g = Flat(HairAbove);
            g.Set(0, 221, 0, true, true);
            Assert.False(g.IsDug(0, 221, 0));
            Assert.False(g.IsOpen(0, 0));
            Assert.True(g.IsBare(0, 0));
            Assert.Empty(g.OpenColumns(-5, -5, 5, 5));
            Assert.Equal(0, g.DugCount);
        }

        [Theory]
        [InlineData(221.5f, 220)]
        [InlineData(221.41f, 220)]
        [InlineData(221.6f, 221)]
        public void TopOfAwayFromTheToleranceBand(float surface, int expected)
        {
            Assert.Equal(expected, Flat(surface).TopOf(0, 0));
        }

        [Fact]
        public void SurfaceBeyondToleranceTakesUpperTopAndCaveAirOpensColumn()
        {
            DugGround g = Flat(221.5f + 0.01f);
            Assert.Equal(221, g.TopOf(0, 0));
            g.Set(0, 221, 0, true, true);
            Assert.True(g.IsOpen(0, 0));
        }

        [Fact]
        public void SurfaceToleranceIsSmallAndPositive()
        {
            Assert.InRange(DugGround.SurfaceTolerance, 0.00001f, 0.00999f);
        }
    }
}
