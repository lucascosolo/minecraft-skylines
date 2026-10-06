using System;
using System.Collections.Generic;

namespace Skylines.Core.Geometry
{
    /// <summary>
    /// One cross-section of a tunnel: the inner wall foot points <c>L</c> and <c>R</c> (on the line between the drawn
    /// edges, at the edges' height there) and <see cref="Ceiling"/>, the y of the ceiling's underside, the same across the
    /// width. <see cref="Covered"/> means the interval from this section to the next is walled and roofed.
    /// </summary>
    public struct TunnelSection
    {
        /// <summary>Left wall foot point.</summary>
        public float Lx, Ly, Lz;
        /// <summary>Right wall foot point.</summary>
        public float Rx, Ry, Rz;
        /// <summary>y of the ceiling's underside.</summary>
        public float Ceiling;
        /// <summary>Curve parameter of the section along the drawn edges.</summary>
        public float T;
        /// <summary>The interval to the next section is walled and roofed.</summary>
        public bool Covered;
    }

    /// <summary>The cross-section shape of one tunnel type, taken from its slope's portal mesh where possible.</summary>
    public struct TunnelDims
    {
        /// <summary>Inner wall half-width divided by the road's half-width.</summary>
        public float InnerRatio;
        /// <summary>Height of the ceiling's underside above the road centre, in metres.</summary>
        public float Lintel;
        /// <summary>Fraction of a slope's length, from its lower end, that is roofed; NaN when unknown.</summary>
        public float CoverFraction;
    }

    /// <summary>
    /// The cross-sections of a road tunnel shared by its drawn shell (<see cref="TunnelShell"/>) and its collision
    /// (Skylines.Host NetGeometry), so both have the same walls and the same level ceiling. Every section is a function of
    /// its own two edge points and the <see cref="TunnelDims"/> only, so pieces whose edges share end points share their
    /// end sections exactly. Unity-free.
    /// </summary>
    public static class TunnelProfile
    {
        /// <summary>Ceiling underside above the road without a portal mesh, in metres.</summary>
        public const float DefaultLintel = 6f;
        /// <summary>Distance from each drawn edge to its inner wall without a portal mesh, in metres.</summary>
        public const float DefaultWallInset = 2f;
        /// <summary>Portal geometry entirely above this height is the structure (walls and roof); below it, road and kerbs.</summary>
        public const float PortalSolidAbove = 2f;

        /// <summary>Dims of a tunnel whose half-width is <paramref name="halfWidth"/> and whose portal is unknown.</summary>
        public static TunnelDims Fallback(float halfWidth)
        {
            return new TunnelDims
            {
                InnerRatio = halfWidth > DefaultWallInset ? (halfWidth - DefaultWallInset) / halfWidth : 1f,
                Lintel = DefaultLintel,
                CoverFraction = float.NaN,
            };
        }

        /// <summary>
        /// Dims from a slope's portal mesh (flat xyz, mesh-local: x across, y up from the road, z along; x spans
        /// ±<paramref name="halfWidth"/>). Among triangles entirely above <see cref="PortalSolidAbove"/>: the inner wall is
        /// their smallest |x|; the lintel is the lowest point of those spanning x = 0; the roofed part is the z range of the
        /// spanning ones reaching within 0.5 m of the lintel, measured from the mesh end it touches. False without them.
        /// </summary>
        public static bool FromPortal(float[] positions, int[] indices, float halfWidth, out TunnelDims dims)
        {
            dims = default(TunnelDims);
            float inner = float.PositiveInfinity, lintel = float.PositiveInfinity;
            float zmin = float.PositiveInfinity, zmax = float.NegativeInfinity;
            for (int i = 2; i < positions.Length; i += 3)
            {
                zmin = Math.Min(zmin, positions[i]);
                zmax = Math.Max(zmax, positions[i]);
            }
            for (int pass = 0; pass < 2; pass++)
            {
                float z0 = float.PositiveInfinity, z1 = float.NegativeInfinity;
                for (int t = 0; t + 2 < indices.Length; t += 3)
                {
                    float minX = float.PositiveInfinity, maxX = float.NegativeInfinity, minY = float.PositiveInfinity, minAbs = float.PositiveInfinity;
                    float tz0 = float.PositiveInfinity, tz1 = float.NegativeInfinity;
                    for (int c = 0; c < 3; c++)
                    {
                        int v = 3 * indices[t + c];
                        float x = positions[v];
                        minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, positions[v + 1]);
                        minAbs = Math.Min(minAbs, Math.Abs(x));
                        tz0 = Math.Min(tz0, positions[v + 2]); tz1 = Math.Max(tz1, positions[v + 2]);
                    }
                    if (!(minY > PortalSolidAbove)) continue;
                    bool spanning = minX <= 0f && maxX >= 0f;
                    if (pass == 0)
                    {
                        inner = Math.Min(inner, minAbs);
                        if (spanning) lintel = Math.Min(lintel, minY);
                    }
                    else if (spanning && minY <= lintel + 0.5f)
                    {
                        z0 = Math.Min(z0, tz0);
                        z1 = Math.Max(z1, tz1);
                    }
                }
                if (pass == 0 && (!(inner > 0f) || float.IsInfinity(lintel) || !(halfWidth > 0f))) return false;
                if (pass == 1)
                {
                    float length = zmax - zmin;
                    dims.InnerRatio = inner / halfWidth;
                    dims.Lintel = lintel;
                    dims.CoverFraction = !(length > 0f) ? float.NaN
                        : z0 - zmin <= zmax - z1 ? (z1 - zmin) / length : (zmax - z0) / length;
                }
            }
            return true;
        }

