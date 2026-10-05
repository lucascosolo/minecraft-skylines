using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using ColossalFramework.IO;
using ICities;
using MinecraftSkylines.Mod.Diagnostics;
using MinecraftSkylines.Mod.SelfTest;
using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Skylines.Host;

namespace MinecraftSkylines.Mod
{
    /// <summary>
    /// Owns the bridge host and player mode, exchanges status with Minecraft and shows the link state.
    /// Everything here runs on the Unity main thread except OnLoadData/OnSaveData, which only touch
    /// the thread-safe <see cref="SaveIdentity"/>. The game thread never blocks on the socket.
    /// </summary>
    internal static class LinkService
    {
        private const string ModName = "MinecraftSkylines";
        private const string ModVersion = "0.1.0";

        private static readonly SaveIdentity s_saveId = new SaveIdentity("MinecraftSkylines.SaveId");
        private static readonly List<BridgeEvent> s_events = new List<BridgeEvent>();
        private static readonly Stopwatch s_tickWatch = new Stopwatch();

        private static HostLog s_log;
        private static MainThreadPump s_pump;
        private static StatusOverlay s_overlay;
        private static TerrainClipProbe s_probe;
        private static PlayerMode s_player;
        private static SelfTestController s_selfTest;
        private static MinecraftLauncher s_launcher;
        private static BridgeHost s_host;
        private static string s_startError;

        private static CityState s_lastCity;
        private static Guid s_lastSentSaveId;
        private static bool s_statusDirty;
        private static GuestStatus s_guest;
        private static string s_lastDisconnect = "";
        private static double s_tickMaxMs;
        private static double s_tickTotalMs;
        private static long s_ticks;

        public static void Start()
        {
            if (s_host != null)
            {
                return;
            }
            s_log = new HostLog(ModName);
            s_log.Info("enabling " + ModName + " " + ModVersion + " on Cities: Skylines " + CityState.GameVersion
                + "; log file " + (s_log.FilePath ?? "(none)"));

            s_pump = MainThreadPump.Install(ModName);
            s_pump.OnHandlerError = (where, e) => s_log.Error("handler in " + where, e);
            s_overlay = new StatusOverlay();
            s_pump.Updated += Tick;
            s_probe = new TerrainClipProbe(s_log);
            s_pump.Updated += s_probe.Update;
            s_pump.Gui += s_overlay.Draw;
            s_launcher = new MinecraftLauncher(s_log, Path.Combine(Path.Combine(DataLocation.localApplicationData, "ModLogs"), "MinecraftSkylines-companion.log"),
                UnityEngine.Application.platform == UnityEngine.RuntimePlatform.LinuxPlayer);
            s_player = new PlayerMode(s_log, () => s_statusDirty = true, s_launcher);
            s_selfTest = new SelfTestController(s_log, s_player, s_launcher, ModVersion);
            s_pump.LateUpdated += () => s_player.LateUpdate(s_host);
            s_pump.Gui += s_player.OnGui;
            s_pump.Updated += s_player.Viewer.Update;
            s_pump.Quitting += () => Stop("game exiting");

            var options = new HostOptions
            {
                Port = ConfiguredPort(),
                AppProtocol = AppProtocol.Name,
                AppMajor = AppProtocol.Major,
                AppMinor = AppProtocol.Minor,
                PeerName = "Cities: Skylines " + CityState.GameVersion,
                PeerVersion = ModVersion,
                Log = line => s_log.Info("bridge: " + line),
            };
            s_host = new BridgeHost(options);
            s_startError = null;
            if (!s_host.Start())
            {
                s_startError = "could not listen on 127.0.0.1:" + options.Port + " (port in use?)";
                s_log.Warn(s_startError);
            }
            else
            {
                s_log.Info("listening on 127.0.0.1:" + s_host.Port);
            }
            s_statusDirty = true;
        }

        public static void Stop(string why)
        {
            if (s_host == null)
            {
                return;
            }
            s_log.Info("stopping: " + why + PerfSummary());
            s_selfTest.Abort(why);
            s_player.Exit(why, s_host, true);
            s_player.Viewer.Dispose();
            s_probe.RestoreAll(why, true);
            s_host.Shutdown(GoodbyeCodes.ShuttingDown, why);
            s_host = null;
            s_pump.Uninstall();
            s_pump = null;
            s_overlay = null;
            s_player = null;
            s_selfTest = null;
            s_launcher = null;
            s_guest = null;
            s_log.Close();
        }

