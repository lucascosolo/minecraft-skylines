using System;
using System.Diagnostics;
using System.IO;
using ColossalFramework.Plugins;
using Skylines.Host;

namespace MinecraftSkylines.Mod
{
    /// <summary>
    /// Starts Minecraft in the background from <c>launch.cfg</c> (in the mod's folder) and tracks the wait
    /// for it to connect. It only ever touches the one process it started and never stops it.
    /// Main thread only; every entry point is exception-guarded.
    /// </summary>
    internal sealed class MinecraftLauncher
    {
        private const string ConfigName = "launch.cfg";

        private readonly HostLog _log;
        private readonly string _companionLog;
        private CompanionProcess _process;
        private readonly Stopwatch _waited = new Stopwatch();
        private LaunchConfig _config;
        private string _configPath;
        private string _disabledReason;
        private bool _loaded;
        private string _error = "";

        public MinecraftLauncher(HostLog log, string companionLogPath)
        {
            _log = log;
            _companionLog = companionLogPath;
            _process = new CompanionProcess(companionLogPath);
        }

        /// <summary>True while waiting for the link after a start (or a re-arm); the caller then auto-enters.</summary>
        public bool Pending { get; private set; }

        /// <summary>True when a usable launch.cfg was found.</summary>
        public bool Enabled
        {
            get { Load(); return _config != null; }
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
                _error = "";
                if (!_process.IsRunning)
                {
                    _process = new CompanionProcess(_companionLog);
                    string failure = _process.Start(_config.Command, _config.Args, _config.WorkingDir, _config.Env);
                    if (failure != null)
                    {
                        Fail("could not start Minecraft: " + failure);
                        return true;
                    }
                    _log.Info("companion started: " + _config.Command + " " + string.Join(" ", _config.Args.ToArray()) + "; log " + _process.LogPath);
                }
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
                    Fail("Minecraft exited before connecting (exit code " + (_process.ExitCode.HasValue ? _process.ExitCode.Value.ToString() : "?") + ")");
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
                return "Starting Minecraft… (first start can take a minute) " + (int)_waited.Elapsed.TotalSeconds + " s  [Esc cancels]";
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
