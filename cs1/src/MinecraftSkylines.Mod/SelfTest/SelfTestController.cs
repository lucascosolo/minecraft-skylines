using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ColossalFramework;
using ColossalFramework.IO;
using ColossalFramework.Math;
using ColossalFramework.UI;
using MinecraftSkylines.Mod.Blocks;
using MinecraftSkylines.Mod.City;
using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Skylines.Core.Geometry;
using Skylines.Host;
using Skylines.Host.Camera;
using Skylines.Host.Geometry;
using Skylines.Host.Overlay;
using UnityEngine;
using UInput = UnityEngine.Input;

namespace MinecraftSkylines.Mod.SelfTest
{
    /// <summary>
    /// Milestone 2 acceptance test run inside the game: launch.cfg <c>selftest = city_load</c> or Ctrl+Shift+T in a
    /// loaded city, at most once per city load. Waits up to 240 s for Minecraft, then runs each scenario with a
    /// timeout, drives Minecraft mode through <see cref="PlayerMode"/>'s self-test API and writes
    /// <c>ModLogs/selftest/&lt;utc&gt;/report.json</c>. Reads the city only; never edits it or its save. Esc aborts.
    /// Whatever happens, the run ends with Minecraft mode off, synthetic keys cleared and the camera restored.
    /// Main thread only.
    /// </summary>
    internal sealed class SelfTestController
    {
        private const double LinkWaitSeconds = 240;
        private const double FreshStateMs = 2000;
        private const ushort RoadMask = NetGeometry.RoadSurfaceFlag | NetGeometry.BridgeDeckFlag;
        private static readonly int[] HoldW = { (int)KeyCode.W };
        private static readonly int[] NoKeys = new int[0];
        private static readonly int[] HoldE = { (int)KeyCode.E };
        private static readonly int[] HoldEscape = { (int)KeyCode.Escape };
        private static readonly double[] CurbEdges = { 0, 0.1, 0.25, 0.5, 1, 2 };

        private enum Phase { Idle, Waiting, Running }

        private readonly HostLog _log;
        private readonly PlayerMode _player;
        private readonly MinecraftLauncher _launcher;
        private readonly string _modVersion;
        private readonly BlockRenderer _blocks;
        private readonly OverlayLink _gui;
        private readonly Func<GuestStatus> _guest;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly TerrainSampler _terrain = new TerrainSampler();
        private readonly NetGeometry _net = new NetGeometry();
        private readonly CameraTakeover _shotCamera = new CameraTakeover();
        private readonly Dictionary<long, TriangleBuffer> _regions = new Dictionary<long, TriangleBuffer>();

        private Phase _phase;
        private bool _ranThisLoad;
        private bool _autoPending;
        private double _waitStart;
        private ScenarioRunner _runner;
        private SelfTestReport _report;
        private string _dir;
        private BridgeHost _host;
        private bool _cityReady;
        private bool _viewerWas;
        private bool _entered;
        private string _lastLine = "";

        // Camera state recorded before the first ENTER (S6).
        private bool _haveCamera;
        private Vector3 _camPos, _ctlTargetPos, _ctlCurrentPos;
        private Quaternion _camRot;
        private Vector2 _ctlTargetAngle, _ctlCurrentAngle;
        private float _camFov, _camNear, _camFar, _ctlTargetSize, _ctlCurrentSize;

        // Handed from S2 to S7.
        private bool _s2HaveSpawn;
        private Vector3 _s2Spawn, _s2RoadPoint;
        private string _s2McShot;
        private float _s2Yaw;

        // S8 state for its cleanup.
        private BlockRowPlan _s8Plan;
        private int _s8VariantWas = -1;

        public SelfTestController(HostLog log, PlayerMode player, MinecraftLauncher launcher, BlockRenderer blocks, OverlayLink gui, Func<GuestStatus> guest, string modVersion)
        {
            _gui = gui;
            _blocks = blocks;
            _guest = guest;
            _log = log;
            _player = player;
            _launcher = launcher;
            _modVersion = modVersion;
        }

        public bool Running { get { return _phase != Phase.Idle; } }

        /// <summary>Pairing and the city's edit set (S10).</summary>
        public CityLink City;

        /// <summary>Called after every run's report was written (finished, aborted or failed).</summary>
        public Action ReportWritten;

        /// <summary>The roads built for this run (new game started by autoload), or null: scenarios then search near the camera.</summary>
        public Func<Fixture> CurrentFixture;

        /// <summary>True while the automatic start must wait (the fixture is still being built).</summary>
        public Func<bool> HoldAutoStart;

        public void OnLevelLoaded()
        {
            _ranThisLoad = false;
            try { _autoPending = _launcher.SelfTest == SelfTestMode.CityLoad; }
            catch (Exception e) { _log.Error("self-test config", e); }
        }

        public void OnLevelUnloading(BridgeHost host)
        {
            _host = host;
            Abort("city unloading");
            _autoPending = false;
        }

        /// <summary>Per frame, after <see cref="PlayerMode.Update"/>. Never throws.</summary>
        public void Update(BridgeHost host, bool cityReady)
        {
            _host = host;
            _cityReady = cityReady;
            try
            {
                if (_phase == Phase.Idle)
                {
                    if (!cityReady || _ranThisLoad) return;
                    if (_autoPending && HoldAutoStart != null && HoldAutoStart()) return;
                    if (_autoPending || TriggerPressed()) Begin(_autoPending ? "launch.cfg selftest = city_load" : "Ctrl+Shift+T");
                    return;
                }
                if (UInput.GetKeyDown(KeyCode.Escape)) { Abort("Esc pressed"); return; }
                if (!cityReady) { Abort("city no longer loaded"); return; }
                if (_phase == Phase.Waiting)
                {
                    string why = NotReady();
                    double waited = Now() - _waitStart;
                    if (why == null) Start(null);
                    else if (waited > LinkWaitSeconds) Start("no Minecraft (" + why + " after " + LinkWaitSeconds + " s)");
                    else _lastLine = "Self-test: waiting for Minecraft (" + why + ", " + (int)waited + " s)  [Esc aborts]";
                    return;
                }
                _runner.Tick(Now());
                ScenarioResult cur = _runner.Current;
                _lastLine = cur == null ? _lastLine : "Self-test: running " + cur.Id + " " + cur.Name + "  [Esc aborts]";
                if (_runner.Finished) Finish();
            }
            catch (Exception e)
            {
                _log.Error("self-test update", e);
                Abort("internal error: " + e.Message);
            }
        }

        public string OverlayText { get { return _lastLine; } }

        /// <summary>Ends a run now (Esc, unload, mod disable) and writes its report. Never throws.</summary>
        public void Abort(string reason)
        {
            if (_phase == Phase.Idle) return;
            try
            {
                if (_runner == null) Start("aborted before start");
                _runner.Abort(reason, Now());
            }
            catch (Exception e) { _log.Error("self-test abort", e); }
            Finish();
        }

        private static bool TriggerPressed()
        {
            return UInput.GetKeyDown(KeyCode.T)
                && (UInput.GetKey(KeyCode.LeftControl) || UInput.GetKey(KeyCode.RightControl))
                && (UInput.GetKey(KeyCode.LeftShift) || UInput.GetKey(KeyCode.RightShift))
                && !UIView.HasModalInput() && !UIView.HasInputFocus();
        }

