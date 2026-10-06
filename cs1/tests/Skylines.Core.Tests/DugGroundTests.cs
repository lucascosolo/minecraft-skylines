using System;
using System.Collections.Generic;
using System.Linq;
using Skylines.Core.Voxels;
using Xunit;

namespace Skylines.Core.Tests
{
    public class DugGroundTests
    {
        private static DugGround Flat(float surface) { return new DugGround((x, z) => surface); }

        private static void Box(DugGround g, int x0, int x1, int y0, int y1, int z0, int z1)
        {
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++) g.Set(x, y, z, true);
        }

        private static float[] Normal(DugQuad q)
        {
            float ux = q.Bx - q.Ax, uy = q.By - q.Ay, uz = q.Bz - q.Az;
            float vx = q.Cx - q.Ax, vy = q.Cy - q.Ay, vz = q.Cz - q.Az;
            return new[] { uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx };
        }

        private static float[] Xs(DugQuad q) { return new[] { q.Ax, q.Bx, q.Cx, q.Dx }; }
        private static float[] Ys(DugQuad q) { return new[] { q.Ay, q.By, q.Cy, q.Dy }; }
        private static float[] Zs(DugQuad q) { return new[] { q.Az, q.Bz, q.Cz, q.Dz }; }

        private static List<DugQuad> Faces(DugGround g, int a, int b, int c, int d)
        {
            var l = new List<DugQuad>();
            g.Faces(a, b, c, d, l);
            return l;
        }

        private static List<DugQuad> Skirts(DugGround g, int a, int b, int c, int d, Func<float, float, float> s)
        {
            var l = new List<DugQuad>();
            g.Skirts(a, b, c, d, s, l);
            return l;
        }

        private static int Count(List<DugQuad> l, DugQuadKind k) { return l.Count(q => q.Kind == k); }

        [Theory]
        [InlineData(10.0f, 9)]
        [InlineData(10.4f, 9)]
        [InlineData(10.6f, 10)]
        [InlineData(9.5f, 8)]
        public void SolidTopIsHighestCellWithCentreBelowSurface(float surface, int expected)
        {
            Assert.Equal(expected, DugGround.SolidTop(surface));
        }

        [Fact]
        public void SurfaceIsCachedPerColumnUntilForgotten()
        {
            int calls = 0;
            var g = new DugGround((x, z) => { calls++; return 10f; });
            g.TopOf(1, 2); g.TopOf(1, 2); g.IsDug(1, 9, 2); g.IsSolid(1, 5, 2); g.IsOpen(1, 2);
            Assert.Equal(1, calls);
            g.TopOf(3, 3);
            Assert.Equal(2, calls);
            g.ForgetSurface();
            g.TopOf(1, 2);
            Assert.Equal(3, calls);
        }

        [Fact]
        public void ForgetSurfaceKeepsEditsAndClearDropsThem()
        {
            var g = Flat(10f);
            g.Set(0, 9, 0, true);
            g.ForgetSurface();
            Assert.True(g.IsDug(0, 9, 0));
            g.Clear();
            Assert.False(g.IsDug(0, 9, 0));
            Assert.Equal(0, g.DugCount);
        }

        [Fact]
        public void DugAndSolidFollowTheColumnTop()
        {
            var g = Flat(10f);
            g.Set(0, 9, 0, true);
            Assert.Equal(9, g.TopOf(0, 0));
            Assert.True(g.IsDug(0, 9, 0));
            Assert.False(g.IsSolid(0, 9, 0));
            Assert.True(g.IsSolid(0, 8, 0));
            Assert.False(g.IsDug(0, 8, 0));
            Assert.False(g.IsSolid(0, 10, 0));
            Assert.True(g.IsOpen(0, 0));
            Assert.False(g.IsOpen(1, 0));
        }

        [Fact]
        public void SetFalseRemovesTheEdit()
        {
            var g = Flat(10f);
            g.Set(0, 9, 0, true);
            Assert.Equal(1, g.DugCount);
            g.Set(0, 9, 0, false);
            Assert.Equal(0, g.DugCount);
            Assert.False(g.IsDug(0, 9, 0));
            Assert.False(g.IsOpen(0, 0));
        }

