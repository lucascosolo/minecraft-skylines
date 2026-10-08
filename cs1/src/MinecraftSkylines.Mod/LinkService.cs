using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using ColossalFramework.IO;
using ICities;
using MinecraftSkylines.Mod.Blocks;
using MinecraftSkylines.Mod.City;
using MinecraftSkylines.Mod.Diagnostics;
using MinecraftSkylines.Mod.SelfTest;
using MinecraftSkylines.Mod.Terrain;
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
        private static CityLink s_city;
        private static CityFocusLink s_focus;
        private static CityConditionsLink s_conditions;
        private static ClockLink s_cityClock;
        private static ObstacleLink s_obstacles;
        private static LightLink s_lights;
        private static WaterLink s_water;
        private static SkyLink s_sky;
        private static WalkInButton s_walkIn;
        private static BlockRenderer s_blocks;
        private static Entities.EntityLink s_entities;
        private static DigLink s_dig;
        private static SelectionOutline s_selection;
        private static OverlayLink s_gui;
        private static SelfTestController s_selfTest;
        private static MinecraftLauncher s_launcher;
        private static SaveAutoloader s_autoload;
        private static FixtureBuilder s_fixture;
        private static UnattendedPolicy s_unattended;
        private static readonly Stopwatch s_clock = Stopwatch.StartNew();
        private static bool s_autoloadIssued;
        private static BridgeHost s_host;
        private static string s_startError;

        private static CityState s_lastCity;
        private static Guid s_lastSentSaveId;
        private static bool s_statusDirty;
        private static bool s_cityReady;
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
            // Minecraft's GUI is drawn over everything except the status box, so it registers first.
            s_gui = new OverlayLink(s_log);
            s_pump.Gui += s_gui.Draw;
            s_pump.Gui += s_overlay.Draw;
            s_launcher = new MinecraftLauncher(s_log, Path.Combine(Path.Combine(DataLocation.localApplicationData, "ModLogs"), "MinecraftSkylines-companion.log"),
                UnityEngine.Application.platform == UnityEngine.RuntimePlatform.LinuxPlayer);
            s_gui.SetMode(s_launcher.OverlayMode);
            s_player = new PlayerMode(s_log, () => s_statusDirty = true, s_launcher);
            s_city = new CityLink(s_log, s_saveId, s_player);
            s_focus = new CityFocusLink(s_city, s_player);
            s_conditions = new CityConditionsLink(s_city, s_player);
            s_cityClock = new ClockLink(s_log);
            s_obstacles = new ObstacleLink(s_log);
            s_lights = new LightLink(s_log);
            s_water = new WaterLink(s_log);
            s_sky = new SkyLink(s_log);
            var pause = new PauseGate(s_log, s_player);
            s_player.EnterGate = (host, connected) => pause.Check() ?? s_city.EnterGate(host, connected);
            s_walkIn = new WalkInButton(s_log, s_player);
            s_pump.Updated += s_walkIn.Update;
            DigLink.Current = s_dig = new DigLink(s_log);
            s_city.EditChanged = s_dig.OnEdit;
            s_blocks = new BlockRenderer(s_log, s_launcher.BlockMaterial);
            s_pump.Updated += s_blocks.Update;
            s_entities = new Entities.EntityLink(s_log, s_blocks);
            s_selection = new SelectionOutline(() => s_player != null && s_player.IsActive);
            s_selfTest = new SelfTestController(s_log, s_player, s_launcher, s_blocks, s_gui, () => s_guest, ModVersion);
            s_autoload = new SaveAutoloader(s_log, s_launcher.Autoload);
            s_fixture = new FixtureBuilder(s_log);
            s_selfTest.City = s_city;
            s_selfTest.CurrentFixture = () => s_fixture.Fixture;
            s_selfTest.HoldAutoStart = () => s_fixture.Busy;
            s_unattended = new UnattendedPolicy(s_launcher.SelfTestQuit);
            s_selfTest.ReportWritten = () =>
            {
                s_unattended.ReportWritten(Now());
                if (s_launcher.SelfTestQuit) s_log.Info("unattended: self-test report written; quitting in " + UnattendedPolicy.QuitDelaySeconds + " s");
            };
            if (s_launcher.Autoload.Length > 0 || s_launcher.SelfTestQuit)
            {
                s_log.Info("unattended: autoload '" + s_launcher.Autoload + "', selftest_quit " + s_launcher.SelfTestQuit);
                // Nobody watches an unattended run: don't let a sleeping or locked display throttle frames
                // through vsync (owner's remote run 2026-10-05 ran at ~1 fps and never finished loading).
                // Runtime only, not saved in the game's settings. CS1 already runs in the background by default.
                UnityEngine.QualitySettings.vSyncCount = 0;
                UnityEngine.Application.targetFrameRate = 60;
                s_unattendedFps = true;
                s_log.Info("unattended: vsync off, target 60 fps for this session");
            }
            s_pump.LateUpdated += () => s_player.LateUpdate(s_host);
            // After the player's LateUpdate, which places the camera the sky is centred on.
            s_pump.LateUpdated += () => s_sky.LateUpdate(s_host, s_player.IsOn);
            // Graphics.DrawMesh queues for the coming render of every camera, so the pump's LateUpdate (after
            // the camera is placed) draws in city and Minecraft mode alike. Meshes are kept across city reloads (the
            // guest only resends changed sections) but drawn only while a city is loaded.
            s_pump.LateUpdated += () => s_blocks.LateUpdate(s_cityReady);
            s_pump.LateUpdated += () => s_entities.LateUpdate(s_cityReady);
            s_pump.LateUpdated += () => s_dig.LateUpdate(s_cityReady);
            s_pump.Gui += s_player.OnGui;
            s_pump.Updated += s_player.Viewer.Update;
            s_pump.Updated += new NetRenderDump(s_log, () => s_player != null && s_player.IsOn).Update;
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
            s_walkIn.Dispose();
            s_player.Viewer.Dispose();
            s_blocks.Dispose();
            s_entities.Dispose();
            s_sky.Dispose();
            s_selection.Dispose();
            s_gui.Dispose();
            s_probe.RestoreAll(why, true);
            s_dig.Dispose();
            DigLink.Current = s_dig = null;
            s_host.Shutdown(GoodbyeCodes.ShuttingDown, why);
            s_host = null;
            s_pump.Uninstall();
            s_pump = null;
            s_overlay = null;
            s_player = null;
            s_city = null;
            s_focus = null;
            s_conditions = null;
            s_walkIn = null;
            s_blocks = null;
            s_entities = null;
            s_sky = null;
            s_selection = null;
            s_gui = null;
            s_selfTest = null;
            s_autoload = null;
            s_fixture = null;
            s_unattended = null;
            s_launcher = null;
            s_guest = null;
            s_log.Close();
        }

        public static void OnLevelLoaded(string mode)
        {
            CityState.SetInCity(true);
            // No id is assigned on load: a city is only paired (after a verified backup) when the player enables
            // Minecraft for it (CityLink), so a city merely loaded with the mod enabled is saved exactly as without it.
            if (s_city != null) s_city.OnLevelLoaded(mode);
            if (s_city != null && s_dig != null) s_dig.Reload(s_city.SortedEdits());
            if (s_walkIn != null) s_walkIn.OnLevelLoaded(mode);
            if (s_fixture != null) s_fixture.OnLevelLoaded();
            if (s_selfTest != null) s_selfTest.OnLevelLoaded();
            if (s_autoloadIssued && s_unattended != null) s_unattended.AutoloadLevelLoaded(Now());
            if (s_autoloadIssued && s_launcher != null && s_launcher.SelfTestQuit)
            {
                try { SaveAutoloader.PauseAutosave(); Log("unattended: game autosave paused for this level (not persisted)"); }
                catch (Exception e) { LogError("pause autosave", e); }
            }
            Log("level loaded (" + mode + "); save id " + (s_saveId.LoadedFromSave ? s_saveId.Id.ToString() : "none (city not paired)"));
            s_statusDirty = true;
        }

        public static void OnLevelUnloading()
        {
            CityState.SetInCity(false);
            if (s_city != null) s_city.OnLevelUnloading(s_host);
            if (s_entities != null) s_entities.OnLevelUnloading();
            s_saveId.Clear();
            if (s_selfTest != null) s_selfTest.OnLevelUnloading(s_host);
            if (s_fixture != null) s_fixture.OnLevelUnloading();
            if (s_walkIn != null) s_walkIn.Dispose();
            if (s_player != null) s_player.Exit("city unloading", s_host, true);
            if (s_sky != null) s_sky.Reset();
            if (s_probe != null) s_probe.RestoreAll("level unloading", false);
            if (s_dig != null) s_dig.Unload();
            Log("level unloading");
            s_statusDirty = true;
        }

        public static void OnLoadData(ISerializableData data)
        {
            Log("load data: " + s_saveId.Load(data));
            CityLink city = s_city;
            if (city != null) city.OnLoadData(data);
        }

        public static void OnSaveData(ISerializableData data)
        {
            // Writes only if the city is paired (SaveIdentity.Save and CityLink.OnSaveData are no-ops otherwise).
            CityLink city = s_city;
            if (city != null) city.OnSaveData(data);
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

        // Unattended runs log their frame rate every 30 s (a crawling run is otherwise invisible remotely).
        private static bool s_unattendedFps;
        private static int s_fpsFrames;
        private static float s_fpsSince = -1f;

        private static void LogFps()
        {
            if (!s_unattendedFps) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (s_fpsSince < 0f) { s_fpsSince = now; s_fpsFrames = 0; return; }
            s_fpsFrames++;
            if (now - s_fpsSince >= 30f)
            {
                s_log.Info(string.Format("unattended: {0:0.0} fps over the last {1:0} s", s_fpsFrames / (now - s_fpsSince), now - s_fpsSince));
                s_fpsSince = now;
                s_fpsFrames = 0;
            }
        }

        // In Minecraft mode the screen is the first-person view plus Minecraft's HUD only (owner, 2026-10-06), so the
        // status box hides on entering and comes back on leaving as it was; F7 still toggles it meanwhile.
        private static bool s_wasPlayerActive;
        private static bool s_boxVisibleBeforePlayer;

        private static void HideStatusBoxInMinecraftMode()
        {
            bool active = s_player != null && s_player.IsActive;
            if (active == s_wasPlayerActive) return;
            s_wasPlayerActive = active;
            if (active)
            {
                s_boxVisibleBeforePlayer = s_overlay.Visible;
                s_overlay.Visible = false;
            }
            else
            {
                s_overlay.Visible = s_boxVisibleBeforePlayer;
            }
        }

        private static void Tick()
        {
            LogFps();
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
            s_cityReady = city.InCity && !city.Loading;
            Guid saveId = s_saveId.Id;
            s_launcher.Prewarm(city.InCity && !city.Loading, s_host.State == BridgeState.Connected);
            s_city.Update(s_host, city.InCity && !city.Loading);
            s_dig.Update(city.InCity && !city.Loading);
            s_cityClock.Update(s_host, city.InCity && !city.Loading, s_clock.Elapsed.TotalMilliseconds);
            s_player.Update(s_host, city.InCity && !city.Loading);
            s_focus.Update(s_host, UnityEngine.Time.realtimeSinceStartup);
            s_conditions.Update(s_host, s_clock.Elapsed.TotalSeconds);
            s_obstacles.Update(s_host, s_player, s_clock.Elapsed.TotalSeconds);
            s_lights.Update(s_host, s_player, s_clock.Elapsed.TotalSeconds);
            s_water.Update(s_host, s_player, s_clock.Elapsed.TotalSeconds);
            s_gui.Tick(s_host, s_player.IsOn);
            s_fixture.Update(city.InCity && !city.Loading);
            s_selfTest.Update(s_host, city.InCity && !city.Loading);
            Unattended();
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
            HideStatusBoxInMinecraftMode();

            s_tickWatch.Stop();
            double ms = s_tickWatch.Elapsed.TotalMilliseconds;
            s_ticks++;
            s_tickTotalMs += ms;
            if (ms > s_tickMaxMs)
            {
                s_tickMaxMs = ms;
            }
        }

        private static double Now()
        {
            return s_clock.Elapsed.TotalSeconds;
        }

        private static void Unattended()
        {
            try
            {
                SaveAutoloader.Outcome o = s_autoload.Update();
                if (o == SaveAutoloader.Outcome.Loaded) { s_autoloadIssued = true; s_unattended.AutoloadIssued(Now()); }
                else if (o == SaveAutoloader.Outcome.NotFound || o == SaveAutoloader.Outcome.Failed) s_unattended.AutoloadNotFound(Now());
                switch (s_unattended.Tick(Now()))
                {
                    case UnattendedAction.AbortSelfTest:
                        s_log.Warn("unattended: " + s_unattended.QuitReason + "; aborting the self-test");
                        s_selfTest.Abort("safety timeout");
                        break;
                    case UnattendedAction.Quit:
                        s_log.Info("unattended: quitting the game (" + s_unattended.QuitReason + "); no save");
                        SaveAutoloader.QuitGame();
                        break;
                }
            }
            catch (Exception e)
            {
                s_log.Error("unattended run", e);
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
                    s_city.OnDisconnect();
                    s_focus.Reset();
                    s_conditions.Reset();
                    s_player.SetGuestFlags(0);
                    s_gui.OnDisconnect();
                    s_selection.Hide();
                    s_sky.Reset();
                    s_entities.OnDisconnect();
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
                            s_player.SetGuestFlags(g.Flags);
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
                    else if (e.MessageType == AppProtocol.OverlayOfferType)
                    {
                        try
                        {
                            s_gui.OnOffer(OverlayOffer.Decode(e.Payload));
                        }
                        catch (ProtocolException ex)
                        {
                            s_log.Warn("bad OVERLAY_OFFER ignored: " + ex.Message);
                        }
                    }
                    else if (e.MessageType == AppProtocol.OverlayStopType)
                    {
                        s_gui.OnStop();
                    }
                    else if (e.MessageType == AppProtocol.BlockSelectionType)
                    {
                        try
                        {
                            s_selection.Set(BlockSelection.Decode(e.Payload));
                        }
                        catch (ProtocolException ex)
                        {
                            s_log.Warn("bad BLOCK_SELECTION ignored: " + ex.Message);
                        }
                    }
                    else if (e.MessageType == AppProtocol.TimeSetType && s_host.NegotiatedAppMinor >= 16)
                    {
                        try
                        {
                            s_cityClock.Set(TimeSet.Decode(e.Payload));
                        }
                        catch (ProtocolException ex)
                        {
                            s_log.Warn("bad TIME_SET ignored: " + ex.Message);
                        }
                    }
                    else if ((e.MessageType >= AppProtocol.BlockEditsType && e.MessageType <= AppProtocol.CityStateType)
                        || ((e.MessageType == AppProtocol.PlayerDataType || e.MessageType == AppProtocol.RespawnRequestType) && s_host.NegotiatedAppMinor >= 11)
                        || (e.MessageType == AppProtocol.TreeFelledType && s_host.NegotiatedAppMinor >= 12)
                        || (e.MessageType == AppProtocol.TreeGrownType && s_host.NegotiatedAppMinor >= 15)
                        || (e.MessageType == AppProtocol.CitizenEventsType && s_host.NegotiatedAppMinor >= 19)
                        || ((e.MessageType == AppProtocol.ShopOpenType || e.MessageType == AppProtocol.ShopTradeType) && s_host.NegotiatedAppMinor >= 20))
                    {
                        try
                        {
                            s_city.Handle(e.MessageType, e.Payload);
                        }
                        catch (ProtocolException ex)
                        {
                            s_log.Warn("bad city message 0x" + e.MessageType.ToString("x4") + " ignored: " + ex.Message);
                        }
                    }
                    else if (e.MessageType == AppProtocol.SkyStateType || e.MessageType == AppProtocol.SkyTexturesType)
                    {
                        try
                        {
                            s_sky.Handle(e.MessageType, e.Payload);
                        }
                        catch (ProtocolException ex)
                        {
                            s_log.Warn("bad sky message 0x" + e.MessageType.ToString("x4") + " ignored: " + ex.Message);
                        }
                    }
                    else if (Entities.EntityLink.Handles(e.MessageType) && s_host.NegotiatedAppMinor >= 14)
                    {
                        try
                        {
                            s_entities.Handle(e.MessageType, e.Payload);
                        }
                        catch (ProtocolException ex)
                        {
                            s_log.Warn("bad entity message 0x" + e.MessageType.ToString("x4") + " ignored: " + ex.Message);
                        }
                    }
                    else if (e.MessageType >= AppProtocol.BlockAtlasType && e.MessageType <= AppProtocol.SectionsClearType)
                    {
                        try
                        {
                            s_blocks.Handle(e.MessageType, e.Payload);
                        }
                        catch (ProtocolException ex)
                        {
                            s_log.Warn("bad block message 0x" + e.MessageType.ToString("x4") + " ignored: " + ex.Message);
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
            string blockLine = s_city.OverlayText(city.InCity);
            if (blockLine.Length > 0) sb.Append('\n').Append(blockLine);
            sb.Append('\n').Append(s_player.OverlayText());
            string gui = s_gui.OverlayText();
            if (gui.Length > 0) sb.Append('\n').Append(gui);
            string blocks = s_blocks.OverlayText();
            string entities = s_entities.OverlayText();
            if (blocks.Length > 0) sb.Append('\n').Append(blocks);
            if (entities.Length > 0) sb.Append('\n').Append(entities);
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