        private void Begin(string trigger)
        {
            _ranThisLoad = true;
            _autoPending = false;
            _phase = Phase.Waiting;
            _waitStart = Now();
            _runner = null;
            _report = new SelfTestReport
            {
                RunStartedUtc = DateTime.UtcNow,
                GameVersion = CityState.GameVersion,
                ModVersion = _modVersion,
                CityName = CityState.Capture().CityName,
            };
            _dir = Path.Combine(Path.Combine(Path.Combine(DataLocation.localApplicationData, "ModLogs"), "selftest"),
                _report.RunStartedUtc.ToString("yyyyMMdd'T'HHmmss'Z'"));
            _log.Info("self-test: started by " + trigger + "; output " + _dir);
        }

        private string NotReady()
        {
            if (_host == null || _host.State != BridgeState.Connected) return "link not connected";
            if (_host.NegotiatedAppMinor < 1) return "Minecraft app minor " + _host.NegotiatedAppMinor;
            if (_player.IsOn) return "Minecraft mode is on";
            Vector3 feet;
            uint flags;
            double age;
            if (!_player.TryLatestState(out feet, out flags, out age) || age > FreshStateMs) return "no PLAYER_STATE";
            if ((flags & PlayerStateFlags.InWorld) == 0) return "Minecraft has no world";
            if ((flags & PlayerStateFlags.Held) != 0) return "Minecraft is in its teleport hold";
            return null;
        }

        private void Start(string noMinecraft)
        {
            _phase = Phase.Running;
            _entered = false;
            _s2HaveSpawn = false;
            _s2McShot = null;
            _regions.Clear();
            Directory.CreateDirectory(_dir);
            if (_host != null && _host.State == BridgeState.Connected) _report.MinecraftPeer = _host.PeerName + " " + _host.PeerVersion;
            RecordCamera();
            _viewerWas = _player.Viewer.On;
            _log.Info("self-test: running" + (noMinecraft == null ? "" : "; Minecraft scenarios skipped: " + noMinecraft));
            var list = new List<Scenario>
            {
                Make("S1", "road surface accuracy", 60, S1RoadSurface, null),
                Mc("S2", "walk onto a ground road", 45, S2WalkOntoRoad, noMinecraft),
                Mc("S7", "screenshots", 20, S7Screenshots, noMinecraft),
                Mc("S9", "GUI overlay and screen input", 60, S9Overlay, noMinecraft),
                noMinecraft == null ? Make("S8", "block rendering", 45, S8BlockRendering, S8Cleanup) : Mc("S8", "block rendering", 45, null, noMinecraft),
                Mc("S3", "bridge railing", 45, S3BridgeRailing, noMinecraft),
                Mc("S4", "under a bridge", 45, S4UnderBridge, noMinecraft),
                Mc("S5", "slope following", 60, S5Slope, noMinecraft),
                Mc("S6", "camera restore", 15, S6CameraRestore, noMinecraft),
                Mc("S10", "city blocks: pairing, edits, save barrier", 60, S10CityBlocks, noMinecraft),
            };
            _runner = new ScenarioRunner(list, (id, e) => _log.Error("self-test " + id, e));
        }

        private Scenario Make(string id, string name, double timeout, Func<ScenarioResult, IEnumerator> body, Action cleanup)
        {
            return new Scenario { Id = id, Name = name, TimeoutSeconds = timeout, Body = body, Cleanup = cleanup };
        }

        private Scenario Mc(string id, string name, double timeout, Func<ScenarioResult, IEnumerator> body, string noMinecraft)
        {
            if (noMinecraft != null) return Make(id, name, timeout, r => SkipNow(r, noMinecraft), null);
            return Make(id, name, timeout, body, LeavePlayerMode);
        }

        private static IEnumerator SkipNow(ScenarioResult r, string why)
        {
            r.Skip(why);
            yield break;
        }

        private void LeavePlayerMode()
        {
            _player.SetSyntheticKeys(null);
            if (_player.IsOn) _player.Exit("self-test", _host, false);
            _shotCamera.Release();
        }

        private void Finish()
        {
            if (_phase == Phase.Idle) return;
            _phase = Phase.Idle;
            Safe("leave player mode", () => { _player.SetSyntheticKeys(null); if (_player.IsOn) _player.Exit("self-test finished", _host, false); });
            Safe("release screenshot camera", () => { string p = _shotCamera.Release(); if (p != null) _log.Warn("self-test camera restore incomplete: " + p); });
            Safe("collision viewer", () => _player.Viewer.On = _viewerWas);
            Safe("report", () =>
            {
                if (_runner != null)
                {
                    _report.Scenarios.Clear();
                    _report.Scenarios.AddRange(_runner.Results);
                    _report.Aborted = _runner.Aborted;
                    _report.AbortReason = _runner.AbortReason;
                }
                Directory.CreateDirectory(_dir);
                string path = Path.Combine(_dir, "report.json");
                File.WriteAllText(path, _report.ToJson());
                _lastLine = _report.SummaryLine(path);
                _log.Info(_lastLine);
            });
            _regions.Clear();
            _runner = null;
            if (ReportWritten != null) Safe("report callback", ReportWritten);
        }

        private void Safe(string what, Action step)
        {
            try { step(); }
            catch (Exception e) { _log.Error("self-test finish, " + what, e); }
        }

        private double Now()
        {
            return _clock.Elapsed.TotalSeconds;
        }

        // ---- S1 ----

        private IEnumerator S1RoadSurface(ScenarioResult r)
        {
            _regions.Clear();
            Fixture fx = Fx();
            Vector3 target = fx != null ? fx.Centre : CityTarget();
            List<ushort> ids = fx != null ? fx.Ground : Segments(target, 400f, NetGeometry.Kind.Ground, 20);
            r.Measurements.Set("fixture", fx != null);
            r.Measurements.Set("search_centre_m", Vec(target));
            r.Measurements.Set("segments_tested", ids.Count);
            if (ids.Count == 0) { r.Skip("no ground road segment within 400 m of the city camera's target"); yield break; }
            var diffs = new List<double>();
            var steps = new List<double>();
            int missing = 0;
            double worst = -1;
            object worstAt = null;
            NetSegment[] segs = Singleton<NetManager>.instance.m_segments.m_buffer;
            foreach (ushort id in ids)
            {
                Bezier3 left, right;
                Bezier3 centre = segs[id].GenerateBezier(id, segs[id].m_startNode, out left, out right);
                for (int i = 0; i < 5; i++)
                {
                    float t = 0.1f + 0.2f * i;
                    Vector3 c = centre.Position(t);
                    double? top = SurfaceAt(c.x, c.z, RoadMask, c.y, 2.0);
                    if (top == null) { missing++; continue; }
                    double d = top.Value - c.y;
                    diffs.Add(d);
                    if (Math.Abs(d) > worst)
                    {
                        worst = Math.Abs(d);
                        worstAt = new Dictionary<string, object> { { "segment_id", (int)id }, { "t", (double)t }, { "position_m", Vec(c) }, { "diff_m", d } };
                    }
                    Vector3 l = left.Position(t), rr = right.Position(t);
                    steps.Add(l.y - _terrain.Height(Out(l, rr).x, Out(l, rr).z));
                    steps.Add(rr.y - _terrain.Height(Out(rr, l).x, Out(rr, l).z));
                }
                yield return null;
            }
            Summary s = SelfTestMath.Summarize(diffs);
            Summary k = SelfTestMath.Summarize(steps);
            r.Measurements.Set("points_tested", diffs.Count + missing);
            r.Measurements.Set("points_missing_collision", missing);
            r.Measurements.Set("surface_diff_m", SummaryObject(s));
            r.Measurements.Set("worst_point", worstAt);
            r.Measurements.Set("tolerance_m", 0.05);
            r.Measurements.Set("curb_step_m", SummaryObject(k));
            r.Measurements.Set("curb_step_histogram_edges_m", CurbEdges);
            r.Measurements.Set("curb_step_histogram_counts", SelfTestMath.Histogram(steps, CurbEdges));
            if (missing > 0) r.Fail(missing + " sample points have no road collision within 2 m of the drawn surface");
            else if (s.MaxAbs > 0.05) r.Fail("collision top differs from the drawn surface by up to " + s.MaxAbs.Value.ToString("0.000") + " m");
            else r.Pass("");
        }

