using System.Collections.Generic;

namespace Skylines.Core.Motion
{
    /// <summary>
    /// Turn rates (degrees per second) of keyed objects from successive yaw samples on the caller's clock, like
    /// <see cref="VelocityTracker"/>. Sample each object once per round between <see cref="Begin"/> and <see cref="End"/>.
    /// </summary>
    public sealed class YawRateTracker
    {
        /// <summary>Faster than this (deg/s) is a jump (an id reused, a teleport) and reads as zero.</summary>
        public const double MaxRate = 720.0;

        private struct Last { public double Yaw; public double Time; public int Round; }

        private readonly Dictionary<long, Last> _last = new Dictionary<long, Last>();
        private readonly List<long> _stale = new List<long>();
        private double _now;
        private int _round;

        public int Count { get { return _last.Count; } }

        public void Begin(double nowSeconds)
        {
            _now = nowSeconds;
            _round++;
        }

        public double Sample(long key, double yawDeg)
        {
            double rate = 0;
            Last prev;
            if (_last.TryGetValue(key, out prev))
            {
                double dt = _now - prev.Time;
                if (dt > 0)
                {
                    double d = (yawDeg - prev.Yaw) % 360.0;
                    if (d > 180) d -= 360;
                    else if (d <= -180) d += 360;
                    rate = d / dt;
                    if (rate > MaxRate || rate < -MaxRate) rate = 0;
                }
            }
            _last[key] = new Last { Yaw = yawDeg, Time = _now, Round = _round };
            return rate;
        }

        public void End()
        {
            _stale.Clear();
            foreach (KeyValuePair<long, Last> e in _last)
                if (e.Value.Round != _round) _stale.Add(e.Key);
            foreach (long k in _stale) _last.Remove(k);
        }
    }
}
