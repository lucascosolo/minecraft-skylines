using Xunit;

namespace Skylines.Host.Tests
{
    public class UndergroundConfigTests
    {
        [Fact]
        public void DefaultsToTwo()
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\n");
            Assert.Equal(2, c.UndergroundMode);
            Assert.Empty(c.Problems);
        }

        [Theory]
        [InlineData("0", 0)]
        [InlineData(" 3 ", 3)]
        public void ParsesUndergroundMode(string value, int expected)
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\nunderground_mode = " + value + "\n");
            Assert.Empty(c.Problems);
            Assert.Equal(expected, c.UndergroundMode);
        }

        [Theory]
        [InlineData("4")]
        [InlineData("-1")]
        [InlineData("x")]
        public void BadUndergroundModeIsOneProblemAndStaysTheDefault(string value)
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\nunderground_mode = " + value + "\n");
            Assert.Single(c.Problems);
            Assert.Equal(2, c.UndergroundMode);
        }
    }
}
