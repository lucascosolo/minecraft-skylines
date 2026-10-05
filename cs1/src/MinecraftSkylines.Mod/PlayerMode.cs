using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ColossalFramework.UI;
using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Skylines.Core.Input;
using Skylines.Core.Motion;
using Skylines.Host;
using Skylines.Host.Camera;
using Skylines.Host.Input;
using UnityEngine;
using InputMsg = MinecraftSkylines.Protocol.Input;
using McVec = MinecraftSkylines.Protocol.Vec3d;
using UInput = UnityEngine.Input;

namespace MinecraftSkylines.Mod
{
    /// <summary>
    /// Minecraft mode (milestone 2): Ctrl+Shift+M in a loaded city hands the city camera to the
    /// Minecraft player; Esc, link loss, level unload, mod disable or any exception hands it back.
    /// Writes nothing to the city or its save. Main thread only; every entry point is guarded and
    /// <see cref="Exit"/> never throws.
    /// </summary>
    internal sealed class PlayerMode
    {
        private const float SpawnEyeHeight = 1.62f;
        private const float NearClip = 0.1f;
        private const double StreamBudgetMs = 1.5;
        private const double DefaultDegreesPerMouseUnit = 1.5;

        private enum State { Off, Waiting, Active }

        private readonly HostLog _log;
        private readonly Action _changed;
        private readonly MinecraftLauncher _launcher;
        private readonly CameraTakeover _camera = new CameraTakeover();
        private readonly ShortcutBlocker _blocker = new ShortcutBlocker();
        private readonly InputCapture _input = new InputCapture(CapturedKeyCodes());
        private readonly TickInterpolator _interp = new TickInterpolator();
        private readonly CollisionStreamer _streamer;
        private readonly List<CapturedEvent> _captured = new List<CapturedEvent>();
        private readonly List<InputEvent> _events = new List<InputEvent>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Stopwatch _frameWatch = new Stopwatch();
        private readonly double _sensitivity = ConfiguredSensitivity();

        private State _state;
        private uint _teleportSeq;
        private PlayerLook _look = new PlayerLook(0, 0);
        private Vector3 _spawnFeet;
        private Vector3 _feet;
        private float _eye = SpawnEyeHeight;
        private float _fov;
        private bool _haveTick;
        private uint _lastTick;
        private uint _lastAck;
        private bool _lastHeld;
        private string _note = "";
        private double _frameMs;
        private double _frameMaxMs;
        private double _frameTotalMs;
        private long _frames;

        public PlayerMode(HostLog log, Action changed, MinecraftLauncher launcher)
        {
            _log = log;
            _changed = changed;
            _launcher = launcher;
            _streamer = new CollisionStreamer(log);
        }

        public bool IsOn { get { return _state != State.Off; } }

        /// <summary>The Ctrl+Shift+G collision wireframe.</summary>
        public Diagnostics.CollisionViewer Viewer { get { return _streamer.Viewer; } }

        /// <summary>Per frame from the pump's Update, after bridge events were handled.</summary>
        public void Update(BridgeHost host, bool cityReady)
        {
            _frameWatch.Reset();
            _frameWatch.Start();
            try
            {
                _blocker.Tick();
                if (_state == State.Off)
                {
                    UpdateOff(host, cityReady);
                    return;
                }
                if (host == null || host.State != BridgeState.Connected)
                {
                    Exit("link lost", host, false);
                    return;
                }
                if (!cityReady)
                {
                    Exit("city no longer loaded", host, true);
                    return;
                }
                if (UInput.GetKeyDown(KeyCode.Escape))
                {
                    Exit("player pressed Esc", host, false);
                    return;
                }
                float dx, dy;
                _input.ReadMouse(out dx, out dy);
                _look.Apply(dx, dy, _sensitivity);
                _streamer.Tick(_state == State.Active ? _feet : _spawnFeet, host, StreamBudgetMs);
                if (_state == State.Active)
                {
                    SendInput(host);
                }
            }
            catch (Exception e)
            {
                _log.Error("player mode update", e);
                Exit("error: " + e.Message, host, true);
            }
            finally
            {
                StopFrameWatch();
            }
        }

