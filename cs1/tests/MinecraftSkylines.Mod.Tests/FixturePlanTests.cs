using System;
using MinecraftSkylines.Mod.SelfTest;
using Xunit;

namespace MinecraftSkylines.Mod.Tests
{
    public class FixturePlanTests
    {
        private static readonly Func<double, double, bool> None = (x, z) => false;
        private static readonly Func<double, double, double> Flat = (x, z) => 0;

        [Fact]
        public void FlattestFindsLatticeAlignedPlateau()
        {
            // Plateau at 100 over [150,470]^2 fits exactly one 300 m window (centre 310,310); ramp elsewhere.
            Func<double, double, double> h = (x, z) => x >= 150 && x <= 470 && z >= 150 && z <= 470 ? 100 : 0.1 * (x + z);
            Assert.True(FixturePlan.FindFlattest(h, None, 0, 0, 1000, 1000, 800, 800, out double cx, out double cz, out double spread));
            Assert.InRange(cx, 290, 330);
            Assert.InRange(cz, 290, 330);
            Assert.InRange(spread, 0, 0.01);
        }

        [Fact]
        public void FlatTerrainPicksCentreClosestToPreference()
        {
            Assert.True(FixturePlan.FindFlattest(Flat, None, 0, 0, 600, 600, 405, 205, out double cx, out double cz, out double spread));
            Assert.Equal(410, cx, 3);
            Assert.Equal(210, cz, 3);
            Assert.Equal(0, spread, 6);
        }

        [Fact]
        public void BlockedSamplesDisqualifyACandidate()
        {
            Func<double, double, bool> water = (x, z) => x >= 300 && x <= 320 && z >= 300 && z <= 320;
            Assert.True(FixturePlan.FindFlattest(Flat, water, 0, 0, 1000, 1000, 310, 310, out double cx, out double cz, out _));
            Assert.False(Math.Abs(cx - 310) <= 150 && Math.Abs(cz - 310) <= 150);
        }

        [Fact]
        public void SpreadIsMaxMinusMinOfSamples()
        {
            // Single 300 m window (area exactly 300): heights 0..3 across x.
            Func<double, double, double> h = (x, z) => x / 100.0;
            Assert.True(FixturePlan.FindFlattest(h, None, 0, 0, 300, 300, 150, 150, out double cx, out double cz, out double spread));
            Assert.Equal(150, cx, 3);
            Assert.Equal(150, cz, 3);
            Assert.Equal(3.0, spread, 3);
        }

        [Fact]
        public void RectangleNarrowerThan300InEitherAxisFails()
        {
            Assert.False(FixturePlan.FindFlattest(Flat, None, 0, 0, 200, 1000, 100, 500, out _, out _, out _));
            Assert.False(FixturePlan.FindFlattest(Flat, None, 0, 0, 1000, 200, 500, 100, out _, out _, out _));
        }

        [Fact]
        public void AllBlockedFails()
        {
            Assert.False(FixturePlan.FindFlattest(Flat, (x, z) => true, 0, 0, 1000, 1000, 500, 500, out _, out _, out _));
        }

        [Fact]
        public void GroundRoadIsFiveLevelNodesSouthOfCentre()
        {
            FixtureNode[] n = FixturePlan.GroundRoad(1000, 2000);
            Assert.Equal(5, n.Length);
            double[] xs = { 900, 950, 1000, 1050, 1100 };
            for (int i = 0; i < 5; i++)
            {
                Assert.Equal(xs[i], n[i].X, 6);
                Assert.Equal(1940, n[i].Z, 6);
                Assert.Equal(0, n[i].Elevation, 6);
            }
        }

        [Fact]
        public void ElevatedRoadRampsEightMetresThenLevels()
        {
            FixtureNode[] n = FixturePlan.ElevatedRoad(1000, 2000);
            Assert.Equal(6, n.Length);
            double[] xs = { 900, 940, 980, 1020, 1060, 1100 };
            double[] el = { 0, 4, 8, 8, 8, 8 };
            for (int i = 0; i < 6; i++)
            {
                Assert.Equal(xs[i], n[i].X, 6);
                Assert.Equal(2060, n[i].Z, 6);
                Assert.Equal(el[i], n[i].Elevation, 6);
            }
        }

