using System;
using System.Collections.Generic;

namespace Skylines.Core.Motion
{
    /// <summary>
    /// Renders a position history one tick (plus a small extra delay) behind the clock. Samples are stamped on an ideal
    /// timeline of one tick per sequence step; an arrival more than two ticks off that timeline re-anchors it.
    /// </summary>
    public sealed class TickInterpolator
    {
        private const int MaxSamples = 16;

        private struct Snap
        {
            public uint Seq;
            public double Stamp;
            public Vec3d Pos;
            public float Eye;
            public float TickMs;
        }

        private readonly double _extraDelayMs;
        private readonly List<Snap> _samples = new List<Snap>();

        /// <summary>Creates an interpolator that renders <c>tickMs + extraDelayMs</c> behind the clock.</summary>
        public TickInterpolator(double extraDelayMs = 10)
        {
            _extraDelayMs = extraDelayMs;
        }

        /// <summary>Adds a tick sample; samples not newer than the latest (serial arithmetic) are ignored.</summary>
        public void Push(uint tickSeq, Vec3d pos, float eyeHeight, double arrivalMs, float tickMs)
        {
            var s = new Snap { Seq = tickSeq, Stamp = arrivalMs, Pos = pos, Eye = eyeHeight, TickMs = tickMs };
            if (_samples.Count > 0)
            {
                Snap last = _samples[_samples.Count - 1];
                int diff = unchecked((int)(tickSeq - last.Seq));
                if (diff <= 0) return;
                double ideal = last.Stamp + diff * (double)tickMs;
                if (Math.Abs(arrivalMs - ideal) <= 2.0 * tickMs) s.Stamp = ideal;
                if (s.Stamp <= last.Stamp) s.Stamp = last.Stamp + 1e-3;
            }
            _samples.Add(s);
            if (_samples.Count > MaxSamples) _samples.RemoveAt(0);
        }

        /// <summary>Position and eye height to draw at <paramref name="nowMs"/>; false when empty. Clamps at both ends.</summary>
        public bool Sample(double nowMs, out Vec3d pos, out float eyeHeight)
        {
            pos = new Vec3d();
            eyeHeight = 0;
            if (_samples.Count == 0) return false;
            Snap newest = _samples[_samples.Count - 1];
            double t = nowMs - (newest.TickMs + _extraDelayMs);
            Snap first = _samples[0];
            if (t <= first.Stamp || _samples.Count == 1) { pos = first.Pos; eyeHeight = first.Eye; return true; }
            if (t >= newest.Stamp) { pos = newest.Pos; eyeHeight = newest.Eye; return true; }
            for (int i = 1; i < _samples.Count; i++)
            {
                Snap b = _samples[i];
                if (t > b.Stamp) continue;
                Snap a = _samples[i - 1];
                double f = (t - a.Stamp) / (b.Stamp - a.Stamp);
                pos.X = a.Pos.X + (b.Pos.X - a.Pos.X) * f;
                pos.Y = a.Pos.Y + (b.Pos.Y - a.Pos.Y) * f;
                pos.Z = a.Pos.Z + (b.Pos.Z - a.Pos.Z) * f;
                eyeHeight = (float)(a.Eye + (b.Eye - a.Eye) * f);
                return true;
            }
            pos = newest.Pos; eyeHeight = newest.Eye;
            return true;
        }

        /// <summary>Drops all samples.</summary>
        public void Reset()
        {
            _samples.Clear();
        }
    }
}