        // 1 m outward from edge point `e`, away from the opposite edge point `o`.
        private static Vector3 Out(Vector3 e, Vector3 o)
        {
            Vector3 d = new Vector3(e.x - o.x, 0f, e.z - o.z).normalized;
            return e + d;
        }

        // ---- S2 / S7 ----

        private IEnumerator S2WalkOntoRoad(ScenarioResult r)
        {
            _regions.Clear();
            NetSegment[] segs = Singleton<NetManager>.instance.m_segments.m_buffer;
            ushort chosen = 0;
            Vector3 spawn = Vector3.zero, centre = Vector3.zero, inward = Vector3.zero;
            float half = 0;
            Fixture fx = Fx();
            r.Measurements.Set("fixture", fx != null);
            foreach (ushort id in fx != null ? fx.Ground : Segments(CityTarget(), 400f, NetGeometry.Kind.Ground, 40))
            {
                Bezier3 left, right;
                Bezier3 c = segs[id].GenerateBezier(id, segs[id].m_startNode, out left, out right);
                Vector3 m = c.Position(0.5f), l = left.Position(0.5f), rr = right.Position(0.5f);
                float h = new Vector2(l.x - rr.x, l.z - rr.z).magnitude * 0.5f;
                if (h < 2f) continue;
                foreach (Vector3 edge in new[] { l, rr })
                {
                    Vector3 n = new Vector3(edge.x - m.x, 0f, edge.z - m.z).normalized;
                    Vector3 p = edge + n * 3f;
                    p.y = _terrain.Height(p.x, p.z);
                    if (Math.Abs(p.y - edge.y) > 1.5f || SurfaceAt(p.x, p.z, RoadMask, p.y, 2.5) != null) continue;
                    chosen = id; spawn = p; centre = m; inward = -n; half = h;
                    break;
                }
                if (chosen != 0) break;
            }
            if (chosen == 0) { r.Skip("no ground road within 400 m with clear terrain 3 m beside its edge"); yield break; }
            float yaw = (float)SelfTestMath.YawTowards(inward.x, inward.z);
            r.Measurements.Set("segment_id", (int)chosen);
            r.Measurements.Set("spawn_m", Vec(spawn));
            r.Measurements.Set("yaw_deg", (double)yaw);
            r.Measurements.Set("half_width_m", (double)half);
            _s2HaveSpawn = true; _s2Spawn = spawn; _s2RoadPoint = centre; _s2Yaw = yaw;
            _player.Viewer.On = true;

            string error = null;
            IEnumerator enter = EnterAndSettle(r, spawn, yaw, e => error = e);
            while (enter.MoveNext()) yield return null;
            if (error != null) { r.Fail(error); yield break; }

            // Hold W until the feet reach the centre line (3 m + half width) or 2.5 s pass.
            double start = Now();
            _player.SetSyntheticKeys(HoldW);
            while (Now() - start < 2.5)
            {
                Vector3 f = Feet();
                if (new Vector2(f.x - spawn.x, f.z - spawn.z).magnitude >= 3f + half) break;
                yield return null;
            }
            _player.SetSyntheticKeys(NoKeys);
            r.Measurements.Set("walk_s", Now() - start);
            IEnumerator settle = Wait(0.6);
            while (settle.MoveNext()) yield return null;

            Vector3 feet = Feet();
            double? top = SurfaceAt(feet.x, feet.z, RoadMask, feet.y, 1.0);
            double lateral = SelfTestMath.LateralDistance(Polyline(segs, chosen), feet.x, feet.z);
            r.Measurements.Set("final_feet_m", Vec(feet));
            r.Measurements.Set("road_top_m", top);
            r.Measurements.Set("height_diff_m", top.HasValue ? feet.y - top.Value : (double?)null);
            r.Measurements.Set("lateral_from_centre_m", lateral);
            r.Measurements.Set("on_ground", OnGround());

            _s2McShot = Path.Combine(_dir, "s2_minecraft_mode.png");
            Application.CaptureScreenshot(_s2McShot);
            IEnumerator shot = Wait(0.5);
            while (shot.MoveNext()) yield return null;

            if (!top.HasValue) r.Fail("no road collision within 1 m of the feet");
            else if (!SelfTestMath.Within(feet.y, top, 0.15)) r.Fail("feet " + (feet.y - top.Value).ToString("0.00") + " m from the road top");
            else if (lateral > half) r.Fail("feet " + lateral.ToString("0.00") + " m from the centre line, outside the road");
            else r.Pass("");
        }

        private IEnumerator S7Screenshots(ScenarioResult r)
        {
            if (!_s2HaveSpawn) { r.Skip("S2 found no road to photograph"); yield break; }
            _player.Viewer.On = true;
            // City camera 15 m from the road point, above the S2 spawn side, looking at it.
            Vector3 away = new Vector3(_s2Spawn.x - _s2RoadPoint.x, 0f, _s2Spawn.z - _s2RoadPoint.z).normalized;
            Vector3 eye = _s2RoadPoint + away * 12f + Vector3.up * 9f;
            string refusal = _shotCamera.Acquire();
            if (refusal != null) { r.Fail("could not take the city camera: " + refusal); yield break; }
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            float fov = cam.fieldOfView, near = cam.nearClipPlane;
            Quaternion look = Quaternion.LookRotation(_s2RoadPoint - eye);
            double start = Now();
            while (Now() - start < 0.3) { _shotCamera.Drive(eye, look, fov, near); yield return null; }
            string city = Path.Combine(_dir, "s2_city_camera.png");
            _shotCamera.Drive(eye, look, fov, near);
            Application.CaptureScreenshot(city);
            start = Now();
            while (Now() - start < 0.5) { _shotCamera.Drive(eye, look, fov, near); yield return null; }
            string problem = _shotCamera.Release();
            if (problem != null) _log.Warn("self-test camera restore incomplete: " + problem);
            IEnumerator wait = Wait(1.5);
            while (wait.MoveNext()) yield return null;

            bool mcOk = _s2McShot != null && File.Exists(_s2McShot), cityOk = File.Exists(city);
            r.Measurements.Set("camera_m", Vec(eye));
            r.Measurements.Set("minecraft_mode_png", _s2McShot);
            r.Measurements.Set("minecraft_mode_png_exists", mcOk);
            r.Measurements.Set("city_camera_png", city);
            r.Measurements.Set("city_camera_png_exists", cityOk);
            if (mcOk && cityOk) r.Pass("");
            else r.Fail("screenshot missing: " + (mcOk ? "" : "minecraft_mode ") + (cityOk ? "" : "city_camera"));
        }