        [Fact]
        public void RoadsAre200mLongRampIs80mAndLevelIs120m()
        {
            FixtureNode[] g = FixturePlan.GroundRoad(0, 0), e = FixturePlan.ElevatedRoad(0, 0);
            Assert.Equal(200, g[4].X - g[0].X, 6);
            Assert.Equal(200, e[5].X - e[0].X, 6);
            Assert.Equal(80, e[2].X - e[0].X, 6);
            Assert.Equal(120, e[5].X - e[2].X, 6);
        }

        [Fact]
        public void RoadsAre120mApartAndInsideTheAreaSquare()
        {
            FixtureNode[] g = FixturePlan.GroundRoad(500, 700), e = FixturePlan.ElevatedRoad(500, 700);
            Assert.Equal(120, e[0].Z - g[0].Z, 6);
            foreach (FixtureNode n in g) AssertInside(n, 500, 700);
            foreach (FixtureNode n in e) AssertInside(n, 500, 700);
        }

        private static void AssertInside(FixtureNode n, double cx, double cz)
        {
            Assert.InRange(n.X, cx - FixturePlan.AreaSize / 2, cx + FixturePlan.AreaSize / 2);
            Assert.InRange(n.Z, cz - FixturePlan.AreaSize / 2, cz + FixturePlan.AreaSize / 2);
        }

        private static Func<double, double, double> Plane(double degrees)
        {
            double t = Math.Tan(degrees * Math.PI / 180);
            return (x, z) => x * t;
        }

        [Fact]
        public void TiltedPlaneAt20DegreesIsFound()
        {
            Assert.True(FixturePlan.SteepestSlope(Plane(20), None, 1000, 1000, out SlopeSpot s));
            Assert.Equal(20, s.Degrees, 0);
            Assert.InRange(s.Degrees, 19.5, 20.5);
            Assert.Equal(1, s.UpX, 2);
            Assert.Equal(0, s.UpZ, 2);
        }

        [Fact]
        public void FlatGroundHasNoSlope()
        {
            Assert.False(FixturePlan.SteepestSlope(Flat, None, 0, 0, out _));
        }

        [Fact]
        public void FortyFiveDegreePlaneIsTooSteep()
        {
            Assert.False(FixturePlan.SteepestSlope(Plane(45), None, 0, 0, out _));
        }

        // Flat to x=100, 15 degrees to bandEnd, then 25 degrees (cx = 0).
        private static Func<double, double, double> Hills(double steepStart)
        {
            double t15 = Math.Tan(15 * Math.PI / 180), t25 = Math.Tan(25 * Math.PI / 180);
            return (x, z) =>
            {
                double h = 0;
                if (x > 100) h += (Math.Min(x, steepStart) - 100) * t15;
                if (x > steepStart) h += (x - steepStart) * t25;
                return h;
            };
        }

        [Fact]
        public void PrefersTheSteeperQualifyingBand()
        {
            Assert.True(FixturePlan.SteepestSlope(Hills(200), None, 0, 0, out SlopeSpot s));
            Assert.InRange(s.Degrees, 24.5, 25.5);
            Assert.True(s.X >= 200);
        }

        [Fact]
        public void SteepBandBeyond300mIsIgnored()
        {
            Assert.True(FixturePlan.SteepestSlope(Hills(320), None, 0, 0, out SlopeSpot s));
            Assert.InRange(s.Degrees, 14.5, 15.5);
            Assert.True(Math.Sqrt(s.X * s.X + s.Z * s.Z) <= 300);
        }

        [Fact]
        public void BlockedSteepBandFallsBackToTheGentlerOne()
        {
            Assert.True(FixturePlan.SteepestSlope(Hills(200), (x, z) => x >= 200, 0, 0, out SlopeSpot s));
            Assert.InRange(s.Degrees, 14.5, 15.5);
            Assert.True(s.X < 200);
        }
    }
}
