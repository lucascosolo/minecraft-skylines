using Skylines.Host.Overlay;
using Xunit;

namespace Skylines.Host.Tests
{
    public class OverlayModeTests
    {
        [Fact]
        public void CyclesThroughAllModesAndWraps()
        {
            int m = 0;
            for (int i = 1; i < OverlayMode.Count; i++)
            {
                m = OverlayMode.Next(m);
                Assert.Equal(i, m);
            }
            Assert.Equal(0, OverlayMode.Next(m));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(5)]
        public void InvalidModeIsNotValidAndNextGivesDefault(int mode)
        {
            Assert.False(OverlayMode.IsValid(mode));
            Assert.Equal(OverlayMode.Default, OverlayMode.Next(mode));
        }

        [Theory]
        [InlineData(0, true, false, 0.5f)]
        [InlineData(1, true, false, 1f)]
        [InlineData(2, false, false, 0.5f)]
        [InlineData(3, true, true, 0.5f)]
        [InlineData(4, false, true, 0.5f)]
        public void ModeTable(int mode, bool shader, bool linear, float grey)
        {
            Assert.Equal(shader, OverlayMode.UsesPremultiplyShader(mode));
            Assert.Equal(linear, OverlayMode.LinearTexture(mode));
            Assert.Equal(grey, OverlayMode.DrawColorGrey(mode));
            Assert.DoesNotContain("invalid", OverlayMode.Describe(mode));
        }

        [Fact]
        public void DefaultIsZero()
        {
            Assert.Equal(0, OverlayMode.Default);
        }
    }
}
