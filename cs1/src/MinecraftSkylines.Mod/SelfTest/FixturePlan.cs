// Unity-free planning for the self-test fixture (roads built into a fresh new game), compiled into
// MinecraftSkylines.Mod.Tests as well. Coordinates are CS1 world metres (x, z horizontal).
using System;

namespace MinecraftSkylines.Mod.SelfTest
{
    /// <summary>A planned road node; <see cref="Elevation"/> is metres above the terrain.</summary>
    internal struct FixtureNode
    {
        public double X, Z, Elevation;
    }

    /// <summary>A hillside spot for S5; (UpX, UpZ) is the unit horizontal uphill direction.</summary>
    internal struct SlopeSpot
    {
        public double X, Z, Degrees, UpX, UpZ;
    }

    /// <summary>
    /// Where the fixture goes: the flattest 300 m square, a straight 200 m ground road 60 m south of its
    /// centre, a 200 m road 60 m north that ramps to +8 m over 80 m and stays there for 120 m, and the
    /// steepest clear 10-30 degree slope within 300 m.
    /// </summary>
    internal static class FixturePlan
    {
        public const double AreaSize = 300, GridStep = 20, SlopeStep = 16, SlopeRadius = 300, RoadOffset = 60;
        private const double TieMetres = 0.01, Probe = 2;

        /// <summary>
        /// Centre of the AreaSize square inside the rectangle whose heights, sampled every GridStep, span the
        /// least; squares with a blocked sample are skipped; near-ties go to the centre nearest (prefX, prefZ).
        /// </summary>
        public static bool FindFlattest(Func<double, double, double> height, Func<double, double, bool> blocked,
            double minX, double minZ, double maxX, double maxZ, double prefX, double prefZ,
            out double cx, out double cz, out double spread)
        {
            cx = cz = 0;
            spread = double.PositiveInfinity;
            int win = (int)(AreaSize / GridStep) + 1;
            int nx = (int)Math.Floor((maxX - minX) / GridStep + 1e-9) + 1;
            int nz = (int)Math.Floor((maxZ - minZ) / GridStep + 1e-9) + 1;
            if (nx < win || nz < win) return false;
            var h = new double[nx, nz];
            var bad = new bool[nx, nz];
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    double x = minX + i * GridStep, z = minZ + k * GridStep;
                    bad[i, k] = blocked(x, z);
                    h[i, k] = bad[i, k] ? 0 : height(x, z);
                }
            double bestDist = double.PositiveInfinity;
            bool found = false;
            for (int i = 0; i + win <= nx; i++)
                for (int k = 0; k + win <= nz; k++)
                {
                    double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
                    bool skip = false;
                    for (int a = i; a < i + win && !skip; a++)
                        for (int b = k; b < k + win; b++)
                        {
                            if (bad[a, b]) { skip = true; break; }
                            lo = Math.Min(lo, h[a, b]);
                            hi = Math.Max(hi, h[a, b]);
                        }
                    if (skip) continue;
                    double x = minX + i * GridStep + AreaSize / 2, z = minZ + k * GridStep + AreaSize / 2;
                    double s = hi - lo, d = Dist(x, z, prefX, prefZ);
                    if (found && !(s < spread - TieMetres) && !(Math.Abs(s - spread) <= TieMetres && d < bestDist)) continue;
                    found = true; spread = s; bestDist = d; cx = x; cz = z;
                }
            return found;
        }

        /// <summary>Five nodes 50 m apart along +x, RoadOffset south (-z) of the centre, on the ground.</summary>
        public static FixtureNode[] GroundRoad(double cx, double cz)
        {
            return Road(cx, cz - RoadOffset, new[] { -100.0, -50, 0, 50, 100 }, new[] { 0.0, 0, 0, 0, 0 });
        }

        /// <summary>Six nodes along +x, RoadOffset north (+z): ground, +4, +8 (80 m ramp), then +8 m for 120 m.</summary>
        public static FixtureNode[] ElevatedRoad(double cx, double cz)
        {
            return Road(cx, cz + RoadOffset, new[] { -100.0, -60, -20, 20, 60, 100 }, new[] { 0.0, 4, 8, 8, 8, 8 });
        }

        /// <summary>
        /// The steepest grid point (SlopeStep apart, within SlopeRadius) with a 10-30 degree slope whose
        /// path 9 m down- and uphill stays within 7-33 degrees and unblocked; exact ties go to the nearer.
        /// </summary>
        public static bool SteepestSlope(Func<double, double, double> height, Func<double, double, bool> blocked,
            double cx, double cz, out SlopeSpot spot)
        {
            spot = default(SlopeSpot);
            bool found = false;
            int n = (int)(SlopeRadius / SlopeStep);
            for (int a = -n; a <= n; a++)
                for (int b = -n; b <= n; b++)
                {
                    double x = cx + a * SlopeStep, z = cz + b * SlopeStep;
                    if (Dist(x, z, cx, cz) > SlopeRadius || blocked(x, z)) continue;
                    double ux, uz;
                    double deg = Slope(height, x, z, out ux, out uz);
                    if (deg < 10 || deg > 30) continue;
                    if (found && (deg < spot.Degrees || (deg == spot.Degrees && Dist(x, z, cx, cz) >= Dist(spot.X, spot.Z, cx, cz)))) continue;
                    if (!ClearPath(height, blocked, x, z, ux, uz)) continue;
                    found = true;
                    spot = new SlopeSpot { X = x, Z = z, Degrees = deg, UpX = ux, UpZ = uz };
                }
            return found;
        }

        private static bool ClearPath(Func<double, double, double> height, Func<double, double, bool> blocked, double x, double z, double ux, double uz)
        {
            for (double d = -9; d <= 9 + 1e-9; d += 1.5)
            {
                double px = x + ux * d, pz = z + uz * d, gx, gz;
                if (blocked(px, pz)) return false;
                double s = Slope(height, px, pz, out gx, out gz);
                if (s < 7 || s > 33) return false;
            }
            return true;
        }

        private static double Slope(Func<double, double, double> height, double x, double z, out double ux, out double uz)
        {
            double hx0 = height(x - Probe, z), hx1 = height(x + Probe, z), hz0 = height(x, z - Probe), hz1 = height(x, z + Probe);
            double gx = hx1 - hx0, gz = hz1 - hz0, len = Math.Sqrt(gx * gx + gz * gz);
            ux = len > 0 ? gx / len : 0;
            uz = len > 0 ? gz / len : 0;
            return SelfTestMath.SlopeDegrees(hx0, hx1, hz0, hz1, Probe);
        }

        private static FixtureNode[] Road(double cx, double z, double[] dx, double[] elevation)
        {
            var nodes = new FixtureNode[dx.Length];
            for (int i = 0; i < dx.Length; i++) nodes[i] = new FixtureNode { X = cx + dx[i], Z = z, Elevation = elevation[i] };
            return nodes;
        }

        private static double Dist(double x0, double z0, double x1, double z1)
        {
            double dx = x0 - x1, dz = z0 - z1;
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
