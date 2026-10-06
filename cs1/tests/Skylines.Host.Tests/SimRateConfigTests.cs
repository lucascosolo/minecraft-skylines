using Xunit;

namespace Skylines.Host.Tests
{
    public class SimRateConfigTests
    {
        [Fact]
        public void DefaultsToFourTenths()
        {
            Assert.Equal(0.4f, LaunchConfig.Parse("command = x").FirstPersonSimRate);
        }

        [Theory]
        [InlineData("0.1", 0.1f)]
        [InlineData("0.55", 0.55f)]
        [InlineData("1", 1f)]
        public void ParsesRate(string value, float expected)
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\nfirst_person_sim_rate = " + value);
            Assert.Empty(c.Problems);
            Assert.Equal(expected, c.FirstPersonSimRate);
        }

        [Theory]
        [InlineData("0.05")]
        [InlineData("1.5")]
        [InlineData("0,5")]
        [InlineData("fast")]
        public void BadRateIsOneProblemAndStaysTheDefault(string value)
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\nfirst_person_sim_rate = " + value);
            Assert.Single(c.Problems);
            Assert.Equal(0.4f, c.FirstPersonSimRate);
        }
    }
}