        [Fact]
        public void EditsAboveTheSurfaceAreIgnoredEverywhere()
        {
            var g = Flat(10f);
            g.Set(0, 15, 0, true);
            Assert.Equal(0, g.DugCount);
            Assert.False(g.IsDug(0, 15, 0));
            Assert.False(g.IsSolid(0, 15, 0));
            Assert.False(g.IsOpen(0, 0));
            Assert.Empty(g.OpenColumns(-5, -5, 5, 5));
            Assert.Empty(Faces(g, -5, -5, 5, 5));
            Assert.Empty(Skirts(g, -5, -5, 5, 5, (x, z) => 50f));
        }

        [Fact]
        public void PitHasNineOpenColumnsSortedByXThenZ()
        {
            var g = Flat(10f);
            Box(g, 0, 2, 8, 9, 0, 2);
            Assert.Equal(18, g.DugCount);
            var cols = g.OpenColumns(-1, -1, 4, 4);
            Assert.Equal(9, cols.Count);
            var expected = new List<KeyValuePair<int, int>>();
            for (int x = 0; x <= 2; x++) for (int z = 0; z <= 2; z++) expected.Add(new KeyValuePair<int, int>(x, z));
            Assert.Equal(expected, cols);
        }

        [Fact]
        public void OpenColumnRangeIsHalfOpen()
        {
            var g = Flat(10f);
            Box(g, 0, 2, 8, 9, 0, 2);
            Assert.Equal(4, g.OpenColumns(0, 0, 2, 2).Count);
            Assert.Empty(g.OpenColumns(0, 0, 0, 0));
            Assert.Empty(g.OpenColumns(3, 3, 9, 9));
        }

        [Fact]
        public void PitFacesAreNineFloorsAndTwentyFourWallsNoCeilings()
        {
            var g = Flat(10f);
            Box(g, 0, 2, 8, 9, 0, 2);
            var q = Faces(g, -1, -1, 4, 4);
            Assert.Equal(33, q.Count);
            Assert.Equal(9, Count(q, DugQuadKind.Floor));
            Assert.Equal(24, Count(q, DugQuadKind.Wall));
            Assert.Equal(0, Count(q, DugQuadKind.Ceiling));
        }

        [Fact]
        public void FloorLiesOnDugCellBottomFacingUp()
        {
            var g = Flat(10f);
            g.Set(0, 9, 0, true);
            var floor = Faces(g, 0, 0, 1, 1).Single(f => f.Kind == DugQuadKind.Floor);
            Assert.All(Ys(floor), y => Assert.Equal(9f, y));
            Assert.Equal(new[] { 0f, 1f }, Xs(floor).Distinct().OrderBy(v => v).ToArray());
            Assert.Equal(new[] { 0f, 1f }, Zs(floor).Distinct().OrderBy(v => v).ToArray());
            Assert.True(Normal(floor)[1] > 0);
        }

        [Fact]
        public void WallsSitOnSharedPlaneFacingIntoTheDugCell()
        {
            var g = Flat(10f);
            g.Set(0, 9, 0, true);
            var walls = Faces(g, 0, 0, 1, 1).Where(f => f.Kind == DugQuadKind.Wall).ToList();
            Assert.Equal(4, walls.Count);
            var west = walls.Single(w => Xs(w).All(v => v == 0f));
            var east = walls.Single(w => Xs(w).All(v => v == 1f));
            var north = walls.Single(w => Zs(w).All(v => v == 0f));
            var south = walls.Single(w => Zs(w).All(v => v == 1f));
            Assert.True(Normal(west)[0] > 0);
            Assert.True(Normal(east)[0] < 0);
            Assert.True(Normal(north)[2] > 0);
            Assert.True(Normal(south)[2] < 0);
            foreach (var w in walls)
                Assert.Equal(new[] { 9f, 10f }, Ys(w).Distinct().OrderBy(v => v).ToArray());
        }

