using System;
using System.Collections.Generic;
using System.Linq;
using Skylines.Core.Streaming;
using Xunit;

namespace Skylines.Core.Tests
{
    public class RegionGridTests
    {
        [Theory]
        [InlineData(0f, 0f, 0, 0)]
        [InlineData(15.99f, 31.5f, 0, 1)]
        [InlineData(16f, 16f, 1, 1)]
        [InlineData(-0.01f, -16f, -1, -1)]
        [InlineData(-16.01f, 0f, -2, 0)]
        [InlineData(-100f, 100f, -7, 6)]
        public void RegionOfFloorsIncludingNegatives(float x, float z, int erx, int erz)
        {
            var g = new RegionGrid(16);
            int rx, rz;
            g.RegionOf(x, z, out rx, out rz);
            Assert.Equal(erx, rx);
            Assert.Equal(erz, rz);
        }

        [Fact]
        public void BoundsAreTheRegionSquare()
        {
            var g = new RegionGrid(16);
            float a, b, c, d;
            g.Bounds(-2, 3, out a, out b, out c, out d);
            Assert.Equal(new float[] { -32, 48, -16, 64 }, new[] { a, b, c, d });
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(-1, -1)]
        [InlineData(int.MaxValue, int.MinValue)]
        [InlineData(int.MinValue, int.MaxValue)]
        [InlineData(-5, 7)]
        [InlineData(1, -1)]
        public void KeyRoundTrips(int rx, int rz)
        {
            int ox, oz;
            RegionGrid.Unkey(RegionGrid.Key(rx, rz), out ox, out oz);
            Assert.Equal(rx, ox);
            Assert.Equal(rz, oz);
        }

        [Fact]
        public void KeysAreDistinctPerRegion()
        {
            var keys = new HashSet<long>();
            for (int x = -3; x <= 3; x++)
                for (int z = -3; z <= 3; z++) keys.Add(RegionGrid.Key(x, z));
            Assert.Equal(49, keys.Count);
        }

        [Fact]
        public void RegionsWithinCoversExactlyTheIntersectingSquares()
        {
            var g = new RegionGrid(16);
            // Centre of region (0,0); radius 10 reaches the neighbours' edges (8 away) but not their corners (8*sqrt2 = 11.3).
            var keys = g.RegionsWithin(8, 8, 10);
            var set = new HashSet<long>(keys);
            Assert.Equal(keys.Count, set.Count);
            var expected = new HashSet<long> { RegionGrid.Key(0, 0), RegionGrid.Key(1, 0), RegionGrid.Key(-1, 0), RegionGrid.Key(0, 1), RegionGrid.Key(0, -1) };
            Assert.True(expected.SetEquals(set));
        }

        [Fact]
        public void RegionsWithinIsSortedByDistanceThenKey()
        {
            var g = new RegionGrid(16);
            float px = 3, pz = 5, r = 40;
            var keys = g.RegionsWithin(px, pz, r);
            Func<long, double> dist = k =>
            {
                int rx, rz; RegionGrid.Unkey(k, out rx, out rz);
                float a, b, c, d; g.Bounds(rx, rz, out a, out b, out c, out d);
                double dx = Math.Max(Math.Max(a - px, 0), px - c), dz = Math.Max(Math.Max(b - pz, 0), pz - d);
                return Math.Sqrt(dx * dx + dz * dz);
            };
            Assert.True(keys.Count > 4);
            Assert.Equal(RegionGrid.Key(0, 0), keys[0]);
            Assert.All(keys, k => Assert.True(dist(k) <= r));
            for (int i = 1; i < keys.Count; i++)
            {
                double d0 = dist(keys[i - 1]), d1 = dist(keys[i]);
                Assert.True(d0 < d1 + 1e-9);
                if (Math.Abs(d0 - d1) < 1e-9) Assert.True(keys[i - 1] < keys[i]);
            }
        }

        [Fact]
        public void RegionsWithinOnCornerOfFourRegionsTiesBreakByKey()
        {
            var g = new RegionGrid(16);
            var keys = g.RegionsWithin(0, 0, 5);
            Assert.Equal(4, keys.Count);
            Assert.Equal(keys.OrderBy(k => k).ToList(), keys);
        }
    }

    public class RegionStreamPlannerTests
    {
        static RegionStreamPlanner Make() { return new RegionStreamPlanner(new RegionGrid(16), 20, 40); }

        [Fact]
        public void EpochStartsAtOne()
        {
            Assert.Equal(1u, Make().Epoch);
        }

        [Fact]
        public void NextHonoursBudgetAndNearestFirst()
        {
            var p = Make();
            var first = p.Next(8, 8, 3);
            Assert.Equal(3, first.Count);
            Assert.Equal(RegionGrid.Key(0, 0), first[0]);
            Assert.True(p.IsSent(0, 0));
        }

        [Fact]
        public void NeverResendsAndEventuallyDrains()
        {
            var p = Make();
            var all = new List<long>();
            for (int i = 0; i < 50; i++)
            {
                var batch = p.Next(8, 8, 2);
                if (batch.Count == 0) break;
                all.AddRange(batch);
            }
            Assert.Equal(all.Count, new HashSet<long>(all).Count);
            var expected = new RegionGrid(16).RegionsWithin(8, 8, 20);
            Assert.True(new HashSet<long>(expected).SetEquals(all));
            Assert.Empty(p.Next(8, 8, 10));
        }

        [Fact]
        public void OnlyRegionsWithinRadiusAreReturned()
        {
            var p = Make();
            var got = p.Next(8, 8, 1000);
            var allowed = new HashSet<long>(new RegionGrid(16).RegionsWithin(8, 8, 20));
            Assert.NotEmpty(got);
            Assert.All(got, k => Assert.Contains(k, allowed));
        }

        [Fact]
        public void RegionsBeyondEvictRadiusAreForgottenAndResentOnReturn()
        {
            var p = Make();
            p.Next(8, 8, 1000);
            Assert.True(p.IsSent(0, 0));
            p.Next(2008, 8, 1000);               // far away: region (0,0) is beyond evictRadius
            Assert.False(p.IsSent(0, 0));
            var back = p.Next(8, 8, 1000);
            Assert.Contains(RegionGrid.Key(0, 0), back);
        }

        [Fact]
        public void RegionsBetweenRadiusAndEvictRadiusAreKept()
        {
            var p = Make();
            p.Next(8, 8, 1000);
            p.Next(8 + 30, 8, 1000);             // region (0,0) is 22..30 away: outside radius, inside evictRadius
            Assert.True(p.IsSent(0, 0));
            Assert.DoesNotContain(RegionGrid.Key(0, 0), p.Next(8, 8, 1000));
        }

        [Fact]
        public void InvalidateMakesOneRegionDueAgain()
        {
            var p = Make();
            p.Next(8, 8, 1000);
            p.Invalidate(1, 0);
            Assert.False(p.IsSent(1, 0));
            Assert.True(p.IsSent(0, 0));
            var again = p.Next(8, 8, 1000);
            Assert.Equal(new List<long> { RegionGrid.Key(1, 0) }, again);
        }

        [Fact]
        public void ResetForgetsEverythingAndBumpsEpoch()
        {
            var p = Make();
            var first = p.Next(8, 8, 1000);
            p.Reset();
            Assert.Equal(2u, p.Epoch);
            Assert.False(p.IsSent(0, 0));
            Assert.Equal(first, p.Next(8, 8, 1000));
            p.Reset();
            Assert.Equal(3u, p.Epoch);
        }
    }
}
