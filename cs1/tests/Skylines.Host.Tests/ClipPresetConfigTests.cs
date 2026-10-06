using Xunit;

namespace Skylines.Host.Tests
{
    public class ClipPresetConfigTests
    {
        [Fact]
        public void DefaultsToTwo()
        {
            Assert.Equal(1, LaunchConfig.Parse("command = x").ClipPreset);
        }

        [Theory]
        [InlineData("0", 0)]
        [InlineData("1", 1)]
        [InlineData("4", 4)]
        public void ParsesClipPreset(string value, int expected)
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\nclip_preset = " + value);
            Assert.Empty(c.Problems);
            Assert.Equal(expected, c.ClipPreset);
        }

        [Theory]
        [InlineData("5")]
        [InlineData("-1")]
        [InlineData("abc")]
        public void BadClipPresetIsOneProblemAndStaysTheDefault(string value)
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\nclip_preset = " + value);
            Assert.Single(c.Problems);
            Assert.Equal(1, c.ClipPreset);
        }
    }
}
