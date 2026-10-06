using MinecraftSkylines.Mod.Render;
using Xunit;

namespace MinecraftSkylines.Mod.Tests
{
    public class ClipPresetTests
    {
        [Fact]
        public void CyclesThroughAllPresetsAndWraps()
        {
            int p = 0;
            for (int i = 1; i < ClipPreset.Count; i++)
            {
                p = ClipPreset.Next(p);
                Assert.Equal(i, p);
            }
            Assert.Equal(0, ClipPreset.Next(p));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(5)]
        public void InvalidPresetGivesDefaultOnNext(int preset)
        {
            Assert.False(ClipPreset.IsValid(preset));
            Assert.Equal(ClipPreset.Default, ClipPreset.Next(preset));
        }

        [Fact]
        public void DefaultIsOne()
        {
            Assert.Equal(1, ClipPreset.Default);
        }

        [Theory]
        [InlineData(0, 0.1f, 9000f)]
        [InlineData(1, 0.15f, 4000f)]
        [InlineData(2, 0.25f, 3000f)]
        [InlineData(3, 0.4f, 2000f)]
        [InlineData(4, 0.6f, 1500f)]
        public void PresetValues(int preset, float near, float far)
        {
            Assert.Equal(near, ClipPreset.Near(preset));
            Assert.Equal(far, ClipPreset.Far(preset, 9000f));
        }

        [Fact]
        public void DescribeNamesNearAndFar()
        {
            Assert.Equal("clip: near 0.25 m, far 3000 m", ClipPreset.Describe(2, 9000f));
            Assert.Equal("clip: near 0.1 m, far 9000 m", ClipPreset.Describe(0, 9000f));
        }

        [Theory]
        [InlineData(float.PositiveInfinity, 0.15f, 0.15f)]
        [InlineData(1f, 0.15f, 0.15f)]
        [InlineData(0.9f, 0.15f, 0.15f)]
        [InlineData(0.2f, 0.15f, 0.12f)]
        [InlineData(0.01f, 0.15f, 0.05f)]
        [InlineData(0f, 0.15f, 0.05f)]
        [InlineData(0.5f, 0.6f, 0.3f)]
        public void DynamicNearIsSixTenthsOfTheDistanceClamped(float distance, float presetNear, float expected)
        {
            Assert.Equal(expected, ClipPreset.DynamicNear(distance, presetNear), 5);
        }
    }
}
