using System;
using System.Collections.Generic;

namespace Skylines.Core.Geometry
{
    /// <summary>The xz area covered by strips between pairs of edge curves, grown by a margin.</summary>
    public sealed class StripFootprint
    {
        private readonly float _margin;
        private readonly List<float> _quads = new List<float>(); // 8 floats per quad: x, z of l_k, l_k+1, r_k+1, r_k
        private float _minX = float.MaxValue, _minZ = float.MaxValue, _maxX = float.MinValue, _maxZ = float.MinValue;

        /// <summary>A footprint that also covers everything within <paramref name="margin"/> metres (xz) of its strips.</summary>
        public StripFootprint(float margin)
        {
            _margin = margin;
        }

        /// <summary>Number of quads added since the last <see cref="Clear"/>.</summary>
        public int Count { get { return _quads.Count / 8; } }

        /// <summary>Forgets every strip.</summary>
        public void Clear()
        {
            _quads.Clear();
            _minX = _minZ = float.MaxValue;
            _maxX = _maxZ = float.MinValue;
        }

        /// <summary>
        /// Adds the strip between <paramref name="left"/> and <paramref name="right"/> (both running start to end), sampled at
        /// <paramref name="samples"/> equal parameter steps (at least 1) into quads (l_k, l_k+1, r_k+1, r_k) projected on xz.
        /// </summary>
        public void Add(Bezier3D left, Bezier3D right, int samples)
        {
            if (samples < 1) throw new ArgumentOutOfRangeException("samples");
            float[] l0 = left.At(0f), r0 = right.At(0f);
            for (int k = 1; k <= samples; k++)
            {
                float t = k / (float)samples;
                float[] l1 = left.At(t), r1 = right.At(t);
                foreach (float[] q in new[] { l0, l1, r1, r0 })
                {
                    _quads.Add(q[0]);
                    _quads.Add(q[2]);
                    _minX = Math.Min(_minX, q[0]); _maxX = Math.Max(_maxX, q[0]);
                    _minZ = Math.Min(_minZ, q[2]); _maxZ = Math.Max(_maxZ, q[2]);
                }
                l0 = l1;
                r0 = r1;
            }
        }

        /// <summary>True when (x, z) lies inside any quad or within the margin of one (xz distance to its boundary).</summary>
        public bool Contains(float x, float z)
        {
            float m = _margin;
            if (x < _minX - m || x > _maxX + m || z < _minZ - m || z > _maxZ + m) return false;
            for (int o = 0; o < _quads.Count; o += 8)
            {
                bool inside = false;
                float best = float.MaxValue;
                for (int i = 0, j = 3; i < 4; j = i++)
                {
                    float xi = _quads[o + 2 * i], zi = _quads[o + 2 * i + 1], xj = _quads[o + 2 * j], zj = _quads[o + 2 * j + 1];
                    if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) inside = !inside;
                    best = Math.Min(best, SegmentDistanceSq(x, z, xi, zi, xj, zj));
                }
                if (inside || best <= m * m) return true;
            }
            return false;
        }

        private static float SegmentDistanceSq(float px, float pz, float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az, len = dx * dx + dz * dz;
            float t = len > 0f ? Math.Max(0f, Math.Min(1f, ((px - ax) * dx + (pz - az) * dz) / len)) : 0f;
            float ex = ax + t * dx - px, ez = az + t * dz - pz;
            return ex * ex + ez * ez;
        }
    }
}
