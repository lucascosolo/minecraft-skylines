using System;
using System.Collections.Generic;
using System.Linq;
using Skylines.Core.Geometry;
using Xunit;
using static Skylines.Core.Tests.TriHelpers;

namespace Skylines.Core.Tests
{
    public class HeightfieldSkipCellTests
    {
        private static float Slope(float x, float z) { return 0.5f * x + 0.25f * z; }

        [Fact]
        public void SkippedCellAddsNoTriangles()
        {
            var b = new TriangleBuffer();
            Heightfield.Triangulate(Slope, (x, z) => Near(x, 1) && Near(z, 1), 0, 0, 4, 4, 2, 7, b);
            var tris = All(b);
            Assert.Equal(6, tris.Count);
            Assert.DoesNotContain(tris, t => { var c = t.Centroid(); return c[0] > 0 && c[0] < 2 && c[2] > 0 && c[2] < 2; });
        }

        [Fact]
        public void SkipCellSeesEachCellCentreOnce()
        {
            var seen = new List<string>();
            Heightfield.Triangulate(Slope, (x, z) => { seen.Add(x + "," + z); return false; }, 0, 0, 4, 4, 2, 7, new TriangleBuffer());
            Assert.Equal(new[] { "1,1", "1,3", "3,1", "3,3" }, seen.OrderBy(s => s).ToArray());
        }

        [Fact]
        public void NullSkipCellMatchesExistingOverload()
        {
            var a = new TriangleBuffer();
            var b = new TriangleBuffer();
            Heightfield.Triangulate(Slope, 0, 0, 4, 4, 2, 7, a);
            Heightfield.Triangulate(Slope, (Func<float, float, bool>)null, 0, 0, 4, 4, 2, 7, b);
            Assert.Equal(a.Count, b.Count);
            Assert.Equal(a.Positions.Take(9 * a.Count).ToArray(), b.Positions.Take(9 * b.Count).ToArray());
            Assert.Equal(a.Flags.Take(a.Count).ToArray(), b.Flags.Take(b.Count).ToArray());
        }
    }

    public class BezierCutTests
    {
        private static Bezier3D Curve()
        {
            return new Bezier3D
            {
                Ax = 0, Ay = 0, Az = 0,
                Bx = 10, By = 5, Bz = 2,
                Cx = 20, Cy = -3, Cz = 14,
                Dx = 30, Dy = 8, Dz = 6,
            };
        }

        private static float[] At(Bezier3D c, double t)
        {
            double u = 1 - t, a = u * u * u, b = 3 * u * u * t, d = 3 * u * t * t, e = t * t * t;
            return new[]
            {
                (float)(a * c.Ax + b * c.Bx + d * c.Cx + e * c.Dx),
                (float)(a * c.Ay + b * c.By + d * c.Cy + e * c.Dy),
                (float)(a * c.Az + b * c.Bz + d * c.Cz + e * c.Dz),
            };
        }

        private static void AssertPoint(float[] expected, float x, float y, float z)
        {
            Assert.True(Near(expected[0], x) && Near(expected[1], y) && Near(expected[2], z),
                "expected (" + expected[0] + "," + expected[1] + "," + expected[2] + ") got (" + x + "," + y + "," + z + ")");
        }

        [Fact]
        public void CutFullRangeEqualsOriginal()
        {
            var c = Curve();
            var r = c.Cut(0, 1);
            AssertPoint(new[] { c.Ax, c.Ay, c.Az }, r.Ax, r.Ay, r.Az);
            AssertPoint(new[] { c.Bx, c.By, c.Bz }, r.Bx, r.By, r.Bz);
            AssertPoint(new[] { c.Cx, c.Cy, c.Cz }, r.Cx, r.Cy, r.Cz);
            AssertPoint(new[] { c.Dx, c.Dy, c.Dz }, r.Dx, r.Dy, r.Dz);
        }

        [Fact]
        public void CutTracesSameCurveOverSubrange()
        {
            var c = Curve();
            var r = c.Cut(0.25f, 0.75f);
            AssertPoint(At(c, 0.25), r.Ax, r.Ay, r.Az);
            AssertPoint(At(c, 0.75), r.Dx, r.Dy, r.Dz);
            var mid = At(r, 0.5);
            AssertPoint(At(c, 0.5), mid[0], mid[1], mid[2]);
            var q = At(r, 0.25);
            AssertPoint(At(c, 0.375), q[0], q[1], q[2]);
        }
    }