        // ---- S9 ----

        private IEnumerator S9Overlay(ScenarioResult r)
        {
            if (_host.NegotiatedAppMinor < 3) { r.Skip("Minecraft app minor " + _host.NegotiatedAppMinor + " has no GUI overlay"); yield break; }
            if (!_s2HaveSpawn) { r.Skip("S2 found no spawn point"); yield break; }
            string error = null;
            IEnumerator enter = EnterAndSettle(r, _s2Spawn, _s2Yaw, e => error = e);
            while (enter.MoveNext()) yield return null;
            if (error != null) { r.Fail(error); yield break; }
            var steps = new Dictionary<string, object>();
            r.Measurements.Set("steps", steps);

            // 1. An offer is mapped and at least 10 new frames arrive within 10 s.
            double start = Now(), t0 = -1;
            long f0 = 0;
            while (Now() - start < 10)
            {
                if (_gui.Mapped)
                {
                    if (t0 < 0) { t0 = Now(); f0 = _gui.FramesAcquired; }
                    else if (_gui.FramesAcquired - f0 >= 10) break;
                }
                yield return null;
            }
            long frames = t0 < 0 ? 0 : _gui.FramesAcquired - f0;
            OverlayPresenter p = _gui.Presenter;
            r.Measurements.Set("offers", _gui.Offers);
            r.Measurements.Set("frames_seen", frames);
            r.Measurements.Set("frames_per_s", t0 < 0 ? 0 : SelfTestMath.PerSecond(frames, Now() - t0));
            r.Measurements.Set("frame_width_px", p.Width);
            r.Measurements.Set("frame_height_px", p.Height);
            r.Measurements.Set("screen_px", new[] { Screen.width, Screen.height });
            r.Measurements.Set("upload_avg_ms", p.Uploads == 0 ? 0 : p.TotalUploadMs / p.Uploads);
            r.Measurements.Set("upload_max_ms", p.MaxUploadMs);
            r.Measurements.Set("blend", p.BlendDescription);
            steps["offer_and_10_frames"] = frames >= 10;
            if (frames < 10) { r.Fail(_gui.Mapped ? "only " + frames + " overlay frames in 10 s" : "no OVERLAY_OFFER mapped within 10 s"); yield break; }

            // 2. Screenshot with the HUD visible.
            string hud = Path.Combine(_dir, "s9_hud.png");
            Application.CaptureScreenshot(hud);
            IEnumerator wait = Wait(0.5);
            while (wait.MoveNext()) yield return null;

            // 3. E opens the inventory: SCREEN_OPEN, cursor free.
            _player.SetSyntheticKeys(HoldE);
            wait = Wait(0.15);
            while (wait.MoveNext()) yield return null;
            _player.SetSyntheticKeys(NoKeys);
            start = Now();
            while (!_player.ScreenMode && Now() - start < 3) yield return null;
            steps["inventory_screen_open"] = _player.ScreenMode;
            r.Measurements.Set("screen_open_after_s", Now() - start);
            if (!_player.ScreenMode) { r.Fail("SCREEN_OPEN not set within 3 s of pressing E"); yield break; }
            steps["cursor_free"] = Cursor.visible && Cursor.lockState == CursorLockMode.None;

            // 4. Cursor to the screen centre, screenshot.
            _player.SetSyntheticCursor(Screen.width / 2, Screen.height / 2);
            wait = Wait(0.5);
            while (wait.MoveNext()) yield return null;
            string inv = Path.Combine(_dir, "s9_inventory.png");
            Application.CaptureScreenshot(inv);
            wait = Wait(0.5);
            while (wait.MoveNext()) yield return null;

            // 5. Esc closes the screen and leaves Minecraft mode on.
            _player.SetSyntheticKeys(HoldEscape);
            wait = Wait(0.15);
            while (wait.MoveNext()) yield return null;
            _player.SetSyntheticKeys(NoKeys);
            start = Now();
            while (_player.ScreenMode && _player.IsOn && Now() - start < 3) yield return null;
            wait = Wait(0.5);
            while (wait.MoveNext()) yield return null;
            steps["screen_closed_by_esc"] = !_player.ScreenMode;
            steps["still_in_minecraft_mode"] = _player.IsOn;
            steps["cursor_locked_again"] = !Cursor.visible && Cursor.lockState == CursorLockMode.Locked;
            bool hudOk = File.Exists(hud), invOk = File.Exists(inv);
            steps["screenshots_written"] = hudOk && invOk;
            r.Measurements.Set("hud_png", hud);
            r.Measurements.Set("inventory_png", inv);

            var failed = new List<string>();
            foreach (KeyValuePair<string, object> kv in steps) if (!(bool)kv.Value) failed.Add(kv.Key);
            if (failed.Count == 0) r.Pass("");
            else r.Fail("failed steps: " + string.Join(", ", failed.ToArray()));
        }

        // ---- S8 ----

        private const string DevWorldName = "skylines-dev";
        // M4 replaced the dev world with the per-city cache world; S8 accepts either.
        private const string CityWorldName = "skylines-city";
        private const string DebugCommandsArg = "-PmcskylinesDebugCommands";

