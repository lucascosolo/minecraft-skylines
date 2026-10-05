using System;
using System.Collections.Generic;
using System.Linq;
using Skylines.Core.Geometry;
using Xunit;
using static Skylines.Core.Tests.TriHelpers;

namespace Skylines.Core.Tests
{
    public class TriangleBufferTests
    {
        [Fact]
        public void AddStoresPositionsAndFlags()
        {
            var b = new TriangleBuffer();
            b.Add(1, 2, 3, 4, 5, 6, 7, 8, 9, 42);
            Assert.Equal(1, b.Count);
            Assert.Equal(new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, b.Positions.Take(9).ToArray());
            Assert.Equal(42, b.Flags[0]);
        }

        [Fact]
        public void GrowsPastAnyInitialCapacity()
        {
            var b = new TriangleBuffer();
            for (int i = 0; i < 1000; i++) b.Add(i, 0, 0, i, 1, 0, i, 0, 1, (ushort)i);
            Assert.Equal(1000, b.Count);
            Assert.True(b.Positions.Length >= 9000);
            Assert.True(b.Flags.Length >= 1000);
            Assert.Equal(999f, b.Positions[999 * 9]);
            Assert.Equal(999, b.Flags[999]);
        }

        [Fact]
        public void ClearResetsCountAndAllowsReuse()
        {
            var b = new TriangleBuffer();
            b.Add(1, 1, 1, 2, 2, 2, 3, 3, 3, 1);
            b.Clear();
            Assert.Equal(0, b.Count);
            b.Add(9, 8, 7, 6, 5, 4, 3, 2, 1, 2);
            Assert.Equal(1, b.Count);
            Assert.Equal(9f, b.Positions[0]);
            Assert.Equal(2, b.Flags[0]);
        }

        [Fact]
        public void TruncateKeepsTheFirstTrianglesAndRejectsOutOfRange()
        {
            var b = new TriangleBuffer();
            b.Add(1, 1, 1, 2, 2, 2, 3, 3, 3, 1);
            b.Add(4, 4, 4, 5, 5, 5, 6, 6, 6, 2);
            b.Truncate(1);
            Assert.Equal(1, b.Count);
            Assert.Equal(1, b.Flags[0]);
            b.Add(7, 7, 7, 8, 8, 8, 9, 9, 9, 3);
            Assert.Equal(7f, b.Positions[9]);
            Assert.Throws<ArgumentOutOfRangeException>(() => b.Truncate(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => b.Truncate(-1));
        }

        [Fact]
        public void ReverseWindingSwapsBAndCOfEveryTriangle()
        {
            var b = new TriangleBuffer();
            b.Add(1, 2, 3, 4, 5, 6, 7, 8, 9, 5);
            b.Add(0, 0, 0, 1, 0, 0, 0, 0, 1, 6);
            b.ReverseWinding();
            Assert.Equal(new float[] { 1, 2, 3, 7, 8, 9, 4, 5, 6 }, b.Positions.Take(9).ToArray());
            Assert.Equal(new float[] { 0, 0, 0, 0, 0, 1, 1, 0, 0 }, b.Positions.Skip(9).Take(9).ToArray());
            Assert.Equal(new ushort[] { 5, 6 }, b.Flags.Take(2).ToArray());
        }
    }

    public class HeightfieldTests
    {
        static float Wavy(float x, float z) { return 0.5f * x + 0.25f * z + (float)Math.Sin(x * 0.3) * 2f; }

        [Fact]
        public void SixteenBySixteenAtStepTwoGivesOneHundredTwentyEightTriangles()
        {
            var b = new TriangleBuffer();
            Heightfield.Triangulate(Wavy, 0, 0, 16, 16, 2, 0, b);
            Assert.Equal(128, b.Count);
        }

        [Fact]
        public void FlagsAreAppliedToEveryTriangle()
        {
            var b = new TriangleBuffer();
            Heightfield.Triangulate(Wavy, 0, 0, 4, 4, 2, 7, b);
            Assert.Equal(8, b.Count);
            Assert.All(b.Flags.Take(8), f => Assert.Equal(7, f));
        }

        [Fact]
        public void EveryVertexHeightMatchesSamplerAtItsXZ()
        {
            var b = new TriangleBuffer();
            Heightfield.Triangulate(Wavy, -4, 2, 12, 10, 2, 0, b);
            Assert.True(b.Count > 0);
            foreach (var t in All(b))
                foreach (var v in t.Verts())
                    Assert.True(Near(v[1], Wavy(v[0], v[2])), $"y at ({v[0]},{v[2]})");
        }