        /// <summary>Per frame from the pump's LateUpdate: drives the camera.</summary>
        public void LateUpdate(BridgeHost host)
        {
            if (_state == State.Off)
            {
                return;
            }
            _frameWatch.Reset();
            _frameWatch.Start();
            try
            {
                if (_state == State.Active)
                {
                    Skylines.Core.Motion.Vec3d pos;
                    float eye;
                    if (_interp.Sample(NowMs(), out pos, out eye))
                    {
                        _feet = new Vector3((float)pos.X, (float)pos.Y, (float)pos.Z);
                        _eye = eye;
                    }
                }
                Vector3 feet = _state == State.Active ? _feet : _spawnFeet;
                Quaternion rot = Quaternion.Euler((float)_look.Pitch, (float)_look.Yaw, 0f);
                _camera.Drive(feet + new Vector3(0f, _eye, 0f), rot, _fov, NearClip);
            }
            catch (Exception e)
            {
                _log.Error("player mode camera", e);
                Exit("error: " + e.Message, host, true);
            }
            finally
            {
                _frameWatch.Stop();
                _frameMs += _frameWatch.Elapsed.TotalMilliseconds;
            }
        }

        /// <summary>A PLAYER_STATE arrived (called while polling bridge events).</summary>
        public void OnPlayerState(PlayerState s)
        {
            _lastAck = s.TeleportAck;
            _lastHeld = (s.Flags & PlayerStateFlags.Held) != 0;
            if (_state == State.Off || s.TeleportAck != _teleportSeq)
            {
                return;
            }
            if (s.FovDeg > 1f)
            {
                _fov = s.FovDeg;
            }
            if (!_haveTick || s.TickSeq != _lastTick)
            {
                McVec cs = PlayerPose.McToCs(new McVec(s.CurX, s.CurY, s.CurZ));
                var v = new Skylines.Core.Motion.Vec3d { X = cs.X, Y = cs.Y, Z = cs.Z };
                _interp.Push(s.TickSeq, v, s.CurEyeHeight, NowMs(), s.TickMs > 0f ? s.TickMs : 50f);
                _haveTick = true;
                _lastTick = s.TickSeq;
            }
            if (_state == State.Waiting && !_lastHeld)
            {
                _state = State.Active;
                McVec cs = PlayerPose.McToCs(new McVec(s.CurX, s.CurY, s.CurZ));
                _feet = new Vector3((float)cs.X, (float)cs.Y, (float)cs.Z);
                _eye = s.CurEyeHeight;
                _note = "";
                _log.Info("player mode: Minecraft acknowledged teleport " + _teleportSeq + " at CS (" + Fmt(_feet) + "); "
                    + _streamer.Stats);
                _changed();
            }
        }

        /// <summary>
        /// Leaves player mode: tells Minecraft (when possible), releases input and restores the camera.
        /// Never throws. <paramref name="immediate"/> removes the shortcut blocker now (level unload,
        /// mod disable) instead of after Esc is released.
        /// </summary>
        public void Exit(string reason, BridgeHost host, bool immediate)
        {
            if (_state == State.Off)
            {
                if (immediate) Guard("release blocker", () => ReportBlocker(_blocker.ReleaseNow()));
                return;
            }
            State was = _state;
            _state = State.Off;
            Guard("send EXIT_PLAYER_MODE", () =>
            {
                if (host != null && host.State == BridgeState.Connected && host.NegotiatedAppMinor >= 1)
                {
                    host.Send(AppProtocol.ExitPlayerModeType, new ExitPlayerMode { Reason = reason }.Encode());
                }
            });
            Guard("release input", () => _input.End());
            Guard("restore camera", () =>
            {
                string problems = _camera.Release();
                if (problems != null) _log.Warn("camera restore incomplete: " + problems);
            });
            Guard("release blocker", () =>
            {
                if (immediate) ReportBlocker(_blocker.ReleaseNow());
                else _blocker.End();
            });
            Guard("reset interpolator", () => _interp.Reset());
            _haveTick = false;
            _note = "left: " + reason;
            _log.Info("player mode off (" + was + "): " + reason + FrameSummary());
            Guard("status", () => _changed());
        }

        /// <summary>From the pump's OnGUI: a centred notice while waiting for Minecraft.</summary>
        public void OnGui()
        {
            bool starting = _state == State.Off && _launcher.Pending;
            if ((_state != State.Waiting && !starting) || Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            const float w = 460f, h = 40f;
            GUI.Box(new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h),
                starting ? _launcher.OverlayText() : "Waiting for Minecraft…  (Esc returns to the city)");
        }

