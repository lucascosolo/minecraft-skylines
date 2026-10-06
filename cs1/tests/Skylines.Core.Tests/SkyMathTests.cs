using System;
using Skylines.Core.Sky;
using Xunit;

namespace Skylines.Core.Tests
{
    public class SkyMathTests
    {
        private const float Tol = 1e-5f;

        private static void Near(double expected, double actual)
        {
            Assert.InRange(actual, expected - Tol, expected + Tol);
        }

        [Fact]
        public void Constants()
        {
            Assert.Equal(12f, SkyMath.CloudCellBlocks);
            Assert.Equal(3.96f, SkyMath.CloudZShift);
            Assert.Equal(5.0, SkyMath.StaleSeconds);
            Assert.Equal(10, SkyMath.Slots);
        }

        [Theory]
        [InlineData(-0.5f, 0f)]
        [InlineData(0.5f, 0.5f)]
        [InlineData(2f, 1f)]
        [InlineData(float.NaN, 0f)]
        public void Clamp01Clamps(float x, float expected)
        {
            Near(expected, SkyMath.Clamp01(x));
        }

        [Theory]
        [InlineData(-0.3f, 0f)]
        [InlineData(0f, 0f)]
        [InlineData(0.25f, 0.5f)]
        [InlineData(0.5f, 1f)]
        [InlineData(1f, 1f)]
        public void HorizonBlendSmoothsstep(float sinE, float expected)
        {
            Near(expected, SkyMath.HorizonBlend(sinE));
        }

        [Theory]
        [InlineData(0f, 1f, 1f)]
        [InlineData(0.3f, 1f, 0.5f)]
        [InlineData(0f, 0.5f, 0.25f)]
        [InlineData(0f, -1f, 0f)]
        [InlineData(0.6f, 1f, 0f)]
        [InlineData(-0.3f, 1f, 0.5f)]
        [InlineData(0f, float.NaN, 0f)]
        public void GlowWeight(float sinE, float cosAz, float expected)
        {
            Near(expected, SkyMath.GlowWeight(sinE, cosAz));
        }

        [Fact]
        public void DomeColourAtZenithIsSky()
        {
            float[] o = new float[3];
            SkyMath.DomeColour(1f, 1f, new[] { 0.2f, 0.4f, 0.6f }, new[] { 0.8f, 0.8f, 0.8f }, new[] { 1f, 0f, 0f, 1f }, o);
            Near(0.2, o[0]); Near(0.4, o[1]); Near(0.6, o[2]);
        }

        [Fact]
        public void DomeColourAtHorizonFacingSunIsSunrise()
        {
            float[] o = new float[3];
            SkyMath.DomeColour(0f, 1f, new[] { 0.2f, 0.4f, 0.6f }, new[] { 0.8f, 0.8f, 0.8f }, new[] { 1f, 0.5f, 0.25f, 1f }, o);
            Near(1.0, o[0]); Near(0.5, o[1]); Near(0.25, o[2]);
        }

        [Fact]
        public void DomeColourWithZeroSunriseAlphaIsFogAtHorizon()
        {
            float[] o = new float[3];
            SkyMath.DomeColour(0f, 1f, new[] { 0.2f, 0.4f, 0.6f }, new[] { 0.8f, 0.7f, 0.6f }, new[] { 1f, 1f, 1f, 0f }, o);
            Near(0.8, o[0]); Near(0.7, o[1]); Near(0.6, o[2]);
        }

        [Fact]
        public void DomeColourBlendsHalfwayAndPartialGlow()
        {
            float[] o = new float[3];
            // sinE 0.25 -> h 0.5: base = fog + (sky - fog) * 0.5; glow w = 1 * 1 * (1 - 0.25/0.6)
            float[] sky = { 0.2f, 0.4f, 0.6f }, fog = { 0.8f, 0.6f, 0.2f };
            float[] sun = { 1f, 0f, 0.5f, 0.5f };
            SkyMath.DomeColour(0.25f, 1f, sky, fog, sun, o);
            double w = 0.5 * (1 - 0.25 / 0.6);
            for (int i = 0; i < 3; i++)
            {
                double b = fog[i] + (sky[i] - fog[i]) * 0.5;
                Near(b + (sun[i] - b) * w, o[i]);
            }
        }