        [Fact]
        public void SamplerCalledExactlyOncePerGridVertex()
        {
            var calls = new Dictionary<string, int>();
            Func<float, float, float> f = (x, z) =>
            {
                string k = x + "," + z;
                calls[k] = calls.ContainsKey(k) ? calls[k] + 1 : 1;
                return 0;
            };
            Heightfield.Triangulate(f, 0, 0, 8, 6, 2, 0, new TriangleBuffer());
            Assert.Equal(5 * 4, calls.Count);
            Assert.All(calls.Values, c => Assert.Equal(1, c));
        }

        [Fact]
        public void AllNormalsPointUp()
        {
            var b = new TriangleBuffer();
            Heightfield.Triangulate(Wavy, 0, 0, 16, 16, 2, 0, b);
            Assert.Equal(128, b.Count);
            Assert.All(All(b), t => Assert.True(t.Normal()[1] > 0));
        }

        [Fact]
        public void DiagonalRunsFromLowCornerToHighCornerOfEveryCell()
        {
            var b = new TriangleBuffer();
            Heightfield.Triangulate((x, z) => 0, 0, 0, 4, 4, 2, 0, b);
            Assert.Equal(8, b.Count);
            for (int cx = 0; cx < 2; cx++)
                for (int cz = 0; cz < 2; cz++)
                {
                    float x0 = cx * 2, z0 = cz * 2, x1 = x0 + 2, z1 = z0 + 2;
                    var cell = All(b).Where(t => Math.Abs(t.Centroid()[0] - (x0 + 1)) < 1 && Math.Abs(t.Centroid()[2] - (z0 + 1)) < 1).ToList();
                    Assert.Equal(2, cell.Count);
                    Assert.All(cell, t =>
                    {
                        Assert.Contains(t.Verts(), v => v[0] == x0 && v[2] == z0);
                        Assert.Contains(t.Verts(), v => v[0] == x1 && v[2] == z1);
                    });
                }
        }

        [Fact]
        public void SharedEdgesLeaveNoGaps()
        {
            var b = new TriangleBuffer();
            Heightfield.Triangulate(Wavy, 0, 0, 8, 6, 2, 0, b);
            Assert.Equal(24, b.Count);
            Func<float[], string> key = v => v[0] + "," + v[1] + "," + v[2];
            var edges = new Dictionary<string, int>();
            var onBoundary = new Dictionary<string, bool>();
            foreach (var t in All(b))
            {
                var vs = new[] { t.A, t.B, t.C };
                for (int i = 0; i < 3; i++)
                {
                    var p = vs[i]; var q = vs[(i + 1) % 3];
                    string k = string.CompareOrdinal(key(p), key(q)) < 0 ? key(p) + "|" + key(q) : key(q) + "|" + key(p);
                    edges[k] = edges.ContainsKey(k) ? edges[k] + 1 : 1;
                    onBoundary[k] = (p[0] == q[0] && (p[0] == 0 || p[0] == 8)) || (p[2] == q[2] && (p[2] == 0 || p[2] == 6));
                }
            }
            foreach (var kv in edges)
                Assert.Equal(onBoundary[kv.Key] ? 1 : 2, kv.Value);
        }

        [Fact]
        public void LastColumnAndRowAreClampedToMaxWhenExtentIsNotDivisible()
        {
            var b = new TriangleBuffer();
            Heightfield.Triangulate((x, z) => 0, 0, 0, 5, 3, 2, 0, b);
            Assert.Equal(3 * 2 * 2, b.Count);
            var xs = All(b).SelectMany(t => t.Verts()).Select(v => v[0]).Distinct().OrderBy(v => v).ToArray();
            var zs = All(b).SelectMany(t => t.Verts()).Select(v => v[2]).Distinct().OrderBy(v => v).ToArray();
            Assert.Equal(new float[] { 0, 2, 4, 5 }, xs);
            Assert.Equal(new float[] { 0, 2, 3 }, zs);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        public void NonPositiveStepThrows(float step)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Heightfield.Triangulate((x, z) => 0, 0, 0, 4, 4, step, 0, new TriangleBuffer()));
        }