        private IEnumerator S8BlockRendering(ScenarioResult r)
        {
            if (_host.NegotiatedAppMinor < 2) { r.Skip("Minecraft app minor " + _host.NegotiatedAppMinor + " has no block meshes"); yield break; }
            if (!_launcher.HasArg(DebugCommandsArg)) { r.Skip("launch.cfg args lack " + DebugCommandsArg + " (tools/install-cs1-mod.sh --selftest)"); yield break; }
            GuestStatus g = _guest();
            if (g == null || (g.WorldName != DevWorldName && g.WorldName != CityWorldName)) { r.Skip("Minecraft world is not " + DevWorldName + " or " + CityWorldName); yield break; }
            if (!_s2HaveSpawn) { r.Skip("S2 found no spawn point"); yield break; }

            float yaw = (float)BlockRowPlan.SnapYaw(_s2Yaw);
            string error = null;
            IEnumerator enter = EnterAndSettle(r, _s2Spawn, yaw, e => error = e);
            while (enter.MoveNext()) yield return null;
            if (error != null) { r.Fail(error); yield break; }

            Vector3 feet = Feet();
            Vec3d mc = MinecraftFrame.CsToMc(new Vec3d(feet.x, feet.y, feet.z));
            BlockRowPlan plan = BlockRowPlan.Create(mc.X, mc.Y, mc.Z, yaw, 3);
            _s8Plan = plan;
            int rowSections = 0, rowVertices = 0;
            double firstMesh = -1, sent = Now();
            Action<int, int, int, int> onSection = (sx, sy, sz, n) =>
            {
                if (!plan.Covers(sx, sy, sz) || n == 0) return;
                rowSections++;
                rowVertices += n;
                if (firstMesh < 0) firstMesh = Now() - sent;
            };
            long sections0 = _blocks.SectionsReceived, vertices0 = _blocks.VerticesReceived;
            double build0 = _blocks.Store.TotalBuildMs;
            int errors0 = _blocks.Errors;
            _blocks.SectionReceived += onSection;
            try
            {
                foreach (string c in plan.BuildCommands()) SendCommand(c);
                r.Measurements.Set("commands", plan.BuildCommands());
                while (firstMesh < 0 && Now() - sent < 10) yield return null;
                double settle = Now();
                while (_blocks.Store.PendingCount > 0 && Now() - settle < 2) yield return null;
            }
            finally { _blocks.SectionReceived -= onSection; }

            r.Measurements.Set("atlas_width_px", _blocks.AtlasWidth);
            r.Measurements.Set("atlas_height_px", _blocks.AtlasHeight);
            r.Measurements.Set("time_to_first_mesh_s", firstMesh < 0 ? (double?)null : firstMesh);
            r.Measurements.Set("row_sections_received", rowSections);
            r.Measurements.Set("row_vertices_received", rowVertices);
            r.Measurements.Set("sections_received", _blocks.SectionsReceived - sections0);
            r.Measurements.Set("vertices_received", _blocks.VerticesReceived - vertices0);
            r.Measurements.Set("mesh_build_total_ms", _blocks.Store.TotalBuildMs - build0);
            r.Measurements.Set("mesh_build_max_frame_ms", _blocks.Store.MaxFrameBuildMs);
            r.Measurements.Set("meshes_drawn", _blocks.Store.Count);
            if (firstMesh < 0) { r.Fail("no SECTION_MESH covering the row within 10 s (debug commands off in Minecraft?)"); yield break; }

            // Blocks are solid in Minecraft: walking at the row must stop the player in front of it.
            double start = Now();
            _player.SetSyntheticKeys(HoldW);
            while (Now() - start < 1.5) yield return null;
            _player.SetSyntheticKeys(NoKeys);
            IEnumerator wait = Wait(0.5);
            while (wait.MoveNext()) yield return null;
            Vector3 after = Feet();
            Vec3d mcAfter = MinecraftFrame.CsToMc(new Vec3d(after.x, after.y, after.z));
            double gapBefore = plan.GapToRow(mc.X, mc.Z), gap = plan.GapToRow(mcAfter.X, mcAfter.Z);
            bool stopped = gap >= -0.05 && gap <= 0.15;
            r.Measurements.Set("gap_before_walk_m", gapBefore);
            r.Measurements.Set("gap_after_walk_m", gap);
            r.Measurements.Set("stopped_in_front", stopped);
            _player.Exit("self-test", _host, false);
            wait = Wait(1.0);
            while (wait.MoveNext()) yield return null;

            // One screenshot per material variant from a fixed city-camera pose 6 m in front of the row.
            Vector3 centre = new Vector3((plan.BlockX[2] + plan.BlockX[3]) * 0.5f + 0.5f, plan.FeetBlockY + 1f, -((plan.BlockZ[2] + plan.BlockZ[3]) * 0.5f + 0.5f));
            Vector3 forward = new Vector3(plan.ForwardX, 0f, -plan.ForwardZ);
            Vector3 eye = centre - forward * 6f + Vector3.up * 1.5f;
            Quaternion look = Quaternion.LookRotation(centre - eye);
            string refusal = _shotCamera.Acquire();
            if (refusal != null) { r.Fail("could not take the city camera: " + refusal); yield break; }
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            float fov = cam.fieldOfView, near = 0.1f;
            _s8VariantWas = _blocks.Variant;
            var shots = new List<string>();
            for (int v = 0; v < BlockRenderer.VariantCount; v++)
            {
                _blocks.SetVariant(v);
                for (int f = 0; f < 2; f++) { _shotCamera.Drive(eye, look, fov, near); yield return null; }
                _shotCamera.Drive(eye, look, fov, near);
                string path = Path.Combine(_dir, "s8_material_" + v + ".png");
                Application.CaptureScreenshot(path);
                shots.Add(path);
                start = Now();
                while (Now() - start < 0.3) { _shotCamera.Drive(eye, look, fov, near); yield return null; }
            }
            _blocks.SetVariant(_s8VariantWas);
            _s8VariantWas = -1;
            string problem = _shotCamera.Release();
            if (problem != null) _log.Warn("self-test camera restore incomplete: " + problem);
            wait = Wait(1.0);
            while (wait.MoveNext()) yield return null;

            int missing = 0;
            foreach (string p in shots) if (!File.Exists(p)) missing++;
            r.Measurements.Set("camera_m", Vec(eye));
            r.Measurements.Set("screenshots", shots);
            r.Measurements.Set("screenshots_missing", missing);
            r.Measurements.Set("draw_errors", _blocks.Errors - errors0);
            if (_blocks.Errors != errors0) r.Fail("block drawing threw " + (_blocks.Errors - errors0) + " times: " + _blocks.LastError);
            else if (!stopped) r.Fail("walking at the row left the player " + gap.ToString("0.00") + " m from it (expected 0 to 0.15)");
            else if (missing > 0) r.Fail(missing + " screenshots missing");
            else r.Pass("");
        }

        private void S8Cleanup()
        {
            LeavePlayerMode();
            if (_s8VariantWas >= 0) { _blocks.SetVariant(_s8VariantWas); _s8VariantWas = -1; }
            if (_s8Plan != null) { SendCommand(_s8Plan.ClearCommand()); _s8Plan = null; }
        }

