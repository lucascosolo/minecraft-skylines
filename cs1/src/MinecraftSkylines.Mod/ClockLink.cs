using System;
using ColossalFramework;
using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Skylines.Host;

namespace MinecraftSkylines.Mod
{
    /// <summary>
    /// Sends the city's time of day to Minecraft (WORLD_TIME, minor 6) so its sky and light levels, the hand included,
    /// follow the city (owner, 2026-10-06: "sync the minecraft clock to the cities skylines days"). Sends right after
    /// a connect or level load, then at most once a second while the value changes. Applies Minecraft's time commands
    /// (TIME_SET, minor 16; owner, same day: "minecraft /time command should control the CS1 time") by moving the sun
    /// forward, then re-sends on the next frame. Main thread only.
    /// </summary>
    internal sealed class ClockLink
    {
        private const double IntervalMs = 1000;
        private const float MinHourChange = 1f / 600f; // 6 s of city time

        private readonly HostLog _log;
        private bool _sent;
        private bool _wasLive;
        private bool _resend;
        private double _lastMs;
        private WorldTime _last;
        private TimeSet _pending;

        public ClockLink(HostLog log)
        {
            _log = log;
        }

        /// <summary>TIME_SET from the guest; applied on the next <see cref="Update"/> while a city is loaded.</summary>
        public void Set(TimeSet t)
        {
            _pending = t;
        }

        public void Update(BridgeHost host, bool cityReady, double nowMs)
        {
            bool live = host != null && host.State == BridgeState.Connected && host.NegotiatedAppMinor >= 6
                && cityReady && SimulationManager.exists;
            if (!live)
            {
                _wasLive = false;
                _pending = null;
                return;
            }
            if (!_wasLive)
            {
                _wasLive = true;
                _sent = false;
            }
            SimulationManager sim = Singleton<SimulationManager>.instance;
            if (_pending != null)
            {
                Apply(sim, _pending);
                _pending = null;
                return; // SimulationManager.Update derives m_currentDayTimeHour from the new offset before the next frame
            }
            if (_sent && !_resend && nowMs - _lastMs < IntervalMs) return;
            var now = new WorldTime
            {
                Hour = Math.Max(0f, Math.Min(23.9999f, sim.m_currentDayTimeHour)),
                // Days of the sun's cycle (65536 frames), not the calendar, which CS1 runs about 112x faster than the sun.
                Day = (sim.m_referenceFrameIndex + sim.m_dayTimeOffsetFrames) / SimulationManager.DAYTIME_FRAMES,
                Flags = sim.m_enableDayNight ? WorldTime.DayNight : (byte)0,
            };
            if (_sent && !_resend && now.Flags == _last.Flags && now.Day == _last.Day && Math.Abs(now.Hour - _last.Hour) < MinHourChange) return;
            if (!host.Send(AppProtocol.WorldTimeType, now.Encode())) return;
            if (!_sent) _log.Info("clock: city time " + now.Hour.ToString("0.00") + " h, day/night " + (now.Flags != 0 ? "on" : "off") + " sent to Minecraft");
            _sent = true;
            _resend = false;
            _lastMs = nowMs;
            _last = now;
        }

        private void Apply(SimulationManager sim, TimeSet t)
        {
            _resend = true;
            if (!sim.m_enableDayNight)
            {
                _log.Info("clock: Minecraft time command ignored, the city has no day/night cycle");
                return;
            }
            // Unmasked, unlike the game's own setters (InfoPanel, ThreadingWrapper), so the day count only moves forward.
            sim.m_dayTimeOffsetFrames += TimeSet.OffsetFrames(sim.m_referenceFrameIndex + sim.m_dayTimeOffsetFrames, t.Hour, t.Days);
            _log.Info("clock: Minecraft time command moved the city to " + t.Hour.ToString("0.00") + " h" + (t.Days > 0 ? " plus " + t.Days + " days" : ""));
        }
    }
}