        public string OverlayText()
        {
            var sb = new StringBuilder();
            sb.Append("Minecraft mode: ");
            switch (_state)
            {
                case State.Off: sb.Append("off  [Ctrl+Shift+M]"); break;
                case State.Waiting: sb.Append("waiting for Minecraft (teleport ").Append(_teleportSeq).Append(", ack ").Append(_lastAck).Append(_lastHeld ? ", held)" : ")"); break;
                default: sb.Append("on  [Esc returns]  feet ").Append(Fmt(_feet)).Append("  yaw ").Append(_look.Yaw.ToString("0")).Append(" pitch ").Append(_look.Pitch.ToString("0")); break;
            }
            if (_note.Length > 0) sb.Append("  (").Append(_note).Append(')');
            string launch = _launcher.OverlayText();
            if (launch.Length > 0) sb.Append('\n').Append(launch);
            if (_state != State.Off) sb.Append("\nCollision: ").Append(_streamer.Stats);
            if (_frames > 0) sb.Append("\nPlayer mode frame: avg ").Append((_frameTotalMs / _frames).ToString("0.000"))
                .Append(" ms, max ").Append(_frameMaxMs.ToString("0.000")).Append(" ms");
            return sb.ToString();
        }

        /// <summary>Off: Ctrl+Shift+M enters, or starts Minecraft and enters once it connects (see <see cref="MinecraftLauncher"/>).</summary>
        private void UpdateOff(BridgeHost host, bool cityReady)
        {
            bool connected = host != null && host.State == BridgeState.Connected;
            bool key = EnterKeyPressed();
            if (_launcher.Pending)
            {
                if (key || UInput.GetKeyDown(KeyCode.Escape) || !cityReady)
                {
                    _launcher.Cancel();
                    _note = "start cancelled (Minecraft keeps running)";
                }
                else if (connected)
                {
                    _launcher.Cancel();
                    TryEnter(host, cityReady);
                }
                else
                {
                    _launcher.Tick();
                }
                return;
            }
            if (!key)
            {
                return;
            }
            if (!connected && cityReady && _launcher.Begin())
            {
                _note = "";
                return;
            }
            TryEnter(host, cityReady);
        }

        private void TryEnter(BridgeHost host, bool cityReady)
        {
            string refusal = null;
            if (!cityReady || !TerrainManager.exists) refusal = "no city loaded";
            else if (host == null || host.State != BridgeState.Connected) refusal = "Minecraft is not connected";
            else if (host.NegotiatedAppMinor < 1) refusal = "Minecraft side is too old (app minor " + host.NegotiatedAppMinor + ")";
            else refusal = _camera.Acquire();
            if (refusal != null)
            {
                _note = "cannot enter: " + refusal;
                _log.Info("player mode refused: " + refusal);
                return;
            }
            _state = State.Waiting;
            try
            {
                string blocked = _blocker.Begin();
                if (blocked != null) throw new InvalidOperationException("shortcut blocker: " + blocked);
                _input.Begin();

                Vector3 target = _camera.CityTarget;
                target.y = TerrainManager.instance.SampleDetailHeightSmooth(target);
                _spawnFeet = target;
                try { _log.Info("player mode: " + _streamer.DescribeNearestGround(target)); }
                catch (Exception e) { _log.Error("nearest road diagnostics", e); }
                _eye = SpawnEyeHeight;
                _fov = 0f;
                _look = new PlayerLook(_camera.CityRotation.eulerAngles.y, 0);
                _interp.Reset();
                _haveTick = false;
                _frames = 0; _frameTotalMs = 0; _frameMaxMs = 0;

                if (!host.Send(AppProtocol.CollisionResetType, _streamer.Reset()))
                    throw new InvalidOperationException("could not send COLLISION_RESET");
                _teleportSeq++;
                McVec mc = PlayerPose.FeetCsToMc(new McVec(target.x, target.y, target.z));
                McLook look = _look.ToMc();
                var enter = new EnterPlayerMode
                {
                    TeleportSeq = _teleportSeq,
                    X = mc.X, Y = mc.Y, Z = mc.Z,
                    Yaw = (float)look.Yaw, Pitch = (float)look.Pitch,
                    CollisionEpoch = _streamer.Epoch,
                };
                if (!host.Send(AppProtocol.EnterPlayerModeType, enter.Encode()))
                    throw new InvalidOperationException("could not send ENTER_PLAYER_MODE");
                _note = "";
                _log.Info("player mode: enter, teleport " + _teleportSeq + " to CS (" + Fmt(target) + ") = MC ("
                    + mc.X.ToString("0.00") + ", " + mc.Y.ToString("0.00") + ", " + mc.Z.ToString("0.00") + "), yaw "
                    + look.Yaw.ToString("0.0") + ", epoch " + _streamer.Epoch);
                _changed();
            }
            catch (Exception e)
            {
                _log.Error("player mode enter", e);
                Exit("could not enter: " + e.Message, host, true);
            }
        }