        // S10: pairs a city started from a map (no backup: nothing to protect; CityLink logs why), waits for the guest to
        // have applied the open, places and breaks blocks through DEBUG_COMMAND high above the player, checks the edit
        // set follows, runs one EDIT_SYNC round trip, and removes its blocks again.
        private IEnumerator S10CityBlocks(ScenarioResult r)
        {
            if (_host.NegotiatedAppMinor < 5) { r.Skip("Minecraft app minor " + _host.NegotiatedAppMinor + " has no per-city blocks"); yield break; }
            if (City == null) { r.Error("no city link"); yield break; }
            if (!_launcher.HasArg(DebugCommandsArg)) { r.Skip("launch.cfg args lack " + DebugCommandsArg + " (tools/install-cs1-mod.sh --selftest)"); yield break; }
            bool wasPaired = City.Paired;
            string refusal = City.PairForSelfTest();
            r.Measurements.Set("was_paired", wasPaired);
            if (refusal != null) { r.Skip(refusal); yield break; }

            double start = Now();
            while (!City.Ready && Now() - start < 20) yield return null;
            r.Measurements.Set("open_seq", City.OpenSeq);
            r.Measurements.Set("time_to_ready_s", City.Ready ? (double?)(Now() - start) : null);
            if (!City.Ready) { r.Fail("no CITY_STATE ready within 20 s of pairing"); yield break; }

            Vector3 feet;
            uint flags;
            double age;
            int x = 0, y = 200, z = 0;
            if (_player.TryLatestState(out feet, out flags, out age))
            {
                Vec3d mc = MinecraftFrame.CsToMc(new Vec3d(feet.x, feet.y, feet.z));
                x = (int)Math.Floor(mc.X) + 4;
                y = Math.Min((int)Math.Floor(mc.Y) + 40, 300);
                z = (int)Math.Floor(mc.Z) + 4;
            }
            int before = City.EditCount;
            r.Measurements.Set("block_origin", new[] { x, y, z });
            r.Measurements.Set("edits_before", before);
            SendCommand("setblock " + x + " " + y + " " + z + " minecraft:stone");
            SendCommand("fill " + (x + 1) + " " + y + " " + z + " " + (x + 3) + " " + y + " " + z + " minecraft:oak_planks");
            start = Now();
            while (!S10Has(x, y, z, x + 3) && Now() - start < 10) yield return null;
            bool placed = S10Has(x, y, z, x + 3);
            r.Measurements.Set("placed_seen_s", placed ? (double?)(Now() - start) : null);
            r.Measurements.Set("edits_after_place", City.EditCount);

            SendCommand("setblock " + x + " " + y + " " + z + " minecraft:air");
            start = Now();
            string state;
            while (City.TryGetEdit(x, y, z, out state) && Now() - start < 10) yield return null;
            bool broken = !City.TryGetEdit(x, y, z, out state);
            r.Measurements.Set("break_seen_s", broken ? (double?)(Now() - start) : null);
            r.Measurements.Set("edits_after_break", City.EditCount);

            uint token = City.SendEditSync(_host);
            start = Now();
            while (!City.SyncAcked(token) && Now() - start < 2) yield return null;
            bool acked = City.SyncAcked(token);
            r.Measurements.Set("sync_token", token);
            r.Measurements.Set("sync_round_trip_ms", acked ? (double?)((Now() - start) * 1000) : null);

            SendCommand("fill " + x + " " + y + " " + z + " " + (x + 3) + " " + y + " " + z + " minecraft:air");
            start = Now();
            while (City.EditCount != before && Now() - start < 10) yield return null;
            r.Measurements.Set("edits_after_cleanup", City.EditCount);

            if (!placed) r.Fail("the edit set did not receive the placed blocks within 10 s");
            else if (!broken) r.Fail("the edit set still holds the broken block after 10 s");
            else if (!acked) r.Fail("EDIT_SYNC " + token + " not acknowledged within 2 s");
            else if (City.EditCount != before) r.Fail("cleanup left " + (City.EditCount - before) + " edits");
            else r.Pass("placed 4, broke 1, edit sync acknowledged");
        }

        private bool S10Has(int x, int y, int z, int toX)
        {
            string state;
            if (!City.TryGetEdit(x, y, z, out state) || state != "minecraft:stone") return false;
            for (int i = x + 1; i <= toX; i++)
                if (!City.TryGetEdit(i, y, z, out state) || state != "minecraft:oak_planks") return false;
            return true;
        }

        private void SendCommand(string command)
        {
            if (_host == null || _host.State != BridgeState.Connected) return;
            _host.Send(AppProtocol.DebugCommandType, new DebugCommand { Command = command }.Encode());
            _log.Info("self-test: DEBUG_COMMAND " + command);
        }

        // ---- S3 ----

        private IEnumerator S3BridgeRailing(ScenarioResult r)
        {
            _regions.Clear();
            NetSegment[] segs = Singleton<NetManager>.instance.m_segments.m_buffer;
            ushort chosen = 0;
            Vector3 spawn = Vector3.zero, outward = Vector3.zero;
            float half = 0;
            Fixture fx = Fx();
            r.Measurements.Set("fixture", fx != null);
            foreach (ushort id in fx != null ? fx.Raised : Segments(CityTarget(), 600f, NetGeometry.Kind.Bridge, 20))
            {
                Bezier3 left, right;
                Bezier3 c = segs[id].GenerateBezier(id, segs[id].m_startNode, out left, out right);
                Vector3 m = c.Position(0.5f), l = left.Position(0.5f), rr = right.Position(0.5f);
                float h = new Vector2(l.x - rr.x, l.z - rr.z).magnitude * 0.5f;
                double? top = SurfaceAt(m.x, m.z, NetGeometry.BridgeDeckFlag, m.y, 1.5);
                if (h < 1.5f || top == null) continue;
                chosen = id; half = h;
                spawn = new Vector3(m.x, (float)top.Value, m.z);
                outward = new Vector3(l.x - m.x, 0f, l.z - m.z).normalized;
                break;
            }
            if (chosen == 0) { r.Skip("no bridge or elevated segment within 600 m"); yield break; }
            float yaw = (float)SelfTestMath.YawTowards(outward.x, outward.z);
            r.Measurements.Set("segment_id", (int)chosen);
            r.Measurements.Set("spawn_m", Vec(spawn));
            r.Measurements.Set("deck_top_m", (double)spawn.y);
            r.Measurements.Set("half_width_m", (double)half);
            r.Measurements.Set("yaw_deg", (double)yaw);

            string error = null;
            IEnumerator enter = EnterAndSettle(r, spawn, yaw, e => error = e);
            while (enter.MoveNext()) yield return null;
            if (error != null) { r.Fail(error); yield break; }
            _player.SetSyntheticKeys(HoldW);
            IEnumerator walk = Wait(3.0);
            while (walk.MoveNext()) yield return null;
            _player.SetSyntheticKeys(NoKeys);
            IEnumerator settle = Wait(0.6);
            while (settle.MoveNext()) yield return null;

            Vector3 feet = Feet();
            double? top2 = SurfaceAt(feet.x, feet.z, NetGeometry.BridgeDeckFlag, feet.y, 1.0);
            double lateral = SelfTestMath.LateralDistance(Polyline(segs, chosen), feet.x, feet.z);
            r.Measurements.Set("final_feet_m", Vec(feet));
            r.Measurements.Set("deck_top_at_feet_m", top2);
            r.Measurements.Set("lateral_from_centre_m", lateral);
            r.Measurements.Set("drop_m", (double)(spawn.y - feet.y));
            if (!top2.HasValue || !SelfTestMath.Within(feet.y, top2, 0.15))
                r.Fail("player is not on the deck (feet y " + feet.y.ToString("0.00") + ", deck top at spawn " + spawn.y.ToString("0.00") + ")");
            else if (lateral > half) r.Fail("feet " + lateral.ToString("0.00") + " m from the centre line, past the edge");
            else r.Pass("");
        }

        // ---- S4 ----