        public static void OnLevelLoaded(string mode)
        {
            CityState.SetInCity(true);
            // Milestone 1 never assigns an id: a city is only paired (and backed up first) when the
            // player switches it to Minecraft mode, so a city merely loaded with the mod enabled is
            // saved exactly as without it. See docs/DECISIONS.md, "player safety".
            if (s_selfTest != null) s_selfTest.OnLevelLoaded();
            Log("level loaded (" + mode + "); save id " + (s_saveId.LoadedFromSave ? s_saveId.Id.ToString() : "none (city not paired)"));
            s_statusDirty = true;
        }

        public static void OnLevelUnloading()
        {
            CityState.SetInCity(false);
            s_saveId.Clear();
            if (s_selfTest != null) s_selfTest.OnLevelUnloading(s_host);
            if (s_player != null) s_player.Exit("city unloading", s_host, true);
            if (s_probe != null) s_probe.RestoreAll("level unloading", false);
            Log("level unloading");
            s_statusDirty = true;
        }

        public static void OnLoadData(ISerializableData data)
        {
            Log("load data: " + s_saveId.Load(data));
        }

        public static void OnSaveData(ISerializableData data)
        {
            // Writes only if the city is paired (SaveIdentity.Save is a no-op for Guid.Empty).
            s_saveId.Save(data);
            if (s_saveId.Id != Guid.Empty)
            {
                Log("save data: wrote save id " + s_saveId.Id);
            }
        }

        public static void LogError(string where, Exception e)
        {
            if (s_log != null)
            {
                s_log.Error(where, e);
            }
        }

        private static void Log(string line)
        {
            if (s_log != null)
            {
                s_log.Info(line);
            }
        }

        private static int ConfiguredPort()
        {
            string v = Environment.GetEnvironmentVariable("MCSKYLINES_PORT");
            int p;
            if (!string.IsNullOrEmpty(v) && int.TryParse(v, out p) && p > 0 && p < 65536)
            {
                return p;
            }
            return BridgeConstants.DefaultPort;
        }

        private static void Tick()
        {
            if (s_host == null)
            {
                return;
            }
            s_tickWatch.Reset();
            s_tickWatch.Start();

            s_events.Clear();
            s_host.Poll(s_events);
            foreach (BridgeEvent e in s_events)
            {
                Handle(e);
            }

            CityState city = CityState.Capture();
            Guid saveId = s_saveId.Id;
            s_launcher.Prewarm(city.InCity && !city.Loading, s_host.State == BridgeState.Connected);
            s_player.Update(s_host, city.InCity && !city.Loading);
            s_selfTest.Update(s_host, city.InCity && !city.Loading);
            if (!city.Equals(s_lastCity) || saveId != s_lastSentSaveId)
            {
                s_lastCity = city;
                s_statusDirty = true;
            }
            if (s_statusDirty && s_host.State == BridgeState.Connected)
            {
                SendHostStatus(city, saveId);
            }
            s_overlay.Text = OverlayText(city, saveId);

            s_tickWatch.Stop();
            double ms = s_tickWatch.Elapsed.TotalMilliseconds;
            s_ticks++;
            s_tickTotalMs += ms;
            if (ms > s_tickMaxMs)
            {
                s_tickMaxMs = ms;
            }
        }

