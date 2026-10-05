using MinecraftSkylines.Mod;
using Xunit;

namespace MinecraftSkylines.Mod.Tests
{
    public class ScreenInputTests
    {
        private static void Check(float ux, float uy, int w, int h, int ex, int ey)
        {
            int x, y;
            ScreenInput.ToHostPixels(ux, uy, w, h, out x, out y);
            Assert.Equal(ex, x);
            Assert.Equal(ey, y);
        }

        [Fact]
        public void BottomLeftMapsToLastRow() { Check(0f, 0f, 1920, 1080, 0, 1079); }

        [Fact]
        public void TopRightMapsToFirstRow() { Check(1919.5f, 1079.5f, 1920, 1080, 1919, 0); }

        [Fact]
        public void FractionalPositionsFloor() { Check(10.9f, 20.9f, 100, 100, 10, 79); }

        [Fact]
        public void OutsideTheWindowIsClamped()
        {
            Check(-5f, -5f, 1920, 1080, 0, 1079);
            Check(5000f, 5000f, 1920, 1080, 1919, 0);
            Check(1920f, 1080f, 1920, 1080, 1919, 0);
        }

        [Fact]
        public void EscapeWithoutScreenExitsMode()
        {
            var r = new EscapeRouter();
            Assert.Equal(EscapeRoute.ExitMode, r.Down(false));
            Assert.False(r.Holding);
            Assert.False(r.Up());
        }

        [Fact]
        public void EscapeWithScreenGoesToGuestAndReleaseIsForwardedOnce()
        {
            var r = new EscapeRouter();
            Assert.Equal(EscapeRoute.ToGuest, r.Down(true));
            Assert.True(r.Holding);
            Assert.True(r.Up());
            Assert.False(r.Holding);
            Assert.False(r.Up());
        }

        [Fact]
        public void ReleaseIsForwardedEvenIfScreenClosedMeanwhile()
        {
            var r = new EscapeRouter();
            r.Down(true);
            Assert.True(r.Up());
        }

        [Fact]
        public void ResetDropsHeldPress()
        {
            var r = new EscapeRouter();
            r.Down(true);
            r.Reset();
            Assert.False(r.Holding);
            Assert.False(r.Up());
        }
    }
}