        private IEnumerator S4UnderBridge(ScenarioResult r)
        {
            _regions.Clear();
            NetSegment[] segs = Singleton<NetManager>.instance.m_segments.m_buffer;
            bool found = false;
            Vector3 spawn = Vector3.zero, dir = Vector3.zero;
            double clearance = 0;
            int chosen = 0;
            Fixture fx = Fx();
            r.Measurements.Set("fixture", fx != null);
            foreach (ushort id in fx != null ? fx.Raised : Segments(CityTarget(), 600f, NetGeometry.Kind.Bridge, 30))
            {
                Bezier3 left, right;
                Bezier3 c = segs[id].GenerateBezier(id, segs[id].m_startNode, out left, out right);
                for (int i = 2; i <= 8 && !found; i++)
                {
                    float t = i / 10f;
                    Vector3 m = c.Position(t);
                    float ground = _terrain.Height(m.x, m.z);
                    double? top = SurfaceAt(m.x, m.z, NetGeometry.BridgeDeckFlag, m.y, 1.5);
                    if (top == null || top.Value - ground < 4.0) continue;
                    if (SurfaceAt(m.x, m.z, NetGeometry.RoadSurfaceFlag, ground, 2.0) != null) continue;
                    found = true;
                    chosen = id;
                    clearance = top.Value - ground;
                    spawn = new Vector3(m.x, ground, m.z);
                    Vector3 l = left.Position(t);
                    dir = new Vector3(l.x - m.x, 0f, l.z - m.z).normalized;
                }
                if (found) break;
            }
            if (!found) { r.Skip("no bridge deck at least 4 m above clear terrain within 600 m"); yield break; }
            float yaw = (float)SelfTestMath.YawTowards(dir.x, dir.z);
            r.Measurements.Set("segment_id", chosen);
            r.Measurements.Set("spawn_m", Vec(spawn));
            r.Measurements.Set("deck_above_terrain_m", clearance);
            r.Measurements.Set("yaw_deg", (double)yaw);

            string error = null;
            IEnumerator enter = EnterAndSettle(r, spawn, yaw, e => error = e);
            while (enter.MoveNext()) yield return null;
            if (error != null) { r.Fail(error); yield break; }
            var diffs = new List<double>();
            double start = Now();
            _player.SetSyntheticKeys(HoldW);
            while (Now() - start < 1.0)
            {
                Vector3 f = Feet();
                diffs.Add(f.y - _terrain.Height(f.x, f.z));
                yield return null;
            }
            _player.SetSyntheticKeys(NoKeys);
            Vector3 feet = Feet();
            diffs.Add(feet.y - _terrain.Height(feet.x, feet.z));
            Summary s = SelfTestMath.Summarize(diffs);
            r.Measurements.Set("final_feet_m", Vec(feet));
            r.Measurements.Set("feet_minus_terrain_m", SummaryObject(s));
            if (s.MaxAbs > 0.3) r.Fail("feet left the terrain by up to " + s.MaxAbs.Value.ToString("0.00") + " m");
            else r.Pass("");
        }

        // ---- S5 ----

        private IEnumerator S5Slope(ScenarioResult r)
        {
            _regions.Clear();
            Vector3 target = CityTarget();
            Vector3 start = Vector3.zero, up = Vector3.zero;
            double slope = 0;
            bool found = false;
            int checkedPaths = 0;
            Fixture fx = Fx();
            r.Measurements.Set("fixture", fx != null);
            if (fx != null && fx.HasSlope)
            {
                found = true;
                slope = fx.Slope.Degrees;
                up = new Vector3((float)fx.Slope.UpX, 0f, (float)fx.Slope.UpZ);
                start = new Vector3((float)fx.Slope.X, 0f, (float)fx.Slope.Z) - up * 4.5f;
                start.y = _terrain.Height(start.x, start.z);
            }
            for (int ring = 0; ring <= 18 && !found && checkedPaths < 12; ring++)
            {
                for (int gx = -ring; gx <= ring && !found && checkedPaths < 12; gx++)
                {
                    for (int gz = -ring; gz <= ring && !found && checkedPaths < 12; gz++)
                    {
                        if (Math.Max(Math.Abs(gx), Math.Abs(gz)) != ring) continue;
                        float x = target.x + gx * 16f, z = target.z + gz * 16f;
                        Vector3 grad;
                        double s = Slope(x, z, out grad);
                        if (s < 10 || s > 30) continue;
                        checkedPaths++;
                        if (!PathOnClearSlope(x, z, grad)) continue;
                        found = true;
                        slope = s;
                        up = grad;
                        start = new Vector3(x, 0f, z) - grad * 4.5f;
                        start.y = _terrain.Height(start.x, start.z);
                    }
                }
                yield return null;
            }
            if (!found) { r.Skip("no clear 10-30 degree terrain slope within 300 m"); yield break; }
            float yaw = (float)SelfTestMath.YawTowards(up.x, up.z);
            r.Measurements.Set("spawn_m", Vec(start));
            r.Measurements.Set("slope_deg", slope);
            r.Measurements.Set("yaw_up_deg", (double)yaw);

            string error = null;
            IEnumerator enter = EnterAndSettle(r, start, yaw, e => error = e);
            while (enter.MoveNext()) yield return null;
            if (error != null) { r.Fail(error); yield break; }
            var diffs = new List<double>();
            var air = new AirborneTracker();
            double t0 = Now(), nextSample = t0;
            bool turned = false;
            _player.SetSyntheticKeys(HoldW);
            while (Now() - t0 < 4.0)
            {
                double now = Now();
                if (!turned && now - t0 >= 2.0) { _player.SetLook(yaw + 180.0, 0); turned = true; }
                air.Sample(now, OnGround());
                if (now >= nextSample)
                {
                    Vector3 f = Feet();
                    diffs.Add(f.y - _terrain.Height(f.x, f.z));
                    nextSample += 0.25;
                }
                yield return null;
            }
            _player.SetSyntheticKeys(NoKeys);
            Summary s2 = SelfTestMath.Summarize(diffs);
            r.Measurements.Set("final_feet_m", Vec(Feet()));
            r.Measurements.Set("feet_minus_terrain_m", SummaryObject(s2));
            r.Measurements.Set("feet_minus_terrain_samples_m", diffs.ToArray());
            r.Measurements.Set("longest_airborne_s", air.LongestSeconds);
            if (s2.MaxAbs > 0.25) r.Fail("feet off the slope by up to " + s2.MaxAbs.Value.ToString("0.00") + " m");
            else if (air.LongestSeconds > 0.5) r.Fail("off the ground for " + air.LongestSeconds.ToString("0.00") + " s without jumping");
            else r.Pass("");
        }

        private double Slope(float x, float z, out Vector3 uphill)
        {
            const float step = 2f;
            float hx0 = _terrain.Height(x - step, z), hx1 = _terrain.Height(x + step, z);
            float hz0 = _terrain.Height(x, z - step), hz1 = _terrain.Height(x, z + step);
            uphill = new Vector3(hx1 - hx0, 0f, hz1 - hz0).normalized;
            return SelfTestMath.SlopeDegrees(hx0, hx1, hz0, hz1, step);
        }

        // Every 1.5 m from 9 m below to 9 m above (x, z) along the uphill direction: 7-33 degrees and no road surface.
        private bool PathOnClearSlope(float x, float z, Vector3 uphill)
        {
            for (float d = -9f; d <= 9f; d += 1.5f)
            {
                float px = x + uphill.x * d, pz = z + uphill.z * d;
                Vector3 g;
                double s = Slope(px, pz, out g);
                if (s < 7 || s > 33) return false;
                if (SurfaceAt(px, pz, RoadMask, _terrain.Height(px, pz), 3.0) != null) return false;
            }
            return true;
        }

        // ---- S6 ----