        private void SendInput(BridgeHost host)
        {
            _captured.Clear();
            _events.Clear();
            _input.Poll(_captured);
            foreach (CapturedEvent c in _captured)
            {
                switch (c.Kind)
                {
                    case CapturedKind.KeyDown:
                    case CapturedKind.KeyUp:
                        byte action = (byte)(c.Kind == CapturedKind.KeyDown ? 1 : 0);
                        int button = GlfwKeyMap.MouseButtonFromUnityKeyCode(c.Code);
                        int key = button >= 0 ? -1 : GlfwKeyMap.FromUnityKeyCode(c.Code);
                        if (button >= 0) _events.Add(new InputEvent(InputKind.MouseButton, action, button));
                        else if (key >= 0) _events.Add(new InputEvent(InputKind.Key, action, key));
                        break;
                    case CapturedKind.Wheel:
                        _events.Add(new InputEvent(InputKind.Scroll, 0, (int)Math.Round(c.Amount * 120f)));
                        break;
                    case CapturedKind.Text:
                        _events.Add(new InputEvent(InputKind.Text, 0, c.Code));
                        break;
                }
            }
            McLook look = _look.ToMc();
            var msg = new InputMsg { Yaw = (float)look.Yaw, Pitch = (float)look.Pitch, Events = _events.ToArray() };
            host.Send(AppProtocol.InputType, msg.Encode());
        }

        private static bool EnterKeyPressed()
        {
            return UInput.GetKeyDown(KeyCode.M)
                && (UInput.GetKey(KeyCode.LeftControl) || UInput.GetKey(KeyCode.RightControl))
                && (UInput.GetKey(KeyCode.LeftShift) || UInput.GetKey(KeyCode.RightShift))
                && !UIView.HasModalInput() && !UIView.HasInputFocus();
        }

        /// <summary>Every Unity key code Minecraft can receive; Esc stays with CS1.</summary>
        private static int[] CapturedKeyCodes()
        {
            var codes = new List<int>();
            foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
            {
                int u = (int)k;
                if (k != KeyCode.Escape && !codes.Contains(u)
                    && (GlfwKeyMap.FromUnityKeyCode(u) >= 0 || GlfwKeyMap.MouseButtonFromUnityKeyCode(u) >= 0))
                {
                    codes.Add(u);
                }
            }
            return codes.ToArray();
        }

        private static double ConfiguredSensitivity()
        {
            double v;
            string s = Environment.GetEnvironmentVariable("MCSKYLINES_MOUSE_DEG");
            if (!string.IsNullOrEmpty(s) && double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v) && v > 0 && v < 100)
            {
                return v;
            }
            return DefaultDegreesPerMouseUnit;
        }

        private void ReportBlocker(string problem)
        {
            if (problem != null) _log.Warn("shortcut blocker still up: " + problem);
        }

        private void Guard(string what, Action step)
        {
            try { step(); }
            catch (Exception e) { _log.Error("player mode exit, " + what, e); }
        }

        private double NowMs()
        {
            return _clock.Elapsed.TotalMilliseconds;
        }

        private void StopFrameWatch()
        {
            _frameWatch.Stop();
            if (_state == State.Off && _frameMs == 0)
            {
                return;
            }
            double ms = _frameMs + _frameWatch.Elapsed.TotalMilliseconds;
            _frameMs = 0;
            _frames++;
            _frameTotalMs += ms;
            if (ms > _frameMaxMs) _frameMaxMs = ms;
        }

        private string FrameSummary()
        {
            return _frames == 0 ? "" : string.Format("; frame avg {0:0.000} ms, max {1:0.000} ms over {2} frames",
                _frameTotalMs / _frames, _frameMaxMs, _frames);
        }

        private static string Fmt(Vector3 v)
        {
            return v.x.ToString("0.0") + ", " + v.y.ToString("0.0") + ", " + v.z.ToString("0.0");
        }
    }
}
