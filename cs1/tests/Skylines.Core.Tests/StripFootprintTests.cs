using Skylines.Core.Geometry;
using Xunit;

namespace Skylines.Core.Tests
{
    public class StripFootprintTests
    {
        private static Bezier3D Line(float x0, float z0, float x1, float z1)
        {
            return new Bezier3D
            {
                Ax = x0, Az = z0,
                Bx = x0 + (x1 - x0) / 3f, Bz = z0 + (z1 - z0) / 3f,
                Cx = x0 + 2f * (x1 - x0) / 3f, Cz = z0 + 2f * (z1 - z0) / 3f,
                Dx = x1, Dz = z1,
            };
        }

        // Quarter circle about the origin from (r,0) to (0,r).
        private static Bezier3D Arc(float r)
        {
            float k = 0.5522847f * r;
            return new Bezier3D { Ax = r, Az = 0, Bx = r, Bz = k, Cx = k, Cz = r, Dx = 0, Dz = r };
        }

        private static StripFootprint Straight(float margin, bool swap = false)
        {
            var f = new StripFootprint(margin);
            var l = Line(0, 0, 0, 20);
            var r = Line(4, 0, 4, 20);
            if (swap) f.Add(r, l, 4); else f.Add(l, r, 4);
            return f;
        }

        [Fact]
        public void StraightStripContainsCentre()
        {
            Assert.True(Straight(2).Contains(2, 10));
        }

        [Fact]
        public void StraightStripMarginSides()
        {
            var f = Straight(2);
            Assert.True(f.Contains(5.5f, 10));
            Assert.True(f.Contains(-1.5f, 10));
            Assert.False(f.Contains(6.5f, 10));
            Assert.False(f.Contains(-2.5f, 10));
        }

        [Fact]
        public void StraightStripEnds()
        {
            var f = Straight(2);
            Assert.True(f.Contains(2, 21.5f));
            Assert.True(f.Contains(2, -1.5f));
            Assert.False(f.Contains(2, 22.5f));
            Assert.False(f.Contains(2, -2.5f));
        }

        [Fact]
        public void ZeroMarginIsExactArea()
        {
            var f = Straight(0);
            Assert.True(f.Contains(3.9f, 10));
            Assert.False(f.Contains(4.5f, 10));
        }

        [Fact]
        public void CurvedStripFollowsBend()
        {
            var f = new StripFootprint(2);
            f.Add(Arc(8), Arc(12), 16);
            Assert.True(f.Contains(7.0710678f, 7.0710678f));
            Assert.True(f.Contains(10, 0.1f));
            Assert.False(f.Contains(0, 0));
            Assert.False(f.Contains(15, 15));
        }

        [Fact]
        public void CountIsSamplesPerAddAndAccumulates()
        {
            var f = new StripFootprint(1);
            Assert.Equal(0, f.Count);
            f.Add(Line(0, 0, 0, 20), Line(4, 0, 4, 20), 8);
            Assert.Equal(8, f.Count);
            f.Add(Line(10, 0, 10, 20), Line(14, 0, 14, 20), 5);
            Assert.Equal(13, f.Count);
            Assert.True(f.Contains(12, 10));
        }

        [Fact]
        public void ClearEmptiesFootprint()
        {
            var f = Straight(2);
            f.Clear();
            Assert.Equal(0, f.Count);
            Assert.False(f.Contains(2, 10));
        }

        [Fact]
        public void EmptyFootprintContainsNothing()
        {
            Assert.False(new StripFootprint(5).Contains(0, 0));
        }

        [Fact]
        public void WindingIndependent()
        {
            var a = Straight(2);
            var b = Straight(2, true);
            float[][] pts = { new[] { 2f, 10f }, new[] { 5.5f, 10f }, new[] { 6.5f, 10f }, new[] { -1.5f, 10f }, new[] { 2f, 22.5f }, new[] { 2f, 21.5f } };
            foreach (var p in pts)
                Assert.Equal(a.Contains(p[0], p[1]), b.Contains(p[0], p[1]));
            Assert.True(b.Contains(2, 10));
            Assert.False(b.Contains(6.5f, 10));
        }
    }
}
