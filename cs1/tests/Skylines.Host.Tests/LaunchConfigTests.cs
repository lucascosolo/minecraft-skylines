using Skylines.Host;
using Xunit;

namespace Skylines.Host.Tests
{
    public class LaunchConfigTests
    {
        [Fact]
        public void ParsesAllKeys()
        {
            LaunchConfig c = LaunchConfig.Parse(
                "# comment\n\ncommand = /opt/gradlew\r\nargs = --no-daemon --console=plain :fabric:runClient\n"
                + "working_dir = /home/x/minecraft\nenv.GRADLE_USER_HOME = /home/x/.cache/gradle-home\nenv.A=b=c\nconnect_timeout_seconds = 90\n");
            Assert.True(c.IsUsable);
            Assert.Empty(c.Problems);
            Assert.Equal("/opt/gradlew", c.Command);
            Assert.Equal(new[] { "--no-daemon", "--console=plain", ":fabric:runClient" }, c.Args.ToArray());
            Assert.Equal("/home/x/minecraft", c.WorkingDir);
            Assert.Equal("/home/x/.cache/gradle-home", c.Env["GRADLE_USER_HOME"]);
            Assert.Equal("b=c", c.Env["A"]);
            Assert.Equal(90, c.ConnectTimeoutSeconds);
        }

        [Fact]
        public void DefaultsAndMissingCommand()
        {
            LaunchConfig c = LaunchConfig.Parse("working_dir = /x\n");
            Assert.False(c.IsUsable);
            Assert.Equal(180, c.ConnectTimeoutSeconds);
            Assert.Contains(c.Problems, p => p.Contains("command"));
        }

        [Fact]
        public void ReportsBadLinesWithoutThrowing()
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\nnonsense\nbogus = 1\nconnect_timeout_seconds = abc\nconnect_timeout_seconds = 0\n");
            Assert.True(c.IsUsable);
            Assert.Equal(4, c.Problems.Count);
            Assert.Equal(180, c.ConnectTimeoutSeconds);
        }

