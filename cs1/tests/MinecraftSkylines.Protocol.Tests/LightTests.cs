using System;
using System.Linq;
using Skylines.Bridge;
using Xunit;

namespace MinecraftSkylines.Protocol.Tests
{
    public class LightTests
    {
        private static LightSource L(int x, int y, int z, byte level)
        {
            return new LightSource { X = x, Y = y, Z = z, Level = level };
        }

        [Fact]
        public void LightSourcesRoundTripAndEveryTruncationThrows()
        {
            byte[] full = new LightSources { Lights = new[] { L(1, 2, -3, 15), L(-4, 5, 6, 1) } }.Encode();
            LightSources m = LightSources.Decode(full);
            Assert.Equal(2, m.Lights.Length);
            Assert.Equal(-3, m.Lights[0].Z);
            Assert.Equal(15, m.Lights[0].Level);
            Assert.Equal(-4, m.Lights[1].X);
            Assert.Equal(full, m.Encode());
            for (int len = 0; len < full.Length; len++)
            {
                byte[] cut = new byte[len];
                Array.Copy(full, cut, len);
                Assert.Throws<ProtocolException>(() => LightSources.Decode(cut));
            }
            Assert.Empty(LightSources.Decode(new LightSources { Lights = new LightSource[0] }.Encode()).Lights);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(16)]
        public void DecodeRejectsLevelOutsideRange(byte level)
        {
            byte[] p = new LightSources { Lights = new[] { L(0, 0, 0, level) } }.Encode();
            Assert.Throws<ProtocolException>(() => LightSources.Decode(p));
        }

        [Fact]
        public void MaxLevelIsFifteen()
        {
            Assert.Equal(15, LightSources.MaxLevel);
        }

        [Theory]
        [InlineData(10f, 2f, 10)]
        [InlineData(10f, 0.5f, 5)]
        [InlineData(0.2f, 1f, 1)]
        [InlineData(40f, 1f, 15)]
        [InlineData(10f, 1f, 10)]
        [InlineData(0f, 1f, 0)]
        [InlineData(5f, 0f, 0)]
        [InlineData(-3f, 1f, 0)]
        [InlineData(float.NaN, 1f, 0)]
        [InlineData(5f, float.NaN, 0)]
        public void FromRangeMapsRangeAndIntensityToLevel(float range, float intensity, int expected)
        {
            Assert.Equal(expected, LightLevels.FromRange(range, intensity));
        }

        [Fact]
        public void MergeDropsLevelZeroKeepsMaxAndSorts()
        {
            LightSource[] got = LightLevels.Merge(new[]
            {
                L(5, 1, 1, 3), L(1, 9, 2, 4), L(1, 2, 2, 7), L(1, 2, 2, 5), L(1, 3, 1, 0), L(1, 1, 2, 2), L(5, 1, 1, 6),
            });
            var keys = got.Select(s => s.X + "," + s.Y + "," + s.Z + ":" + s.Level).ToArray();
            Assert.Equal(new[] { "1,1,2:2", "1,2,2:7", "1,9,2:4", "5,1,1:6" }, keys);
        }

        [Fact]
        public void MergeSortsByXThenZThenY()
        {
            LightSource[] got = LightLevels.Merge(new[] { L(2, 0, 0, 1), L(1, 5, 9, 1), L(1, 6, 3, 1), L(1, 4, 3, 1) });
            Assert.Equal(new[] { "1,4,3", "1,6,3", "1,5,9", "2,0,0" }, got.Select(s => s.X + "," + s.Y + "," + s.Z).ToArray());
        }

        [Fact]
        public void MergeOfNothingIsEmpty()
        {
            Assert.Empty(LightLevels.Merge(new LightSource[0]));
            Assert.Empty(LightLevels.Merge(new[] { L(1, 1, 1, 0) }));
        }
    }
}