        /// <summary><paramref name="edge"/> with every control point moved (1 - innerRatio) / 2 of the way to <paramref name="other"/>'s.</summary>
        public static Bezier3D Inset(Bezier3D edge, Bezier3D other, float innerRatio)
        {
            float f = (1f - innerRatio) * 0.5f;
            return new Bezier3D
            {
                Ax = edge.Ax + (other.Ax - edge.Ax) * f, Ay = edge.Ay + (other.Ay - edge.Ay) * f, Az = edge.Az + (other.Az - edge.Az) * f,
                Bx = edge.Bx + (other.Bx - edge.Bx) * f, By = edge.By + (other.By - edge.By) * f, Bz = edge.Bz + (other.Bz - edge.Bz) * f,
                Cx = edge.Cx + (other.Cx - edge.Cx) * f, Cy = edge.Cy + (other.Cy - edge.Cy) * f, Cz = edge.Cz + (other.Cz - edge.Cz) * f,
                Dx = edge.Dx + (other.Dx - edge.Dx) * f, Dy = edge.Dy + (other.Dy - edge.Dy) * f, Dz = edge.Dz + (other.Dz - edge.Dz) * f,
            };
        }

        /// <summary>
        /// Replaces <paramref name="into"/> with the sections at t = k / n of the drawn edges
        /// (n = max(1, ceil(|left.D - left.A| / step))), plus, on a slope with a known cover fraction, one at the portal
        /// (that fraction of the length from the lower end). L and R are the edge points moved (1 - InnerRatio) / 2 of the
        /// way towards each other; Ceiling is the mean edge y plus the lintel. A tunnel is covered throughout; a slope where
        /// an interval's middle is on the lower side of the portal, or without a cover fraction, where max mean edge y of
        /// the interval + lintel &lt;= the higher end's mean edge y. Returns the section count; 0 when InnerRatio &lt;= 0.
        /// </summary>
        public static int Build(Bezier3D left, Bezier3D right, TunnelDims dims, bool tunnel, float step, List<TunnelSection> into)
        {
            into.Clear();
            if (!(dims.InnerRatio > 0f)) return 0;
            float dx = left.Dx - left.Ax, dy = left.Dy - left.Ay, dz = left.Dz - left.Az;
            int n = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy + dz * dz) / step));
            float meanA = (left.Ay + right.Ay) * 0.5f, meanD = (left.Dy + right.Dy) * 0.5f;
            float portal = Math.Max(meanA, meanD);
            bool lowerAtA = meanA <= meanD;
            bool byFraction = !tunnel && !float.IsNaN(dims.CoverFraction);
            float tc = lowerAtA ? dims.CoverFraction : 1f - dims.CoverFraction;
            bool split = byFraction && tc > 0f && tc < 1f;
            float f = (1f - dims.InnerRatio) * 0.5f;
            float prevT = 0f, prevMean = 0f;
            for (int k = 0; k <= n; k++)
            {
                float t = k / (float)n;
                if (split && tc > prevT + 1e-4f && tc < t - 1e-4f)
                {
                    Add(left, right, tc, f, dims, tunnel, byFraction, lowerAtA, tc, portal, ref prevT, ref prevMean, into);
                }
                Add(left, right, t, f, dims, tunnel, byFraction, lowerAtA, tc, portal, ref prevT, ref prevMean, into);
            }
            return into.Count;
        }

        private static void Add(Bezier3D left, Bezier3D right, float t, float f, TunnelDims dims, bool tunnel, bool byFraction,
            bool lowerAtA, float tc, float portal, ref float prevT, ref float prevMean, List<TunnelSection> into)
        {
            float[] p = left.At(t), q = right.At(t);
            float ex = q[0] - p[0], ey = q[1] - p[1], ez = q[2] - p[2];
            float mean = (p[1] + q[1]) * 0.5f;
            into.Add(new TunnelSection
            {
                Lx = p[0] + ex * f, Ly = p[1] + ey * f, Lz = p[2] + ez * f,
                Rx = q[0] - ex * f, Ry = q[1] - ey * f, Rz = q[2] - ez * f,
                Ceiling = mean + dims.Lintel,
                T = t,
            });
            int k = into.Count - 1;
            if (k > 0)
            {
                float mid = (prevT + t) * 0.5f;
                TunnelSection prev = into[k - 1];
                prev.Covered = tunnel || (byFraction ? (lowerAtA ? mid < tc : mid > tc)
                    : Math.Max(prevMean, mean) + dims.Lintel <= portal);
                into[k - 1] = prev;
            }
            prevT = t;
            prevMean = mean;
        }
    }
}