        private IEnumerator S6CameraRestore(ScenarioResult r)
        {
            if (!_entered) { r.Skip("Minecraft mode was never entered"); yield break; }
            if (!_haveCamera) { r.Error("camera state was not recorded before the first ENTER"); yield break; }
            IEnumerator wait = Wait(1.0);
            while (wait.MoveNext()) yield return null;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            CameraController ctl = cam == null ? null : cam.GetComponent<CameraController>();
            if (ctl == null) { r.Error("no main camera with a CameraController"); yield break; }
            double pos = (cam.transform.position - _camPos).magnitude;
            double rot = Quaternion.Angle(cam.transform.rotation, _camRot);
            double fov = Math.Abs(cam.fieldOfView - _camFov), near = Math.Abs(cam.nearClipPlane - _camNear), far = Math.Abs(cam.farClipPlane - _camFar);
            double ctlPos = Math.Max((ctl.m_targetPosition - _ctlTargetPos).magnitude, (ctl.m_currentPosition - _ctlCurrentPos).magnitude);
            double ctlAngle = Math.Max((ctl.m_targetAngle - _ctlTargetAngle).magnitude, (ctl.m_currentAngle - _ctlCurrentAngle).magnitude);
            double ctlSize = Math.Max(Math.Abs(ctl.m_targetSize - _ctlTargetSize), Math.Abs(ctl.m_currentSize - _ctlCurrentSize));
            bool blocker = _player.BlockerUp, modal = UIView.HasModalInput();
            r.Measurements.Set("position_diff_m", pos);
            r.Measurements.Set("rotation_diff_deg", rot);
            r.Measurements.Set("fov_diff_deg", fov);
            r.Measurements.Set("near_clip_diff_m", near);
            r.Measurements.Set("far_clip_diff_m", far);
            r.Measurements.Set("controller_position_diff_m", ctlPos);
            r.Measurements.Set("controller_angle_diff_deg", ctlAngle);
            r.Measurements.Set("controller_size_diff_m", ctlSize);
            r.Measurements.Set("controller_enabled", ctl.enabled);
            r.Measurements.Set("shortcut_blocker_up", blocker);
            r.Measurements.Set("modal_input", modal);
            const double tol = 1e-3;
            if (pos > tol || rot > tol || fov > tol || near > tol || far > tol || ctlPos > tol || ctlAngle > tol || ctlSize > tol)
                r.Fail("camera differs from before the first ENTER");
            else if (blocker || modal) r.Fail("a modal is still up (shortcut blocker " + blocker + ", any modal " + modal + ")");
            else r.Pass("");
        }

        private void RecordCamera()
        {
            _haveCamera = false;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            CameraController ctl = cam == null ? null : cam.GetComponent<CameraController>();
            if (ctl == null) return;
            _camPos = cam.transform.position;
            _camRot = cam.transform.rotation;
            _camFov = cam.fieldOfView;
            _camNear = cam.nearClipPlane;
            _camFar = cam.farClipPlane;
            _ctlTargetPos = ctl.m_targetPosition;
            _ctlCurrentPos = ctl.m_currentPosition;
            _ctlTargetAngle = ctl.m_targetAngle;
            _ctlCurrentAngle = ctl.m_currentAngle;
            _ctlTargetSize = ctl.m_targetSize;
            _ctlCurrentSize = ctl.m_currentSize;
            _haveCamera = true;
        }

        // ---- shared ----

        // Enters at feet (+0.1 m so the player lands rather than starting embedded), waits for the teleport ack, settles 0.5 s.
        private IEnumerator EnterAndSettle(ScenarioResult r, Vector3 feet, float yaw, Action<string> fail)
        {
            string ready = NotReady();
            if (ready != null) { fail("Minecraft not ready: " + ready); yield break; }
            _player.SetSyntheticKeys(NoKeys);
            string refusal = _player.EnterAt(_host, _cityReady, feet + Vector3.up * 0.1f, yaw);
            if (refusal != null) { fail("could not enter Minecraft mode: " + refusal); yield break; }
            _entered = true;
            double start = Now();
            while (!_player.IsActive)
            {
                if (!_player.IsOn) { fail("Minecraft mode ended before the teleport was acknowledged"); yield break; }
                yield return null;
            }
            r.Measurements.Set("teleport_ack_s", Now() - start);
            IEnumerator settle = Wait(0.5);
            while (settle.MoveNext())
            {
                if (!_player.IsOn) { fail("Minecraft mode ended"); yield break; }
                yield return null;
            }
        }

        private IEnumerator Wait(double seconds)
        {
            double end = Now() + seconds;
            while (Now() < end) yield return null;
        }

        private Vector3 Feet()
        {
            Vector3 feet;
            uint flags;
            double age;
            if (!_player.IsActive || !_player.TryLatestState(out feet, out flags, out age))
                throw new InvalidOperationException("Minecraft mode is no longer active");
            return feet;
        }

        private bool OnGround()
        {
            Vector3 feet;
            uint flags;
            double age;
            return _player.TryLatestState(out feet, out flags, out age) && (flags & PlayerStateFlags.OnGround) != 0;
        }

        private Fixture Fx()
        {
            return CurrentFixture == null ? null : CurrentFixture();
        }

        private static Vector3 CityTarget()
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            CameraController ctl = cam == null ? null : cam.GetComponent<CameraController>();
            if (ctl == null) throw new InvalidOperationException("no main camera with a CameraController");
            return ctl.m_targetPosition;
        }

        private List<ushort> Segments(Vector3 centre, float radius, NetGeometry.Kind kind, int max)
        {
            NetSegment[] segs = Singleton<NetManager>.instance.m_segments.m_buffer;
            var result = new List<ushort>();
            foreach (ushort id in _net.FindSegments(centre, radius))
            {
                if (NetGeometry.Classify(segs[id].Info) == kind) result.Add(id);
                if (result.Count >= max) break;
            }
            return result;
        }

        private static double[] Polyline(NetSegment[] segs, ushort id)
        {
            Bezier3 c = segs[id].GenerateBezier(id, segs[id].m_startNode);
            var xz = new double[2 * 33];
            for (int i = 0; i <= 32; i++)
            {
                Vector3 p = c.Position(i / 32f);
                xz[2 * i] = p.x;
                xz[2 * i + 1] = p.z;
            }
            return xz;
        }

        // The collision top nearest refY at (x, z), built through the streamer's own region builder.
        private double? SurfaceAt(float x, float z, ushort mask, double refY, double window)
        {
            int rx, rz;
            CollisionStreamer.RegionOfCs(x, z, out rx, out rz);
            long key = Skylines.Core.Streaming.RegionGrid.Key(rx, rz);
            TriangleBuffer buf;
            if (!_regions.TryGetValue(key, out buf))
            {
                float minX, minZ, maxX, maxZ;
                CollisionStreamer.RegionRectCs(rx, rz, out minX, out minZ, out maxX, out maxZ);
                buf = new TriangleBuffer();
                Exception roads = CollisionStreamer.BuildCs(_terrain, _net, minX, minZ, maxX, maxZ, buf);
                if (roads != null) throw new InvalidOperationException("roads failed to build in region (" + rx + "," + rz + "): " + roads.Message, roads);
                _regions[key] = buf;
            }
            return SelfTestMath.TopNear(buf.Positions, buf.Flags, buf.Count, x, z, mask, refY, window);
        }

        private static double[] Vec(Vector3 v)
        {
            return new double[] { v.x, v.y, v.z };
        }

        private static Dictionary<string, object> SummaryObject(Summary s)
        {
            return new Dictionary<string, object> { { "count", s.Count }, { "min", s.Min }, { "max", s.Max }, { "mean", s.Mean }, { "max_abs", s.MaxAbs } };
        }
    }
}
