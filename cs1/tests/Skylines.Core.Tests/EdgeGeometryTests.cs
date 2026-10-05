using System;
using System.Collections.Generic;
using System.Linq;
using Skylines.Core.Geometry;
using Xunit;
using static Skylines.Core.Tests.TriHelpers;

namespace Skylines.Core.Tests
{
    public class StripBetweenTests
    {
        const ushort Flags = 0x2A5;

        static Bezier3D Seg(float x0, float y0, float z0, float x1, float y1, float z1)
        {
            return new Bezier3D
            {
                Ax = x0, Ay = y0, Az = z0,
                Bx = x0 + (x1 - x0) / 3, By = y0 + (y1 - y0) / 3, Bz = z0 + (z1 - z0) / 3,
                Cx = x0 + 2 * (x1 - x0) / 3, Cy = y0 + 2 * (y1 - y0) / 3, Cz = z0 + 2 * (z1 - z0) / 3,
                Dx = x1, Dy = y1, Dz = z1,
            };
        }

        // Quarter turn about the origin from (r,0) to (0,r).
        static Bezier3D Arc(float r)
        {
            const float k = 0.5522847f;
            return new Bezier3D { Ax = r, Ay = 1, Az = 0, Bx = r, By = 1, Bz = k * r, Cx = k * r, Cy = 1, Cz = r, Dx = 0, Dy = 1, Dz = r };
        }

        static float[] P(Bezier3D c, double t)
        {
            double u = 1 - t, a = u * u * u, b = 3 * u * u * t, d = 3 * u * t * t, e = t * t * t;
            return new[]
            {
                (float)(a * c.Ax + b * c.Bx + d * c.Cx + e * c.Dx),
                (float)(a * c.Ay + b * c.By + d * c.Cy + e * c.Dy),
                (float)(a * c.Az + b * c.Bz + d * c.Cz + e * c.Dz),
            };
        }

        static double Len(Bezier3D c)
        {
            double len = 0; var p = P(c, 0);
            for (int i = 1; i <= 16; i++)
            {
                var q = P(c, i / 16.0);
                len += Math.Sqrt(Math.Pow(q[0] - p[0], 2) + Math.Pow(q[1] - p[1], 2) + Math.Pow(q[2] - p[2], 2));
                p = q;
            }
            return len;
        }

        static int N(Bezier3D l, Bezier3D r, float step)
        {
            return Math.Max(1, (int)Math.Ceiling(Math.Max(Len(l), Len(r)) / step));
        }

        static bool Same(float[] a, float[] b, float dy = 0)
        {
            return Near(a[0], b[0]) && Near(a[1], b[1] + dy) && Near(a[2], b[2]);
        }

        static List<float[]> Samples(Bezier3D c, int n)
        {
            return Enumerable.Range(0, n + 1).Select(k => P(c, k / (double)n)).ToList();
        }

        static TriangleBuffer Build(Bezier3D l, Bezier3D r, float step, float thickness)
        {
            var b = new TriangleBuffer();
            Strip.Between(l, r, step, thickness, Flags, b);
            return b;
        }

        static bool Up(Tri t) { return t.Normal()[1] > 0; }
        static bool Down(Tri t) { return t.Normal()[1] < 0; }
        static bool Wall(Tri t)
        {
            var n = t.Normal();
            double m = Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
            return m > 0 && Math.Abs(n[1]) <= 1e-3 * m;
        }

        // Asserts that the top surface uses exactly the samples of both curves at t = k/n.
        static void AssertTopVerticesOnSamples(List<Tri> top, Bezier3D l, Bezier3D r, int n)
        {
            var ls = Samples(l, n); var rs = Samples(r, n);
            var verts = top.SelectMany(t => t.Verts()).ToList();
            Assert.All(verts, v => Assert.True(ls.Any(s => Same(v, s)) || rs.Any(s => Same(v, s)), "top vertex off both curves"));
            foreach (var s in ls.Concat(rs))
                Assert.True(verts.Any(v => Same(v, s)), "sample missing from top surface");
        }

        // Walls on the curve `own` must point away from `other` (horizontal dot test).
        static void AssertWallsFace(List<Tri> walls, Bezier3D own, Bezier3D other, int n, float thickness)
        {
            var ownS = Samples(own, n);
            var mine = walls.Where(w => w.Verts().All(v => ownS.Any(s => Same(v, s) || Same(v, s, -thickness)))).ToList();
            Assert.Equal(2 * n, mine.Count);
            var o = P(own, 0.5); var q = P(other, 0.5);
            foreach (var w in mine)
            {
                var nn = w.Normal();
                Assert.True(nn[0] * (o[0] - q[0]) + nn[2] * (o[2] - q[2]) > 0, "wall faces the other edge");
            }
        }

