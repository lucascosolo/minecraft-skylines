// Unity-free timing for unattended runs (launch.cfg selftest_quit), compiled into MinecraftSkylines.Mod.Tests as well.
namespace MinecraftSkylines.Mod.SelfTest
{
    internal enum UnattendedAction { None, AbortSelfTest, Quit }

    /// <summary>
    /// When to quit the game in an unattended run: 3 s after the self-test report (or after an autoload save was
    /// not found), or, 900 s after the autoloaded level finished loading without a report, abort the self-test
    /// (which writes a partial report) and quit 3 s later. Does nothing when <c>selftest_quit</c> is off.
    /// </summary>
    internal sealed class UnattendedPolicy
    {
        public const double QuitDelaySeconds = 3;
        public const double SafetySeconds = 900;

        private readonly bool _quitEnabled;
        private double? _quitDue;
        private double? _loadedAt;
        private bool _quitIssued;

        public UnattendedPolicy(bool quitEnabled)
        {
            _quitEnabled = quitEnabled;
            QuitReason = "";
        }

        public string QuitReason { get; private set; }

        public void ReportWritten(double now) { Schedule(now, "self-test report written"); }

        public void AutoloadNotFound(double now) { Schedule(now, "autoload save not found"); }

        public void AutoloadLevelLoaded(double now)
        {
            if (_loadedAt == null) _loadedAt = now;
        }

        public UnattendedAction Tick(double now)
        {
            if (!_quitEnabled || _quitIssued) return UnattendedAction.None;
            if (_quitDue == null)
            {
                if (_loadedAt == null || now - _loadedAt.Value < SafetySeconds) return UnattendedAction.None;
                Schedule(now, "safety timeout: no self-test report " + SafetySeconds + " s after the level load");
                return UnattendedAction.AbortSelfTest;
            }
            if (now < _quitDue.Value) return UnattendedAction.None;
            _quitIssued = true;
            return UnattendedAction.Quit;
        }

        private void Schedule(double now, string reason)
        {
            if (!_quitEnabled || _quitDue != null) return;
            _quitDue = now + QuitDelaySeconds;
            QuitReason = reason;
        }
    }
}
