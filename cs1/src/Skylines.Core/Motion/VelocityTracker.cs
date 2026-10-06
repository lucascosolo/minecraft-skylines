using System.Collections.Generic;

namespace Skylines.Core.Motion
{
    /// <summary>
    /// Velocities of keyed objects from successive position samples on the caller's clock, so they match what was
    /// observed (zero while nothing moves). Sample each object once per round between <see cref="Begin"/> and
    /// <see cref="End"/>.
    /// </summary>
    public sealed class VelocityTracker
    {
        /// <summary>Faster than this (m/s) is a jump (an id reused, a teleport) and reads as zero.</summary>
        public const double MaxSpeed = 70.0;

        private struct Last { public Vec3d Pos; public double Time; public int Round; }

        private readonly Dictionary<long, Last> _last = new Dictionary<long, Last>();
        private readonly List<long> _stale = new List<long>();
        private double _now;
        private int _round;

        /// <summary>Keys remembered.</summary>
        public int Count { get { return _last.Count; } }

        /// <summary>Starts a sampling round at <paramref name="nowSeconds"/>.</summary>
        public void Begin(double nowSeconds)
        {
            _now = nowSeconds;
            _round++;
        }

        /// <summary>Records <paramref name="position"/> for <paramref name="key"/> and returns its velocity since the key's previous sample.</summary>
        public Vec3d Sample(long key, Vec3d position)
        {
            var v = new Vec3d();
            Last prev;
            if (_last.TryGetValue(key, out prev))
            {
                double dt = _now - prev.Time;
                if (dt > 0)
                {
                    double vx = (position.X - prev.Pos.X) / dt, vy = (position.Y - prev.Pos.Y) / dt, vz = (position.Z - prev.Pos.Z) / dt;
                    if (vx * vx + vy * vy + vz * vz <= MaxSpeed * MaxSpeed) v = new Vec3d { X = vx, Y = vy, Z = vz };
                }
            }
            _last[key] = new Last { Pos = position, Time = _now, Round = _round };
            return v;
        }

        /// <summary>Forgets every key not sampled since the last <see cref="Begin"/>.</summary>
        public void End()
        {
            _stale.Clear();
            foreach (KeyValuePair<long, Last> e in _last)
                if (e.Value.Round != _round) _stale.Add(e.Key);
            foreach (long k in _stale) _last.Remove(k);
        }
    }
}