        [Fact]
        public void LoadOfMissingFileIsAProblemNotAnException()
        {
            LaunchConfig c = LaunchConfig.Load(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "no-such-dir-xyz", "launch.cfg"));
            Assert.False(c.IsUsable);
            Assert.NotEmpty(c.Problems);
        }

        [Fact]
        public void PrewarmDefaultsToGameStart()
        {
            Assert.Equal(PrewarmMode.GameStart, LaunchConfig.Parse("command = x\n").Prewarm);
        }

        [Theory]
        [InlineData("game_start", PrewarmMode.GameStart)]
        [InlineData("city_load", PrewarmMode.CityLoad)]
        [InlineData("off", PrewarmMode.Off)]
        [InlineData("CITY_LOAD", PrewarmMode.CityLoad)]
        [InlineData("  Off  ", PrewarmMode.Off)]
        public void ParsesPrewarm(string value, PrewarmMode expected)
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\nprewarm = " + value + "\n");
            Assert.Empty(c.Problems);
            Assert.Equal(expected, c.Prewarm);
        }

        [Fact]
        public void BadPrewarmIsAProblemWithLineNumberAndStaysGameStart()
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\nprewarm = sometimes\n");
            Assert.Contains(c.Problems, p => p.Contains("prewarm") && p.Contains("2"));
            Assert.Equal(PrewarmMode.GameStart, c.Prewarm);
        }

        [Fact]
        public void SelfTestDefaultsToOff()
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\n");
            Assert.Equal(SelfTestMode.Off, c.SelfTest);
            Assert.Empty(c.Problems);
        }

        [Theory]
        [InlineData("off", SelfTestMode.Off)]
        [InlineData("city_load", SelfTestMode.CityLoad)]
        [InlineData("CITY_LOAD", SelfTestMode.CityLoad)]
        [InlineData("  Off  ", SelfTestMode.Off)]
        public void ParsesSelfTest(string value, SelfTestMode expected)
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\nselftest = " + value + "\n");
            Assert.Empty(c.Problems);
            Assert.Equal(expected, c.SelfTest);
        }

        [Fact]
        public void BadSelfTestIsOneProblemAndStaysOff()
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\nselftest = sometimes\n");
            Assert.Single(c.Problems);
            Assert.Contains(c.Problems, p => p.Contains("selftest"));
            Assert.Equal(SelfTestMode.Off, c.SelfTest);
        }

        [Fact]
        public void OverlayModeDefaultsToTheOverlayDefault()
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\n");
            Assert.Equal(Skylines.Host.Overlay.OverlayMode.Default, c.OverlayMode);
            Assert.Empty(c.Problems);
        }

        [Theory]
        [InlineData("0", 0)]
        [InlineData("2", 2)]
        [InlineData(" 4 ", 4)]
        public void ParsesOverlayMode(string value, int expected)
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\noverlay_mode = " + value + "\n");
            Assert.Empty(c.Problems);
            Assert.Equal(expected, c.OverlayMode);
        }

        [Theory]
        [InlineData("5")]
        [InlineData("x")]
        [InlineData("-1")]
        public void BadOverlayModeIsOneProblemAndStaysTheDefault(string value)
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\noverlay_mode = " + value + "\n");
            Assert.Single(c.Problems);
            Assert.Equal(Skylines.Host.Overlay.OverlayMode.Default, c.OverlayMode);
        }

        [Fact]
        public void BlockMaterialDefaultsToZero()
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\n");
            Assert.Equal(0, c.BlockMaterial);
            Assert.Empty(c.Problems);
        }

        [Theory]
        [InlineData("0", 0)]
        [InlineData("1", 1)]
        [InlineData("2", 2)]
        [InlineData("3", 3)]
        [InlineData("  2  ", 2)]
        public void ParsesBlockMaterial(string value, int expected)
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\nblock_material = " + value + "\n");
            Assert.Empty(c.Problems);
            Assert.Equal(expected, c.BlockMaterial);
        }

        [Theory]
        [InlineData("4")]
        [InlineData("x")]
        [InlineData("-1")]
        public void BadBlockMaterialIsOneProblemAndStaysZero(string value)
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\nblock_material = " + value + "\n");
            Assert.Single(c.Problems);
            Assert.Contains(c.Problems, p => p.Contains("block_material"));
            Assert.DoesNotContain(c.Problems, p => p.ToLowerInvariant().Contains("unknown"));
            Assert.Equal(0, c.BlockMaterial);
        }

        [Theory]
        [InlineData("a b  c", new[] { "a", "b", "c" })]
        [InlineData("a \"b c\" d", new[] { "a", "b c", "d" })]
        [InlineData("--x=\"a b\" y", new[] { "--x=a b", "y" })]
        [InlineData("\"\" z", new[] { "", "z" })]
        [InlineData("\"say \\\"hi\\\" \\\\\"", new[] { "say \"hi\" \\" })]
        [InlineData("   ", new string[0])]
        public void SplitsArguments(string line, string[] expected)
        {
            Assert.Equal(expected, LaunchConfig.SplitArgs(line).ToArray());
        }

        [Fact]
        public void AutoloadAndSelfTestQuitDefaults()
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\n");
            Assert.Equal("", c.Autoload);
            Assert.False(c.SelfTestQuit);
            Assert.Empty(c.Problems);
        }

        [Fact]
        public void AutoloadKeepsValueVerbatimAndLastWins()
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\nautoload = first\nautoload =   My City #2 = Big  Save  \n");
            Assert.Equal("My City #2 = Big  Save", c.Autoload);
            Assert.Empty(c.Problems);
        }

        [Fact]
        public void AutoloadEmptyIsAllowed()
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\nautoload =\n");
            Assert.Equal("", c.Autoload);
            Assert.Empty(c.Problems);
        }

        [Theory]
        [InlineData("TRUE", true)]
        [InlineData("true", true)]
        [InlineData("False", false)]
        public void SelfTestQuitParsesCaseInsensitive(string v, bool expected)
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\nselftest_quit = " + v + "\n");
            Assert.Equal(expected, c.SelfTestQuit);
            Assert.Empty(c.Problems);
        }

        [Fact]
        public void BadSelfTestQuitIsOneProblemAndKeepsPreviousValue()
        {
            LaunchConfig c = LaunchConfig.Parse("command = x\nselftest_quit = true\nselftest_quit = maybe\n");
            Assert.True(c.SelfTestQuit);
            Assert.Single(c.Problems);
            Assert.Contains("selftest_quit", c.Problems[0]);
        }
    }
}