        static void CheckFull(Bezier3D l, Bezier3D r, float step, float thickness)
        {
            int n = N(l, r, step);
            var b = Build(l, r, step, thickness);
            var t = All(b);
            Assert.Equal(thickness > 0 ? 8 * n : 2 * n, b.Count);
            Assert.All(Enumerable.Range(0, b.Count), i => Assert.Equal(Flags, b.Flags[i]));
            var top = t.Where(Up).ToList();
            Assert.Equal(2 * n, top.Count);
            AssertTopVerticesOnSamples(top, l, r, n);
            if (!(thickness > 0)) return;
            var down = t.Where(Down).ToList();
            Assert.Equal(2 * n, down.Count);
            var all = Samples(l, n).Concat(Samples(r, n)).ToList();
            Assert.All(down.SelectMany(d => d.Verts()), v => Assert.True(all.Any(s => Same(v, s, -thickness)), "bottom vertex not a lowered top vertex"));
            var walls = t.Where(Wall).ToList();
            Assert.Equal(4 * n, walls.Count);
            AssertWallsFace(walls, l, r, n, thickness);
            AssertWallsFace(walls, r, l, n, thickness);
        }

        [Fact]
        public void StraightParallelEdgesTopOnly()
        {
            var l = Seg(0, 1, 2, 10, 1, 2); var r = Seg(0, 1, -2, 10, 1, -2);
            var b = Build(l, r, 4, 0);
            Assert.Equal(6, b.Count); // n = ceil(10/4) = 3
            CheckFull(l, r, 4, 0);
        }

        [Fact]
        public void StraightParallelEdgesWithThicknessHaveEightPerInterval()
        {
            var l = Seg(0, 1, 2, 10, 1, 2); var r = Seg(0, 1, -2, 10, 1, -2);
            Assert.Equal(24, Build(l, r, 4, 0.5f).Count);
            CheckFull(l, r, 4, 0.5f);
        }

        [Fact]
        public void NegativeThicknessAddsTopOnly()
        {
            var l = Seg(0, 1, 2, 10, 1, 2); var r = Seg(0, 1, -2, 10, 1, -2);
            Assert.Equal(6, Build(l, r, 4, -1).Count);
        }

        [Fact]
        public void SwappedArgumentsStillFaceUp()
        {
            var l = Seg(0, 1, 2, 10, 1, 2); var r = Seg(0, 1, -2, 10, 1, -2);
            CheckFull(r, l, 4, 0);
            CheckFull(r, l, 4, 0.5f);
            Assert.All(All(Build(l, r, 4, 0)), t => Assert.True(Up(t)));
        }

        [Fact]
        public void CurvedEdgesOfDifferentRadiusShareOneN()
        {
            var inner = Arc(10); var outer = Arc(14);
            Assert.True(N(inner, outer, 5) > N(inner, inner, 5)); // outer edge decides n
            CheckFull(inner, outer, 5, 0);
            CheckFull(inner, outer, 5, 0.75f);
            CheckFull(outer, inner, 5, 0.75f);
        }

        [Fact]
        public void SlopedEdgesKeepExactHeights()
        {
            var l = Seg(0, 1, 2, 10, 5, 2); var r = Seg(0, 2, -2, 10, 4, -2);
            CheckFull(l, r, 3, 0);
            CheckFull(r, l, 3, 0.5f);
        }

        [Fact]
        public void TrimmedShorterEdgeUsesSameN()
        {
            var l = Seg(0, 1, 2, 10, 1, 2); var r = Seg(0, 1, -2, 6, 1, -2);
            Assert.Equal(6, Build(l, r, 4, 0).Count); // n = 3 from the longer edge
            CheckFull(l, r, 4, 0.5f);
            CheckFull(r, l, 4, 0);
        }

        [Fact]
        public void ShortEdgesGiveSingleInterval()
        {
            var l = Seg(0, 1, 1, 1, 1, 1); var r = Seg(0, 1, -1, 1, 1, -1);
            Assert.Equal(2, Build(l, r, 4, 0).Count);
        }

        [Fact]
        public void ZeroLengthEdgesAddNothing()
        {
            var l = Seg(3, 1, 2, 3, 1, 2); var r = Seg(3, 1, -2, 3, 1, -2);
            Assert.Equal(0, Build(l, r, 4, 0).Count);
            Assert.Equal(0, Build(l, r, 4, 1).Count);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        [InlineData(float.NaN)]
        public void NonPositiveOrNaNStepThrows(float step)
        {
            var l = Seg(0, 1, 2, 10, 1, 2); var r = Seg(0, 1, -2, 10, 1, -2);
            Assert.Throws<ArgumentOutOfRangeException>(() => Strip.Between(l, r, step, 0, 0, new TriangleBuffer()));
        }
    }

    public class DiscPolygonTests
    {
        const ushort Flags = 0x91;
        const float Cx = 1, Cy = 2, Cz = -1;

        // Square corners (cx +/- 3, y, cz +/- 3) with distinct heights, deliberately shuffled.
        static float[] Ring()
        {
            return new float[]
            {
                -2, 3, -4,   // angle -135
                 4, 1,  2,   // angle +45
                -2, 4,  2,   // angle +135
                 4, 5, -4,   // angle -45
            };
        }

