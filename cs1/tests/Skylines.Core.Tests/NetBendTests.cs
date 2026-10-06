using Skylines.Core.Geometry;
using Xunit;

namespace Skylines.Core.Tests
{
    public class NetBendTests
    {
        private const float Sx = 0.5f / 8f, Sz = 1f / 64f;

        // Edge along +z at the given x with control points at thirds and end height yEnd.
        private static Bezier3D Edge(float x, float yEnd)
        {
            return new Bezier3D
            {
                Ax = x, Ay = 0, Az = 0,
                Bx = x, By = yEnd / 3f, Bz = 64f / 3f,
                Cx = x, Cy = yEnd * 2f / 3f, Cz = 128f / 3f,
                Dx = x, Dy = yEnd, Dz = 64f,
            };
        }

        private static void Check(Bezier3D l, Bezier3D r, float sx, float sz, float x, float y, float z, float ex, float ey, float ez)
        {
            float wx, wy, wz;
            NetBend.Point(l, r, sx, sz, x, y, z, out wx, out wy, out wz);
            Assert.Equal(ex, wx, 3);
            Assert.Equal(ey, wy, 3);
            Assert.Equal(ez, wz, 3);
        }

        [Fact]
        public void StraightEdgesMapCornersAndCentre()
        {
            Bezier3D l = Edge(0, 0), r = Edge(16, 0);
            Check(l, r, Sx, Sz, -8, 0, -32, 0, 0, 0);
            Check(l, r, Sx, Sz, 8, 2, 32, 16, 2, 64);
            Check(l, r, Sx, Sz, 0, 5, 0, 8, 5, 32);
        }

        [Fact]
        public void CurvedEdgesUseBezierValueAtHalf()
        {
            // Value at t=0.5 is (A+3B+3C+D)/8: x = 30/8 = 3.75, z = 244/8 = 30.5 (right edge shifted +16 in x).
            Bezier3D l = new Bezier3D { Ax = 0, Az = 0, Bx = 0, Bz = 20, Cx = 10, Cz = 40, Dx = 0, Dz = 64 };
            Bezier3D r = new Bezier3D { Ax = 16, Az = 0, Bx = 16, Bz = 20, Cx = 26, Cz = 40, Dx = 16, Dz = 64 };
            Check(l, r, Sx, Sz, -8, 3, 0, 3.75f, 3, 30.5f);
            Check(l, r, Sx, Sz, 0, 3, 0, 11.75f, 3, 30.5f);
            Check(l, r, Sx, Sz, 8, 3, 0, 19.75f, 3, 30.5f);
        }

        [Fact]
        public void NegatedScalesMapLocalOriginCornerToFarEnd()
        {
            Bezier3D l = Edge(0, 0), r = Edge(16, 0);
            Check(l, r, -Sx, -Sz, -8, 0, -32, 16, 0, 64);
            Check(l, r, -Sx, -Sz, 8, 0, 32, 0, 0, 0);
        }

        [Fact]
        public void SlopedEdgesInterpolateYAndAddLocalYUnrotated()
        {
            Bezier3D l = Edge(0, -8), r = Edge(16, -8);
            Check(l, r, Sx, Sz, 0, 1.5f, 0, 8, -2.5f, 32);
            Check(l, r, Sx, Sz, 8, 0, 32, 16, -8, 64);
        }

        [Fact]
        public void TrianglesAppendsBentVerticesInOrderWithFlags()
        {
            Bezier3D l = Edge(0, 0), r = Edge(16, 0);
            float[] pos = { -8, 0, -32, 8, 0, -32, 0, 2, 32 };
            var buf = new TriangleBuffer();
            int n = NetBend.Triangles(pos, new[] { 0, 1, 2 }, l, r, Sx, Sz, -100f, 7, buf);
            Assert.Equal(1, n);
            Assert.Equal(1, buf.Count);
            Assert.Equal((ushort)7, buf.Flags[0]);
            float[] expect = { 0, 0, 0, 16, 0, 0, 8, 2, 64 };
            for (int i = 0; i < 9; i++) Assert.Equal(expect[i], buf.Positions[i], 3);
        }

        [Fact]
        public void TrianglesFiltersOnMaxLocalYAgainstMinY()
        {
            Bezier3D l = Edge(0, 0), r = Edge(16, 0);
            float[] pos =
            {
                -8, 0.4f, -32, 8, 0, -32, 0, 0.1f, 32, // max 0.4: skipped
                -8, 0, -32, 8, 0.5f, -32, 0, 0, 32,    // max 0.5: kept
            };
            var buf = new TriangleBuffer();
            int n = NetBend.Triangles(pos, new[] { 0, 1, 2, 3, 4, 5 }, l, r, Sx, Sz, 0.5f, 3, buf);
            Assert.Equal(1, n);
            Assert.Equal(1, buf.Count);
            Assert.Equal(0.5f, buf.Positions[4], 3); // second vertex of the kept triangle
        }

        [Fact]
        public void TrianglesAppendsToExistingBuffer()
        {
            Bezier3D l = Edge(0, 0), r = Edge(16, 0);
            var buf = new TriangleBuffer();
            buf.Add(0, 0, 0, 1, 0, 0, 0, 1, 0, 1);
            float[] pos = { -8, 0, -32, 8, 0, -32, 0, 0, 32 };
            Assert.Equal(1, NetBend.Triangles(pos, new[] { 0, 1, 2 }, l, r, Sx, Sz, -1f, 2, buf));
            Assert.Equal(2, buf.Count);
            Assert.Equal((ushort)1, buf.Flags[0]);
            Assert.Equal((ushort)2, buf.Flags[1]);
        }
    }
}