    public class StripInsetTests
    {
        private static Bezier3D Straight(float x)
        {
            return new Bezier3D
            {
                Ax = x, Ay = 1, Az = 0,
                Bx = x, By = 1, Bz = 10,
                Cx = x, Cy = 1, Cz = 20,
                Dx = x, Dy = 1, Dz = 30,
            };
        }

        [Fact]
        public void InsetMovesEdgeTowardOtherAndLifts()
        {
            var edge = Straight(0);
            var other = Straight(8);
            Bezier3D inset;
            Assert.True(Strip.Inset(edge, other, 2, 0.5f, out inset));
            Assert.True(Near(inset.Ax, 2) && Near(inset.Bx, 2) && Near(inset.Cx, 2) && Near(inset.Dx, 2));
            Assert.True(Near(inset.Ay, 1.5f) && Near(inset.By, 1.5f) && Near(inset.Cy, 1.5f) && Near(inset.Dy, 1.5f));
            Assert.True(Near(inset.Az, 0) && Near(inset.Bz, 10) && Near(inset.Cz, 20) && Near(inset.Dz, 30));
        }

        [Theory]
        [InlineData(8f)]
        [InlineData(9f)]
        public void InsetReturnsFalseWhenDistanceReachesWidth(float distance)
        {
            Bezier3D inset;
            Assert.False(Strip.Inset(Straight(0), Straight(8), distance, 0.5f, out inset));
        }
    }

    public class BoxOrientedTests
    {
        private static TriangleBuffer Make(float angle)
        {
            var b = new TriangleBuffer();
            Box.Oriented(10, 5, angle, 4, 2, 0, 10, 9, b);
            return b;
        }

        private static float[] Range(IEnumerable<Tri> tris, int axis)
        {
            var v = tris.SelectMany(t => t.Verts()).Select(p => p[axis]).ToArray();
            return new[] { v.Min(), v.Max() };
        }

        [Fact]
        public void AxisAlignedBoxHasTwelveTrianglesAndExpectedExtent()
        {
            var b = Make(0);
            var tris = All(b);
            Assert.Equal(12, tris.Count);
            Assert.All(b.Flags.Take(b.Count), f => Assert.Equal(9, f));
            var x = Range(tris, 0); var y = Range(tris, 1); var z = Range(tris, 2);
            Assert.True(Near(x[0], 6) && Near(x[1], 14));
            Assert.True(Near(y[0], 0) && Near(y[1], 10));
            Assert.True(Near(z[0], 3) && Near(z[1], 7));
        }

        [Fact]
        public void TopAndBottomFacesAreTwoTrianglesEach()
        {
            var tris = All(Make(0));
            var up = tris.Where(t => Kind(t) == "up").ToList();
            var down = tris.Where(t => Kind(t) == "down").ToList();
            Assert.Equal(2, up.Count);
            Assert.Equal(2, down.Count);
            Assert.All(up, t => Assert.All(t.Verts(), p => Assert.True(Near(p[1], 10))));
            Assert.All(down, t => Assert.All(t.Verts(), p => Assert.True(Near(p[1], 0))));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(0.6f)]
        public void AllNormalsPointOutwardFromCentre(float angle)
        {
            var tris = All(Make(angle));
            Assert.Equal(12, tris.Count);
            foreach (var t in tris)
            {
                var n = t.Normal(); var c = t.Centroid();
                float dot = n[0] * (c[0] - 10) + n[1] * (c[1] - 5) + n[2] * (c[2] - 5);
                Assert.True(dot > 0, "normal points inward");
            }
        }

        [Fact]
        public void QuarterTurnSwapsHorizontalExtent()
        {
            var tris = All(Make((float)(Math.PI / 2)));
            var x = Range(tris, 0); var z = Range(tris, 2);
            Assert.True(Near(x[0], 8) && Near(x[1], 12));
            Assert.True(Near(z[0], 1) && Near(z[1], 9));
        }

        [Fact]
        public void UpFaceAreaIsFourHalfXHalfZ()
        {
            foreach (float angle in new[] { 0f, 0.6f })
            {
                double area = 0;
                foreach (var t in All(Make(angle)).Where(t => Kind(t) == "up"))
                {
                    var n = t.Normal();
                    area += 0.5 * Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
                }
                Assert.True(Math.Abs(area - 32) < 1e-3, "area " + area);
            }
        }
    }
}