        static TriangleBuffer Build(float[] ring, int count, float thickness)
        {
            var b = new TriangleBuffer();
            Disc.Polygon(Cx, Cy, Cz, ring, count, thickness, Flags, b);
            return b;
        }

        static string Key(float[] v) { return Math.Round(v[0], 3) + "," + Math.Round(v[1], 3) + "," + Math.Round(v[2], 3); }

        static bool Horizontal(Tri t)
        {
            var n = t.Normal();
            double m = Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
            return m > 0 && Math.Abs(n[1]) <= 1e-3 * m;
        }

        [Fact]
        public void ShuffledSquareGivesUpFacingFanOverExactVertices()
        {
            var b = Build(Ring(), 4, 0);
            Assert.Equal(4, b.Count);
            Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(Flags, b.Flags[i]));
            var t = All(b);
            Assert.All(t, x => Assert.True(x.Normal()[1] > 0));
            var keys = t.SelectMany(x => x.Verts()).Select(Key).Distinct().OrderBy(s => s).ToList();
            var expected = new[] { Key(new float[] { Cx, Cy, Cz }), "-2,3,-4", "4,1,2", "-2,4,2", "4,5,-4" }.OrderBy(s => s).ToList();
            Assert.Equal(expected, keys);
        }

        [Fact]
        public void EveryTriangleUsesCentreAndTwoAngularNeighbours()
        {
            foreach (var t in All(Build(Ring(), 4, 0)))
            {
                var ring = t.Verts().Where(v => !(Near(v[0], Cx) && Near(v[2], Cz))).ToList();
                Assert.Equal(2, ring.Count);
                double d = Math.Sqrt(Math.Pow(ring[0][0] - ring[1][0], 2) + Math.Pow(ring[0][2] - ring[1][2], 2));
                Assert.True(Near((float)d, 6), "ring vertices must be adjacent by angle, not diagonal");
            }
        }

        [Fact]
        public void RingArrayIsNotModified()
        {
            var ring = Ring(); var copy = (float[])ring.Clone();
            Build(ring, 4, 0.5f);
            Assert.Equal(copy, ring);
        }

        [Fact]
        public void CountSmallerThanArrayIgnoresExtraValues()
        {
            var ring = Ring().Concat(new float[] { 100, 100, 100 }).ToArray();
            var b = Build(ring, 4, 0);
            Assert.Equal(4, b.Count);
            Assert.DoesNotContain(All(b).SelectMany(x => x.Verts()), v => v[0] > 50);
        }

        [Fact]
        public void ThicknessAddsBottomFanAndWallsAwayFromCentre()
        {
            const float th = 0.5f;
            var b = Build(Ring(), 4, th);
            Assert.Equal(16, b.Count);
            Assert.All(Enumerable.Range(0, 16), i => Assert.Equal(Flags, b.Flags[i]));
            var t = All(b);
            Assert.Equal(4, t.Count(x => x.Normal()[1] > 0));

            var down = t.Where(x => x.Normal()[1] < 0).ToList();
            Assert.Equal(4, down.Count);
            var ringKeys = new[] { "-2,2.5,-4", "4,0.5,2", "-2,3.5,2", "4,4.5,-4" };
            var centreKey = Key(new float[] { Cx, Cy - th, Cz });
            var downKeys = down.SelectMany(x => x.Verts()).Select(Key).Distinct().OrderBy(s => s).ToList();
            Assert.Equal(ringKeys.Concat(new[] { centreKey }).OrderBy(s => s).ToList(), downKeys);

            var walls = t.Where(Horizontal).ToList();
            Assert.Equal(8, walls.Count);
            foreach (var w in walls)
            {
                var n = w.Normal(); var c = w.Centroid();
                Assert.True(n[0] * (c[0] - Cx) + n[2] * (c[2] - Cz) > 0, "wall must face away from centre");
                var xz = w.Verts().Select(v => Math.Round(v[0], 3) + "," + Math.Round(v[2], 3)).Distinct().ToList();
                Assert.Equal(2, xz.Count); // lies along one ring edge
            }
            Assert.Equal(4, walls.SelectMany(w => w.Verts()).Select(v => Math.Round(v[0], 3) + "," + Math.Round(v[2], 3)).Distinct().Count());
        }

        [Fact]
        public void NonPositiveThicknessAddsTopOnly()
        {
            Assert.Equal(4, Build(Ring(), 4, 0).Count);
            Assert.Equal(4, Build(Ring(), 4, -1).Count);
        }

        [Fact]
        public void FewerThanThreeVerticesAddNothing()
        {
            Assert.Equal(0, Build(Ring(), 2, 0.5f).Count);
            Assert.Equal(0, Build(Ring(), 0, 0).Count);
        }

        [Fact]
        public void NullOrShortRingThrows()
        {
            Assert.ThrowsAny<ArgumentException>(() => Build(null, 4, 0));
            Assert.ThrowsAny<ArgumentException>(() => Build(new float[11], 4, 0));
        }
    }
}