        [Fact]
        public void MaxBelowMinThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Heightfield.Triangulate((x, z) => 0, 4, 0, 0, 4, 2, 0, new TriangleBuffer()));
            Assert.Throws<ArgumentOutOfRangeException>(() => Heightfield.Triangulate((x, z) => 0, 0, 4, 4, 0, 2, 0, new TriangleBuffer()));
        }
    }

    public class StripTests
    {
        // Straight line along +X from x=0 to x=10 at y=1, control points evenly spaced so x = 10t.
        static Bezier3D Line()
        {
            return new Bezier3D { Ax = 0, Ay = 1, Az = 0, Bx = 10f / 3, By = 1, Bz = 0, Cx = 20f / 3, Cy = 1, Cz = 0, Dx = 10, Dy = 1, Dz = 0 };
        }

        [Fact]
        public void StraightLineTopSurfaceHasExactEdgePointsAndCounts()
        {
            var b = new TriangleBuffer();
            Strip.Road(Line(), 2, 3, 0, 9, b);
            Assert.Equal(8, b.Count); // n = ceil(10/3) = 4, two triangles per interval
            Assert.All(b.Flags.Take(8), f => Assert.Equal(9, f));
            var t = All(b);
            Assert.All(t, x => Assert.True(x.Normal()[1] > 0));
            var verts = t.SelectMany(x => x.Verts()).ToList();
            Assert.All(verts, v => Assert.True(Near(v[1], 1)));
            var pts = verts.Select(v => Math.Round(v[0], 3) + "," + Math.Round(v[2], 3)).Distinct().OrderBy(s => s).ToList();
            var expected = new List<string>();
            foreach (var x in new[] { 0.0, 2.5, 5.0, 7.5, 10.0 })
                foreach (var z in new[] { -2.0, 2.0 }) expected.Add(x + "," + z);
            Assert.Equal(expected.OrderBy(s => s).ToList(), pts);
        }

        [Fact]
        public void StraightLineWithThicknessAddsBottomAndTwoOutwardWalls()
        {
            var b = new TriangleBuffer();
            Strip.Road(Line(), 2, 3, 0.5f, 0, b);
            Assert.Equal(32, b.Count);
            var t = All(b);
            var up = t.Where(x => Kind(x) == "up").ToList();
            var down = t.Where(x => Kind(x) == "down").ToList();
            var side = t.Where(x => Kind(x) == "side").ToList();
            Assert.Equal(8, up.Count);
            Assert.Equal(8, down.Count);
            Assert.Equal(16, side.Count);
            Assert.All(up, x => Assert.All(x.Verts(), v => Assert.True(Near(v[1], 1))));
            Assert.All(down, x => Assert.All(x.Verts(), v => Assert.True(Near(v[1], 0.5f))));
            // Left wall (+Z side) faces +Z, right wall faces -Z: away from the centre line.
            Assert.All(side, x =>
            {
                float z = x.A[2];
                Assert.All(x.Verts(), v => Assert.True(Near(v[2], z)));
                Assert.True(Near(Math.Abs(z), 2));
                Assert.True(x.Normal()[2] * z > 0, "wall normal must point away from centre line");
            });
            Assert.Equal(8, side.Count(x => x.A[2] > 0));
        }

        [Fact]
        public void ZeroThicknessProducesOnlyTheTopSurface()
        {
            var b = new TriangleBuffer();
            Strip.Road(Line(), 2, 3, 0, 0, b);
            Assert.Equal(8, b.Count);
            Assert.All(All(b), x => Assert.Equal("up", Kind(x)));
        }

        [Fact]
        public void CurveHasUpNormalsAndEdgePointsExactlyHalfWidthFromCurvePoints()
        {
            var c = new Bezier3D { Ax = 0, Ay = 0, Az = 0, Bx = 10, By = 2, Bz = 0, Cx = 10, Cy = 4, Cz = 10, Dx = 20, Dy = 5, Dz = 10 };
            const float hw = 1.5f;
            Func<double, double[]> pt = t =>
            {
                double u = 1 - t;
                Func<float, float, float, float, double> f = (a, bb, cc, d) => u * u * u * a + 3 * u * u * t * bb + 3 * u * t * t * cc + t * t * t * d;
                return new[] { f(c.Ax, c.Bx, c.Cx, c.Dx), f(c.Ay, c.By, c.Cy, c.Dy), f(c.Az, c.Bz, c.Cz, c.Dz) };
            };
            Func<double, double[]> tan = t =>
            {
                double u = 1 - t;
                Func<float, float, float, float, double> f = (a, bb, cc, d) => 3 * (u * u * (bb - a) + 2 * u * t * (cc - bb) + t * t * (d - cc));
                return new[] { f(c.Ax, c.Bx, c.Cx, c.Dx), 0, f(c.Az, c.Bz, c.Cz, c.Dz) };
            };
            // polyline length over 16 segments, as the contract defines
            double len = 0; var prev = pt(0);
            for (int i = 1; i <= 16; i++) { var p = pt(i / 16.0); len += Math.Sqrt(Math.Pow(p[0] - prev[0], 2) + Math.Pow(p[1] - prev[1], 2) + Math.Pow(p[2] - prev[2], 2)); prev = p; }
            const float step = 2f;
            int n = Math.Max(1, (int)Math.Ceiling(len / step));

            var b = new TriangleBuffer();
            Strip.Road(c, hw, step, 0, 0, b);
            Assert.Equal(2 * n, b.Count);
            var tris = All(b);
            Assert.All(tris, x => Assert.True(x.Normal()[1] > 0));
            foreach (var v in tris.SelectMany(x => x.Verts()))
            {
                bool ok = false;
                for (int k = 0; k <= n && !ok; k++)
                {
                    double t = (double)k / n; var p = pt(t); var tg = tan(t);
                    double dx = v[0] - p[0], dz = v[2] - p[2];
                    double dist = Math.Sqrt(dx * dx + dz * dz);
                    double tl = Math.Sqrt(tg[0] * tg[0] + tg[2] * tg[2]);
                    double along = (dx * tg[0] + dz * tg[2]) / tl;
                    ok = Math.Abs(dist - hw) < 1e-3 && Math.Abs(v[1] - p[1]) < 1e-3 && Math.Abs(along) < 1e-3;
                }
                Assert.True(ok, $"vertex ({v[0]},{v[1]},{v[2]}) is not a perpendicular edge point");
            }
        }

        [Fact]
        public void DegenerateCurveProducesNoTriangles()
        {
            var c = new Bezier3D { Ax = 3, Ay = 1, Az = 3, Bx = 3, By = 1, Bz = 3, Cx = 3, Cy = 1, Cz = 3, Dx = 3, Dy = 1, Dz = 3 };
            var b = new TriangleBuffer();
            Strip.Road(c, 2, 1, 0, 0, b);
            Strip.Road(c, 2, 1, 1, 0, b);
            Assert.Equal(0, b.Count);
        }
    }

    public class DiscTests
    {
        const float Cx = 10, Y = 3, Cz = -4, R = 5;

        [Fact]
        public void TopFanHasOneUpTrianglePerSegmentOnARegularPolygon()
        {
            var b = new TriangleBuffer();
            Disc.Fan(Cx, Y, Cz, R, 8, 0, 3, b);
            Assert.Equal(8, b.Count);
            Assert.All(b.Flags.Take(8), f => Assert.Equal(3, f));
            var t = All(b);
            Assert.All(t, x => Assert.True(x.Normal()[1] > 0));
            var verts = t.SelectMany(x => x.Verts()).ToList();
            Assert.All(verts, v => Assert.True(Near(v[1], Y)));
            var rim = verts.Where(v => Math.Abs(v[0] - Cx) > 1e-3 || Math.Abs(v[2] - Cz) > 1e-3)
                .Select(v => Math.Round(v[0], 3) + "," + Math.Round(v[2], 3)).Distinct().ToList();
            Assert.Equal(8, rim.Count);
            Assert.All(verts, v =>
            {
                double d = Math.Sqrt((v[0] - Cx) * (v[0] - Cx) + (v[2] - Cz) * (v[2] - Cz));
                Assert.True(d < 1e-3 || Math.Abs(d - R) < 1e-3);
            });
        }

        [Fact]
        public void ThicknessAddsDownBottomAndOutwardWalls()
        {
            var b = new TriangleBuffer();
            Disc.Fan(Cx, Y, Cz, R, 8, 2, 0, b);
            Assert.Equal(32, b.Count);
            var t = All(b);
            var up = t.Where(x => Kind(x) == "up").ToList();
            var down = t.Where(x => Kind(x) == "down").ToList();
            var side = t.Where(x => Kind(x) == "side").ToList();
            Assert.Equal(8, up.Count);
            Assert.Equal(8, down.Count);
            Assert.Equal(16, side.Count);
            Assert.All(down, x => Assert.All(x.Verts(), v => Assert.True(Near(v[1], Y - 2))));
            Assert.All(side, x =>
            {
                var n = x.Normal(); var c = x.Centroid();
                Assert.True(n[0] * (c[0] - Cx) + n[2] * (c[2] - Cz) > 0, "wall must face away from centre");
            });
        }
    }
}
