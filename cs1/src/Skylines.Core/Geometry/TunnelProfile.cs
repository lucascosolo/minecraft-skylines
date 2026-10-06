using System;
using System.Collections.Generic;

namespace Skylines.Core.Geometry
{
    /// <summary>
    /// One cross-section of a tunnel: the wall foot points <c>L</c> and <c>R</c> (inset from the drawn edges, at the edges'
    /// height) and <see cref="Top"/>, the y of the ceiling slab's top face, the same across the width.
    /// <see cref="Covered"/> means the interval from this section to the next is walled and roofed.
    /// </summary>
    public struct TunnelSection
    {
        /// <summary>Left wall foot point.</summary>
        public float Lx, Ly, Lz;
        /// <summary>Right wall foot point.</summary>
        public float Rx, Ry, Rz;
        /// <summary>y of the ceiling slab's top face.</summary>
        public float Top;
        /// <summary>The interval to the next section is walled and roofed.</summary>
        public bool Covered;
    }

    /// <summary>
    /// The cross-sections of a road tunnel shared by its drawn shell (<see cref="TunnelShell"/>) and its collision
    /// (Skylines.Host NetGeometry), so both have the same walls and the same level ceiling. Unity-free.
    /// </summary>
    public static class TunnelProfile
    {
        /// <summary>Wanted height of the ceiling's top above the higher road edge, in metres.</summary>
        public const float Headroom = 8f;
        /// <summary>Earth kept above the ceiling's top inside a tunnel segment, in metres.</summary>
        public const float Cover = 1f;

        /// <summary>
        /// Ceiling top over a section whose higher edge is at <paramref name="floorY"/>: <see cref="Headroom"/> (or the
        /// larger <paramref name="clearance"/>) above it, lowered to <paramref name="cap"/> but never below clearance.
        /// </summary>
        public static float Top(float floorY, float clearance, float cap)
        {
            return floorY + Math.Max(clearance, Math.Min(Math.Max(clearance, Headroom), cap - floorY));
        }

        /// <summary>
        /// Replaces <paramref name="into"/> with the n + 1 sections at t = k / n of the drawn edges
        /// (n = max(1, ceil(|left.D - left.A| / step))). L and R lie exactly <paramref name="inset"/> metres from the
        /// edge points towards each other. A slope (<paramref name="tunnel"/> false) is capped at its portal level (the
        /// higher end's mean edge y) and covered where max mean edge y of the interval + clearance &lt;= portal level; a
        /// tunnel is covered throughout and its interior sections are capped at <paramref name="ground"/> - <see cref="Cover"/>
        /// (end sections never, so pieces meeting at a corner agree). Returns the section count; 0 when the edges are not
        /// more than 2 * inset apart.
        /// </summary>
        public static int Build(Bezier3D left, Bezier3D right, float clearance, bool tunnel, float step, float inset,
            Func<float, float, float> ground, List<TunnelSection> into)
        {
            into.Clear();
            float wx = (right.Ax + right.Dx - left.Ax - left.Dx) * 0.5f, wy = (right.Ay + right.Dy - left.Ay - left.Dy) * 0.5f;
            float wz = (right.Az + right.Dz - left.Az - left.Dz) * 0.5f;
            if (!(Math.Sqrt(wx * wx + wy * wy + wz * wz) > 2 * inset)) return 0;
            float dx = left.Dx - left.Ax, dy = left.Dy - left.Ay, dz = left.Dz - left.Az;
            int n = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy + dz * dz) / step));
            float portal = Math.Max((left.Ay + right.Ay) * 0.5f, (left.Dy + right.Dy) * 0.5f);
            float prevMean = 0f;
            for (int k = 0; k <= n; k++)
            {
                float t = k / (float)n;
                float[] p = left.At(t), q = right.At(t);
                float ex = q[0] - p[0], ey = q[1] - p[1], ez = q[2] - p[2];
                float d = (float)Math.Sqrt(ex * ex + ey * ey + ez * ez);
                float f = d > 0f ? inset / d : 0f;
                float cap = float.PositiveInfinity;
                if (!tunnel) cap = portal;
                else if (ground != null && k > 0 && k < n) cap = ground((p[0] + q[0]) * 0.5f, (p[2] + q[2]) * 0.5f) - Cover;
                float mean = (p[1] + q[1]) * 0.5f;
                into.Add(new TunnelSection
                {
                    Lx = p[0] + ex * f, Ly = p[1] + ey * f, Lz = p[2] + ez * f,
                    Rx = q[0] - ex * f, Ry = q[1] - ey * f, Rz = q[2] - ez * f,
                    Top = Top(Math.Max(p[1], q[1]), clearance, cap),
                });
                if (k > 0)
                {
                    TunnelSection prev = into[k - 1];
                    prev.Covered = tunnel || Math.Max(prevMean, mean) + clearance <= portal;
                    into[k - 1] = prev;
                }
                prevMean = mean;
            }
            return n + 1;
        }
    }
}