        [Fact]
        public void TunnelHasCeilingsFloorsAndWallsButNoSkirts()
        {
            var g = Flat(20f);
            Box(g, 0, 0, 10, 11, 0, 7);
            Assert.Empty(g.OpenColumns(-5, -5, 5, 10));
            var q = Faces(g, -1, -1, 2, 9);
            Assert.Equal(8, Count(q, DugQuadKind.Ceiling));
            Assert.Equal(8, Count(q, DugQuadKind.Floor));
            Assert.Equal(36, Count(q, DugQuadKind.Wall));
            Assert.Equal(52, q.Count);
            var ceiling = q.First(f => f.Kind == DugQuadKind.Ceiling);
            Assert.All(Ys(ceiling), y => Assert.Equal(12f, y));
            Assert.True(Normal(ceiling)[1] < 0);
            Assert.Equal(10f, Ys(q.First(f => f.Kind == DugQuadKind.Floor)).Min());
            Assert.Empty(Skirts(g, -1, -1, 2, 9, (x, z) => 50f));
        }

        [Fact]
        public void FacesIgnoreCellsOutsideTheRange()
        {
            var g = Flat(10f);
            Box(g, 0, 2, 8, 9, 0, 2);
            Assert.Empty(Faces(g, 5, 5, 9, 9));
            var centre = Faces(g, 1, 1, 2, 2);
            Assert.Single(centre);
            Assert.Equal(DugQuadKind.Floor, centre[0].Kind);
            var corner = Faces(g, 0, 0, 1, 1);
            Assert.Equal(5, corner.Count);
        }

        [Fact]
        public void PitSkirtsAbsentWhenSurfaceEqualsLo()
        {
            var g = Flat(10f);
            Box(g, 0, 2, 8, 9, 0, 2);
            Assert.Empty(Skirts(g, -1, -1, 4, 4, (x, z) => 10f));
        }

        [Fact]
        public void PitSkirtsRiseToTerrainSurfaceAndFaceInward()
        {
            var g = Flat(10f);
            Box(g, 0, 2, 8, 9, 0, 2);
            var s = Skirts(g, -1, -1, 4, 4, (x, z) => 10.7f);
            Assert.Equal(12, s.Count);
            Assert.All(s, q => Assert.Equal(DugQuadKind.Skirt, q.Kind));
            foreach (var q in s)
            {
                var ys = Ys(q);
                Assert.Equal(2, ys.Count(y => Math.Abs(y - 10f) < 1e-5f));
                Assert.Equal(2, ys.Count(y => Math.Abs(y - 10.7f) < 1e-5f));
                var n = Normal(q);
                Assert.True(Math.Abs(n[1]) < 1e-5f);
                float mx = Xs(q).Average(), mz = Zs(q).Average();
                Assert.True(n[0] * (1.5f - mx) + n[2] * (1.5f - mz) > 0);
            }
        }

        [Fact]
        public void SkirtIsOmittedOnlyWhenBothCornersAreAtOrBelowLo()
        {
            var g = Flat(10f);
            g.Set(0, 9, 0, true);
            Func<float, float, float> s = (cx, cz) => cx == 0f && cz == 0f ? 10.5f : 10f;
            var q = Skirts(g, 0, 0, 1, 1, s);
            Assert.Equal(2, q.Count);
            Assert.Contains(q, k => Xs(k).All(v => v == 0f));
            Assert.Contains(q, k => Zs(k).All(v => v == 0f));
            foreach (var k in q) Assert.Equal(10.5f, Ys(k).Max());
        }

        [Fact]
        public void SkirtBottomUsesLowerOfTheTwoColumnTops()
        {
            // x < 0: surface 6 (top 5); x == 0: surface 10 (top 9); x >= 1: surface 14 (top 13).
            var g = new DugGround((x, z) => x < 0 ? 6f : x == 0 ? 10f : 14f);
            g.Set(0, 9, 0, true);
            var q = Skirts(g, 0, 0, 1, 1, (cx, cz) => 20f);
            Assert.Equal(4, q.Count);
            var west = q.Single(k => Xs(k).All(v => v == 0f));
            var east = q.Single(k => Xs(k).All(v => v == 1f));
            Assert.Equal(6f, Ys(west).Min());
            Assert.Equal(10f, Ys(east).Min());
            Assert.Equal(20f, Ys(west).Max());
            Assert.True(Normal(west)[0] > 0);
            Assert.True(Normal(east)[0] < 0);
        }
    }
}