        private static void Handle(BridgeEvent e)
        {
            switch (e.Kind)
            {
                case BridgeEventKind.StateChanged:
                    Log("link state " + e.StateName + (string.IsNullOrEmpty(e.Detail) ? "" : " (" + e.Detail + ")"));
                    if (e.State == BridgeState.Connected)
                    {
                        Log("connected to " + s_host.PeerName + " " + s_host.PeerVersion + ", app minor " + s_host.NegotiatedAppMinor);
                        s_statusDirty = true;
                    }
                    break;
                case BridgeEventKind.Disconnected:
                    s_lastDisconnect = e.CauseName + (e.Code >= 0 ? " code " + e.Code : "") + (string.IsNullOrEmpty(e.Reason) ? "" : ": " + e.Reason);
                    Log("disconnected: " + s_lastDisconnect);
                    s_guest = null;
                    s_player.Exit("link lost: " + s_lastDisconnect, s_host, false);
                    break;
                case BridgeEventKind.Message:
                    if (e.MessageType == AppProtocol.GuestStatusType)
                    {
                        try
                        {
                            GuestStatus g = GuestStatus.Decode(e.Payload);
                            if (s_guest == null || g.Flags != s_guest.Flags || g.WorldName != s_guest.WorldName || g.PairedSaveId != s_guest.PairedSaveId)
                            {
                                Log("guest status: flags " + g.Flags + ", world '" + g.WorldName + "', paired save " + g.PairedSaveId);
                            }
                            s_guest = g;
                        }
                        catch (ProtocolException ex)
                        {
                            s_log.Warn("bad GUEST_STATUS ignored: " + ex.Message);
                        }
                    }
                    else if (e.MessageType == AppProtocol.PlayerStateType)
                    {
                        try
                        {
                            s_player.OnPlayerState(PlayerState.Decode(e.Payload));
                        }
                        catch (ProtocolException ex)
                        {
                            s_log.Warn("bad PLAYER_STATE ignored: " + ex.Message);
                        }
                    }
                    // Other application types are ignored (forward compatibility within major 1).
                    break;
            }
        }

        private static void SendHostStatus(CityState city, Guid saveId)
        {
            uint flags = 0;
            if (city.InCity) flags |= HostStatusFlags.InCity;
            if (city.Loading) flags |= HostStatusFlags.Loading;
            if (city.SimulationPaused) flags |= HostStatusFlags.SimPaused;
            if (s_player.IsOn) flags |= HostStatusFlags.PlayerMode;
            var status = new HostStatus
            {
                Flags = flags,
                CityName = city.CityName ?? "",
                SaveId = saveId,
                GameVersion = CityState.GameVersion,
            };
            if (s_host.Send(AppProtocol.HostStatusType, status.Encode()))
            {
                s_statusDirty = false;
                s_lastSentSaveId = saveId;
            }
        }

        private static string OverlayText(CityState city, Guid saveId)
        {
            var sb = new StringBuilder();
            sb.Append("Minecraft Skylines ").Append(ModVersion).Append("  [F7 hides]\n");
            if (s_startError != null)
            {
                sb.Append("ERROR: ").Append(s_startError).Append('\n');
            }
            sb.Append("Link: ").Append(BridgeEvent.NameOf(s_host.State));
            if (s_host.State == BridgeState.Connected)
            {
                sb.Append(" to ").Append(s_host.PeerName).Append(' ').Append(s_host.PeerVersion);
            }
            else if (s_host.State == BridgeState.Listening)
            {
                sb.Append(" on 127.0.0.1:").Append(s_host.Port);
            }
            sb.Append('\n');
            if (s_guest != null)
            {
                bool inWorld = (s_guest.Flags & GuestStatusFlags.InWorld) != 0;
                sb.Append("Minecraft: ").Append(inWorld ? "in world '" + s_guest.WorldName + "'" : "no world loaded").Append('\n');
            }
            if (s_lastDisconnect.Length > 0)
            {
                sb.Append("Last disconnect: ").Append(s_lastDisconnect).Append('\n');
            }
            sb.Append("City: ").Append(city.InCity ? (city.CityName.Length > 0 ? city.CityName : "(unnamed)") : "none");
            if (city.InCity)
            {
                sb.Append(saveId == Guid.Empty ? "  (not paired)" : "  save id " + saveId.ToString().Substring(0, 8));
            }
            sb.Append('\n').Append(s_player.OverlayText());
            if (s_selfTest.OverlayText.Length > 0) sb.Append('\n').Append(s_selfTest.OverlayText);
            if (s_probe.Status.Length > 0)
            {
                sb.Append('\n').Append(s_probe.Status);
            }
            if (s_ticks > 0)
            {
                sb.Append("\nBridge tick: avg ").Append((s_tickTotalMs / s_ticks).ToString("0.000"))
                  .Append(" ms, max ").Append(s_tickMaxMs.ToString("0.000")).Append(" ms");
            }
            return sb.ToString();
        }

        private static string PerfSummary()
        {
            if (s_ticks == 0)
            {
                return "";
            }
            return string.Format("; bridge tick avg {0:0.000} ms, max {1:0.000} ms over {2} frames",
                s_tickTotalMs / s_ticks, s_tickMaxMs, s_ticks);
        }
    }
}
