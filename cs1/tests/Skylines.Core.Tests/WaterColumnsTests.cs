using System;
using Skylines.Core.Water;
using Xunit;

namespace Skylines.Core.Tests
{
    public class WaterColumnsTests
    {
        [Fact]
        public void MinDepthConstant()
        {
            Assert.Equal(0.05f, WaterColumns.MinDepth);
        }

        [Fact]
        public void SettleLowersShallowSurfaceToGround()
        {
            float[] s = { 10.02f, 10f, 9f };
            float[] g = { 10f, 10f, 10f };
            WaterColumns.Settle(s, g, 0.05f);
            Assert.Equal(new[] { 10f, 10f, 10f }, s);
        }

        [Fact]
        public void SettleLeavesDeepWaterUntouched()
        {
            float[] s = { 12f, 100.5f };
            float[] g = { 10f, 40f };
            WaterColumns.Settle(s, g, 0.05f);
            Assert.Equal(new[] { 12f, 100.5f }, s);
        }

        [Fact]
        public void SettleKeepsDepthExactlyAtThreshold()
        {
            float[] s = { 4.5f };
            float[] g = { 4f };
            WaterColumns.Settle(s, g, 0.5f);
            Assert.Equal(4.5f, s[0]);
        }

        [Fact]
        public void SettleTreatsNaNSurfaceAsGround()
        {
            float[] s = { float.NaN };
            float[] g = { 7f };
            WaterColumns.Settle(s, g, 0.05f);
            Assert.Equal(7f, s[0]);
        }

        [Theory]
        [InlineData(-0.5, 64, -33)]
        [InlineData(10.9, 64, -22)]
        [InlineData(0.0, 64, -32)]
        [InlineData(-32.0, 64, -64)]
        [InlineData(5.0, 5, 3)]
        public void OriginIsFloorOfFeetMinusHalfSize(double feet, int size, int expected)
        {
            Assert.Equal(expected, WaterColumns.Origin(feet, size));
        }

        [Fact]
        public void SameValuesComparesLengthAndBits()
        {
            Assert.True(WaterColumns.SameValues(new[] { 1f, 2f }, new[] { 1f, 2f }));
            Assert.True(WaterColumns.SameValues(new float[0], new float[0]));
            Assert.False(WaterColumns.SameValues(new[] { 1f, 2f }, new[] { 1f, 2.5f }));
            Assert.False(WaterColumns.SameValues(new[] { 1f }, new[] { 1f, 1f }));
        }

        [Fact]
        public void SameValuesNaNEqualsNaNButNegativeZeroDiffersFromZero()
        {
            Assert.True(WaterColumns.SameValues(new[] { float.NaN }, new[] { float.NaN }));
            Assert.False(WaterColumns.SameValues(new[] { -0f }, new[] { 0f }));
        }
    }
}
