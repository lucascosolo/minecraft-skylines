using MinecraftSkylines.Mod.SelfTest;
using Xunit;

namespace MinecraftSkylines.Mod.Tests
{
    public class UnattendedPolicyTests
    {
        [Fact]
        public void Disabled_never_acts()
        {
            var p = new UnattendedPolicy(false);
            p.AutoloadLevelLoaded(0);
            p.ReportWritten(1);
            p.AutoloadNotFound(1);
            Assert.Equal(UnattendedAction.None, p.Tick(5));
            Assert.Equal(UnattendedAction.None, p.Tick(5000));
            Assert.Equal("", p.QuitReason);
        }

        [Fact]
        public void Constants_match_contract()
        {
            Assert.Equal(3.0, UnattendedPolicy.QuitDelaySeconds);
            Assert.Equal(900.0, UnattendedPolicy.SafetySeconds);
        }

        [Fact]
        public void Fresh_policy_has_empty_reason_and_no_action()
        {
            var p = new UnattendedPolicy(true);
            Assert.Equal("", p.QuitReason);
            Assert.Equal(UnattendedAction.None, p.Tick(100000));
        }

        [Fact]
        public void ReportWritten_quits_once_after_delay()
        {
            var p = new UnattendedPolicy(true);
            p.ReportWritten(100);
            Assert.Contains("report", p.QuitReason);
            Assert.Equal(UnattendedAction.None, p.Tick(102.9));
            Assert.Equal(UnattendedAction.Quit, p.Tick(103));
            Assert.Equal(UnattendedAction.None, p.Tick(104));
            Assert.Equal(UnattendedAction.None, p.Tick(5000));
        }

        [Fact]
        public void AutoloadNotFound_quits_with_reason()
        {
            var p = new UnattendedPolicy(true);
            p.AutoloadNotFound(50);
            Assert.Contains("not found", p.QuitReason);
            Assert.Equal(UnattendedAction.None, p.Tick(52.9));
            Assert.Equal(UnattendedAction.Quit, p.Tick(60));
        }

        [Fact]
        public void First_scheduling_wins()
        {
            var p = new UnattendedPolicy(true);
            p.ReportWritten(10);
            p.AutoloadNotFound(20);
            p.ReportWritten(30);
            Assert.Contains("report", p.QuitReason);
            Assert.Equal(UnattendedAction.Quit, p.Tick(13));
        }

        [Fact]
        public void No_timeout_without_level_loaded()
        {
            var p = new UnattendedPolicy(true);
            Assert.Equal(UnattendedAction.None, p.Tick(100000));
        }

        [Fact]
        public void Report_before_timeout_prevents_abort()
        {
            var p = new UnattendedPolicy(true);
            p.AutoloadLevelLoaded(10);
            p.ReportWritten(500);
            Assert.Equal(UnattendedAction.Quit, p.Tick(503));
            Assert.Equal(UnattendedAction.None, p.Tick(2000));
        }

        [Fact]
        public void Only_first_level_loaded_counts()
        {
            var p = new UnattendedPolicy(true);
            p.AutoloadLevelLoaded(10);
            p.AutoloadLevelLoaded(500);
            Assert.Equal(UnattendedAction.AbortSelfTest, p.Tick(910));
        }

        [Fact]
        public void Safety_timeout_sequence()
        {
            var p = new UnattendedPolicy(true);
            p.AutoloadLevelLoaded(10);
            Assert.Equal(UnattendedAction.None, p.Tick(909.9));
            Assert.Equal(UnattendedAction.AbortSelfTest, p.Tick(910));
            Assert.Contains("timeout", p.QuitReason);
            p.ReportWritten(910.1);
            Assert.Contains("timeout", p.QuitReason);
            Assert.Equal(UnattendedAction.None, p.Tick(912.9));
            Assert.Equal(UnattendedAction.Quit, p.Tick(913));
            Assert.Equal(UnattendedAction.None, p.Tick(1000));
        }
    }
}