        [Theory]
        [InlineData(0f, 1f)]
        [InlineData(0.25f, 0.75f)]
        [InlineData(1f, 0f)]
        [InlineData(2f, 0f)]
        [InlineData(float.NaN, 1f)]
        public void CelestialAlpha(float rain, float expected)
        {
            Near(expected, SkyMath.CelestialAlpha(rain));
        }

        [Fact]
        public void StarAlphaMultipliesBrightnessAndClearWeather()
        {
            Near(0.25, SkyMath.StarAlpha(0.5f, 0.5f));
            Near(1.0, SkyMath.StarAlpha(3f, 0f));
            Near(0.0, SkyMath.StarAlpha(0.8f, 1f));
            Near(0.0, SkyMath.StarAlpha(-1f, 0f));
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(0, 5, 0)]
        [InlineData(1, 0, 1)]
        [InlineData(1, 7, 8)]
        [InlineData(1, 8, -1)]
        [InlineData(2, 0, 9)]
        [InlineData(3, 0, -1)]
        [InlineData(255, 0, -1)]
        public void TextureSlot(byte kind, byte phase, int expected)
        {
            Assert.Equal(expected, SkyMath.TextureSlot(kind, phase));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(7, 8)]
        [InlineData(8, 1)]
        [InlineData(-1, 8)]
        [InlineData(-8, 1)]
        public void MoonSlotWraps(int phase, int expected)
        {
            Assert.Equal(expected, SkyMath.MoonSlot(phase));
        }

        [Theory]
        [InlineData(0.0, 0.0, 0.0, 0, 0)]
        [InlineData(11.9, 0.0, 0.0, 0, 0)]
        [InlineData(12.0, 0.0, 0.0, 1, 0)]
        [InlineData(-0.1, 0.0, 0.0, 15, 0)]
        [InlineData(-12.1, 0.0, 0.0, 14, 0)]
        [InlineData(0.0, 8.03, 0.0, 0, 0)]
        [InlineData(0.0, 8.05, 0.0, 0, 1)]
        [InlineData(0.0, -3.97, 0.0, 0, 15)]
        [InlineData(0.0, -3.95, 0.0, 0, 0)]
        [InlineData(0.0, 0.0, 12.0, 1, 0)]
        [InlineData(190.0, 0.0, 0.0, 15, 0)]
        [InlineData(192.0, 0.0, 0.0, 0, 0)]
        public void CloudTexelWrapsAndFloors(double x, double z, double offset, int column, int row)
        {
            int c, r;
            SkyMath.CloudTexel(x, z, offset, 16, 16, out c, out r);
            Assert.Equal(column, c);
            Assert.Equal(row, r);
        }

        [Fact]
        public void CloudTexelStaysInRangeForNegativeCoordinatesAndNonSquareTextures()
        {
            for (double x = -1000; x < 1000; x += 37.3)
                for (double z = -1000; z < 1000; z += 41.7)
                {
                    int c, r;
                    SkyMath.CloudTexel(x, z, -5.5, 256, 128, out c, out r);
                    Assert.InRange(c, 0, 255);
                    Assert.InRange(r, 0, 127);
                }
        }

        [Fact]
        public void CloudUvKnownValues()
        {
            float u, v;
            SkyMath.CloudUv(48.0, 0.0, 0.0, 16, 16, out u, out v);
            Near(48.0 / 192.0, u);
            Near(1 - 3.96 / 192.0, v);
            SkyMath.CloudUv(0.0, 0.0, 192.0, 16, 16, out u, out v);
            Near(0.0, u);
        }

        [Fact]
        public void CloudUvRangeAndConsistencyWithTexel()
        {
            const int w = 16, h = 8;
            for (double x = -500.3; x < 500; x += 17.9)
                for (double z = -500.1; z < 500; z += 19.3)
                {
                    float u, v; int c, r;
                    SkyMath.CloudUv(x, z, 3.7, w, h, out u, out v);
                    SkyMath.CloudTexel(x, z, 3.7, w, h, out c, out r);
                    Assert.InRange(u, 0f, 0.99999994f);
                    Assert.InRange(v, 1e-9f, 1f);
                    // skip samples within 0.05 texel of a boundary (float rounding)
                    double fu = u * w, fv = (1 - v) * h;
                    if (Math.Abs(fu - Math.Round(fu)) < 0.05 || Math.Abs(fv - Math.Round(fv)) < 0.05) continue;
                    Assert.Equal(c, (int)Math.Floor(fu));
                    Assert.Equal(r, (int)Math.Floor(fv));
                }
        }

