using System;
using System.Diagnostics;
using System.IO;
using ColossalFramework.Plugins;
using Skylines.Host;

namespace MinecraftSkylines.Mod
{
    /// <summary>
    /// Starts Minecraft in the background from <c>launch.cfg</c> (in the mod's folder): ahead of time per its
    /// <c>prewarm</c> key (at the main menu by default, or when a city loads), or on Ctrl+Shift+M, and tracks the
    /// wait for it to connect. At most one companion: it only ever touches the one process it started, never
    /// starts a second while that one runs or while Minecraft is connected, and never stops it.
    /// Main thread only; every entry point is exception-guarded.
    /// </summary>
    internal sealed class MinecraftLauncher
    {
        private const string ConfigName = "launch.cfg";

        /// <summary>A Minecraft started by hand reconnects within the guest's 5 s slow retry; prewarm waits that long first.</summary>
        private const double PrewarmGraceSeconds = 6;

        private readonly HostLog _log;
        private readonly string _companionLog;
        private readonly bool _linux;
        private CompanionProcess _process;
        private readonly Stopwatch _waited = new Stopwatch();
        private readonly Stopwatch _sinceEnabled = Stopwatch.StartNew();
        private LaunchConfig _config;
        private string _configPath;
        private string _disabledReason;
        private bool _loaded;
        private string _error = "";
        private bool _gameStartDone;
        private bool _wasInCity;
        private bool _background;
        private bool _connected;

        /// <param name="linux">Application.platform is LinuxPlayer: spawn through libc posix_spawn.</param>
        public MinecraftLauncher(HostLog log, string companionLogPath, bool linux)
        {
            _log = log;
            _companionLog = companionLogPath;
            _linux = linux;
            _process = new CompanionProcess(companionLogPath, linux);
        }

        /// <summary>True while waiting for the link after a start (or a re-arm); the caller then auto-enters.</summary>
        public bool Pending { get; private set; }

        /// <summary>launch.cfg <c>selftest</c>; Off without a usable launch.cfg.</summary>
        public SelfTestMode SelfTest
        {
            get { Load(); return _config == null ? SelfTestMode.Off : _config.SelfTest; }
        }

        /// <summary>launch.cfg <c>autoload</c>; empty without a usable launch.cfg.</summary>
        public string Autoload
        {
            get { Load(); return _config == null ? "" : _config.Autoload; }
        }

        /// <summary>launch.cfg <c>selftest_quit</c>; false without a usable launch.cfg.</summary>
        public bool SelfTestQuit
        {
            get { Load(); return _config != null && _config.SelfTestQuit; }
        }

        /// <summary>launch.cfg <c>block_material</c>; 0 without a usable launch.cfg.</summary>
        public int BlockMaterial
        {
            get { Load(); return _config == null ? 0 : _config.BlockMaterial; }
        }

        /// <summary>launch.cfg <c>overlay_mode</c>; 0 without a usable launch.cfg.</summary>
        public int OverlayMode
        {
            get { Load(); return _config == null ? Skylines.Host.Overlay.OverlayMode.Default : _config.OverlayMode; }
        }

        /// <summary>launch.cfg <c>underground_mode</c>; the default without a usable launch.cfg.</summary>
        public int UndergroundMode
        {
            get { Load(); return _config == null ? LaunchConfig.DefaultUndergroundMode : _config.UndergroundMode; }
        }

        /// <summary>launch.cfg <c>clip_preset</c>; the default without a usable launch.cfg.</summary>
        public int ClipPreset
        {
            get { Load(); return _config == null ? LaunchConfig.DefaultClipPreset : _config.ClipPreset; }
        }

        /// <summary>True when launch.cfg's args contain <paramref name="arg"/> (e.g. -PmcskylinesDebugCommands).</summary>
        public bool HasArg(string arg)
        {
            Load();
            return _config != null && _config.Args.Contains(arg);
        }

        /// <summary>True when a usable launch.cfg was found.</summary>
        public bool Enabled
        {
            get { Load(); return _config != null; }
        }

        /// <summary>
        /// Every frame: starts the companion once per trigger (mod enabled, or each city load, per <c>prewarm</c>)
        /// unless Minecraft is connected or ours is running, and notices a background start that died.
        /// </summary>
        public void Prewarm(bool inCity, bool connected)
        {
            try
            {
                _connected = connected;
                bool cityLoaded = inCity && !_wasInCity;
                _wasInCity = inCity;
                if (connected)
                {
                    _background = false;
                }
                else if (_background && !_process.IsRunning)
                {
                    _background = false;
                    Fail("Minecraft exited before connecting (exit code " + ExitCodeText() + ")");
                }
                if (connected || Pending || _sinceEnabled.Elapsed.TotalSeconds < PrewarmGraceSeconds || !Enabled)
                {
                    return;
                }
                bool due = _config.Prewarm == PrewarmMode.GameStart ? !_gameStartDone : _config.Prewarm == PrewarmMode.CityLoad && cityLoaded;
                _gameStartDone = true;
                if (due && !_process.IsRunning && StartCompanion("prewarm " + _config.Prewarm))
                {
                    _background = true;
                }
            }
            catch (Exception e)
            {
                _log.Error("minecraft launcher prewarm", e);
                _gameStartDone = true;
            }
        }

