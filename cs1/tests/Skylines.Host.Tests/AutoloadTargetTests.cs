using Skylines.Host;
using Xunit;

namespace Skylines.Host.Tests
{
    public class AutoloadTargetTests
    {
        [Fact]
        public void PrefixConstantIsNewColon()
        {
            Assert.Equal("new:", AutoloadTarget.NewGamePrefix);
        }

        [Theory]
        [InlineData("new:Green Plains", "Green Plains")]
        [InlineData("NEW:Green Plains", "Green Plains")]
        [InlineData("New:Two Rivers", "Two Rivers")]
        [InlineData("  new:  Green Plains  ", "Green Plains")]
        [InlineData("new:", "")]
        [InlineData("new:   ", "")]
        public void ReturnsTheTrimmedMapAfterThePrefix(string autoload, string expected)
        {
            Assert.Equal(expected, AutoloadTarget.NewGameMap(autoload));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("My City")]
        [InlineData("newcity")]
        [InlineData("new city")]
        public void ReturnsNullForSaveNames(string autoload)
        {
            Assert.Null(AutoloadTarget.NewGameMap(autoload));
        }

        [Fact]
        public void ConfigKeepsNewGameValueExactlyAndAddsNoProblem()
        {
            LaunchConfig c = LaunchConfig.Parse("command = /bin/true\nautoload = new:Green Plains\n");
            Assert.Equal("new:Green Plains", c.Autoload);
            Assert.Empty(c.Problems);
        }

        [Fact]
        public void ConfigWithBlankMapIsOneAutoloadProblem()
        {
            LaunchConfig c = LaunchConfig.Parse("command = /bin/true\nautoload = new:\n");
            Assert.Equal("new:", c.Autoload);
            Assert.Single(c.Problems);
            Assert.Contains("autoload", c.Problems[0]);
        }

        [Fact]
        public void ConfigWithPlainSaveNameIsUnchanged()
        {
            LaunchConfig c = LaunchConfig.Parse("command = /bin/true\nautoload = My City\n");
            Assert.Equal("My City", c.Autoload);
            Assert.Empty(c.Problems);
        }
    }
}