        [Theory]
        [InlineData(10.0, 2.0, 3.0, 16.0)]
        [InlineData(10.0, 2.0, 0.0, 10.0)]
        [InlineData(10.0, 2.0, -4.0, 10.0)]
        [InlineData(10.0, 2.0, 5.0, 20.0)]
        [InlineData(10.0, 2.0, 100.0, 20.0)]
        public void CloudOffsetAtClampsAge(double offset, double speed, double age, double expected)
        {
            Near(expected, SkyMath.CloudOffsetAt(offset, speed, age));
        }

        [Theory]
        [InlineData(0.0, true)]
        [InlineData(4.99, true)]
        [InlineData(5.0, false)]
        [InlineData(100.0, false)]
        [InlineData(-0.1, false)]
        public void IsFresh(double age, bool expected)
        {
            Assert.Equal(expected, SkyMath.IsFresh(age));
        }

        [Fact]
        public void StarsAreUnitVectorsWithSizesInRange()
        {
            const int n = 1500;
            float[] d = new float[3 * n], s = new float[n];
            SkyMath.Stars(n, 10842u, d, s);
            double sx = 0, sy = 0, sz = 0;
            for (int i = 0; i < n; i++)
            {
                float x = d[3 * i], y = d[3 * i + 1], z = d[3 * i + 2];
                Assert.False(float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) || float.IsNaN(s[i]));
                Assert.InRange(Math.Sqrt(x * x + y * y + z * z), 1 - 1e-4, 1 + 1e-4);
                Assert.InRange(s[i], 0.0015f, 0.0025f);
                sx += x; sy += y; sz += z;
            }
            Assert.True(Math.Sqrt(sx * sx + sy * sy + sz * sz) / n < 0.1);
        }

        [Fact]
        public void StarsAreDeterministicPerSeed()
        {
            float[] d1 = new float[30], s1 = new float[10], d2 = new float[30], s2 = new float[10], d3 = new float[30], s3 = new float[10];
            SkyMath.Stars(10, 7u, d1, s1);
            SkyMath.Stars(10, 7u, d2, s2);
            SkyMath.Stars(10, 8u, d3, s3);
            Assert.Equal(d1, d2);
            Assert.Equal(s1, s2);
            Assert.NotEqual(d1, d3);
        }

        [Fact]
        public void StarsRejectTooSmallBuffers()
        {
            Assert.Throws<ArgumentException>(() => SkyMath.Stars(10, 1u, new float[29], new float[10]));
            Assert.Throws<ArgumentException>(() => SkyMath.Stars(10, 1u, new float[30], new float[9]));
        }

        [Fact]
        public void FrameOverrideApplyReadsOnceAndRestoreWritesOnce()
        {
            var fo = new FrameOverride<int>();
            Assert.False(fo.Saved);
            int reads = 0, current = 5;
            Func<int> read = () => { reads++; return current; };
            Action<int> write = v => current = v;
            fo.Apply(read, write, 7);
            Assert.True(fo.Saved);
            Assert.Equal(5, fo.SavedValue);
            Assert.Equal(7, current);
            fo.Apply(read, write, 9);
            Assert.Equal(1, reads);
            Assert.Equal(5, fo.SavedValue);
            Assert.Equal(9, current);

            int writes = 0;
            Action<int> countWrite = v => { writes++; current = v; };
            Assert.True(fo.Restore(countWrite));
            Assert.Equal(5, current);
            Assert.False(fo.Saved);
            Assert.False(fo.Restore(countWrite));
            Assert.Equal(1, writes);
        }

        [Fact]
        public void FrameOverrideRestoreWithoutApplyDoesNotWrite()
        {
            var fo = new FrameOverride<string>();
            bool wrote = false;
            Assert.False(fo.Restore(v => wrote = true));
            Assert.False(wrote);
        }

        [Fact]
        public void FrameOverrideApplyAfterRestoreReadsAgain()
        {
            var fo = new FrameOverride<int>();
            int reads = 0, current = 1;
            Func<int> read = () => { reads++; return current; };
            Action<int> write = v => current = v;
            fo.Apply(read, write, 2);
            fo.Restore(write);
            current = 3;
            fo.Apply(read, write, 4);
            Assert.Equal(2, reads);
            Assert.Equal(3, fo.SavedValue);
        }
    }
}