        /// <summary>Ctrl+Shift+M with no link. Starts the companion unless ours is already running. Returns false when auto-launch is off.</summary>
        public bool Begin()
        {
            try
            {
                if (!Enabled)
                {
                    return false;
                }
                if (!_process.IsRunning && !StartCompanion("shortcut"))
                {
                    return true;
                }
                _background = false;
                _waited.Reset();
                _waited.Start();
                Pending = true;
            }
            catch (Exception e)
            {
                _log.Error("minecraft launcher begin", e);
                Fail("launcher error: " + e.Message);
            }
            return true;
        }

        /// <summary>Stops waiting (Esc, second shortcut press, city unloaded, entered). Minecraft keeps running.</summary>
        public void Cancel()
        {
            Pending = false;
            _waited.Stop();
        }

        /// <summary>Per frame while <see cref="Pending"/> and not yet connected: detects an early exit or the timeout.</summary>
        public void Tick()
        {
            try
            {
                if (!Pending)
                {
                    return;
                }
                if (!_process.IsRunning)
                {
                    Fail("Minecraft exited before connecting (exit code " + ExitCodeText() + ")");
                }
                else if (_waited.Elapsed.TotalSeconds > _config.ConnectTimeoutSeconds)
                {
                    Fail("Minecraft did not connect within " + _config.ConnectTimeoutSeconds + " s");
                }
            }
            catch (Exception e)
            {
                _log.Error("minecraft launcher tick", e);
                Fail("launcher error: " + e.Message);
            }
        }

        /// <summary>One or two overlay lines.</summary>
        public string OverlayText()
        {
            if (Pending)
            {
                return "Starting Minecraft… (first start can take a minute) " + SecondsSinceStart() + " s  [Esc cancels]";
            }
            if (_background && !_connected && _process.IsRunning)
            {
                return "Minecraft: starting in background (" + SecondsSinceStart() + " s)";
            }
            if (_error.Length > 0)
            {
                return "Auto-start failed: " + _error + "\nCompanion log: " + _process.LogPath;
            }
            if (!Enabled)
            {
                return "Auto-start: off (" + _disabledReason + ")";
            }
            return "";
        }

        /// <summary>Starts a fresh companion; on failure logs every detail and records the error. Returns true when it started.</summary>
        private bool StartCompanion(string why)
        {
            _error = "";
            _process = new CompanionProcess(_companionLog, _linux);
            string failure = _process.Start(_config.Command, _config.Args, _config.WorkingDir, _config.Env);
            if (_process.Notice != null)
            {
                _log.Warn("auto-start: " + _process.Notice);
            }
            if (failure != null)
            {
                _log.Warn("auto-start: start failed (" + (_process.UsesPosixSpawn ? "posix_spawn" : "Process.Start") + "): " + _process.LastErrorDetail);
                Fail("could not start Minecraft: " + failure);
                return false;
            }
            _log.Info("companion started (" + why + ", " + (_process.UsesPosixSpawn && _process.Notice == null ? "posix_spawn" : "Process.Start")
                + ", pid " + _process.Pid + "): " + _config.Command + " " + string.Join(" ", _config.Args.ToArray()) + "; log " + _process.LogPath);
            return true;
        }

        private string ExitCodeText()
        {
            int? code = _process.ExitCode;
            return code.HasValue ? code.Value.ToString() : "?";
        }

        private int SecondsSinceStart()
        {
            DateTime? at = _process.StartedAt;
            return at.HasValue ? (int)(DateTime.UtcNow - at.Value).TotalSeconds : (int)_waited.Elapsed.TotalSeconds;
        }

        private void Fail(string message)
        {
            Pending = false;
            _waited.Stop();
            _error = message;
            _log.Warn("auto-start: " + message + "; companion log " + _process.LogPath);
        }

        private void Load()
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;
            try
            {
                string folder = FindModFolder();
                if (folder == null)
                {
                    _disabledReason = "mod folder not found";
                    return;
                }
                _configPath = Path.Combine(folder, ConfigName);
                if (!File.Exists(_configPath))
                {
                    _disabledReason = "to enable, create " + _configPath + " with command, args, working_dir";
                    return;
                }
                LaunchConfig c = LaunchConfig.Load(_configPath);
                foreach (string p in c.Problems)
                {
                    _log.Warn(ConfigName + ": " + p);
                }
                if (!c.IsUsable)
                {
                    _disabledReason = "fix " + _configPath + ", see the log";
                    return;
                }
                _config = c;
                _log.Info("auto-start enabled from " + _configPath);
            }
            catch (Exception e)
            {
                _log.Error("launch.cfg", e);
                _disabledReason = "launch.cfg unreadable, see the log";
            }
        }

        /// <summary>
        /// CS1 loads mod DLLs with Assembly.Load(byte[]), so Assembly.Location is empty; the folder comes from
        /// the PluginManager entry that contains this assembly (PluginInfo.modPath).
        /// </summary>
        private static string FindModFolder()
        {
            System.Reflection.Assembly mine = typeof(ModInfo).Assembly;
            foreach (PluginManager.PluginInfo p in PluginManager.instance.GetPluginsInfo())
            {
                if (p.ContainsAssembly(mine))
                {
                    return p.modPath;
                }
            }
            return string.IsNullOrEmpty(mine.Location) ? null : Path.GetDirectoryName(mine.Location);
        }
    }
}
