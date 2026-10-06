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
    /// a connect or level load, then at most once a second while the value changes. Main thread only.
    /// </summary>
    internal sealed class ClockLink
    {
        private const double IntervalMs = 1000;
        private const float MinHourChange = 1f / 600f; // 6 s of city time

        private readonly HostLog _log;
        private bool _sent;
        private bool _wasLive;
        private double _lastMs;
        private WorldTime _last;

        public ClockLink(HostLog log)
        {
            _log = log;
        }

        public void Update(BridgeHost host, bool cityReady, double nowMs)
        {
            bool live = host != null && host.State == BridgeState.Connected && host.NegotiatedAppMinor >= 6
                && cityReady && SimulationManager.exists;
            if (!live)
            {
                _wasLive = false;
                return;
            }
            if (!_wasLive)
            {
                _wasLive = true;
                _sent = false;
            }
            if (_sent && nowMs - _lastMs < IntervalMs) return;
            SimulationManager sim = Singleton<SimulationManager>.instance;
            var now = new WorldTime
            {
                Hour = Math.Max(0f, Math.Min(23.9999f, sim.m_currentDayTimeHour)),
                // Days of the sun's cycle (65536 frames), not the calendar, which CS1 runs about 112x faster than the sun.
                Day = (sim.m_referenceFrameIndex + sim.m_dayTimeOffsetFrames) / SimulationManager.DAYTIME_FRAMES,
                Flags = sim.m_enableDayNight ? WorldTime.DayNight : (byte)0,
            };
            if (_sent && now.Flags == _last.Flags && now.Day == _last.Day && Math.Abs(now.Hour - _last.Hour) < MinHourChange) return;
            if (!host.Send(AppProtocol.WorldTimeType, now.Encode())) return;
            if (!_sent) _log.Info("clock: city time " + now.Hour.ToString("0.00") + " h, day/night " + (now.Flags != 0 ? "on" : "off") + " sent to Minecraft");
            _sent = true;
            _lastMs = nowMs;
            _last = now;
        }
    }
}
