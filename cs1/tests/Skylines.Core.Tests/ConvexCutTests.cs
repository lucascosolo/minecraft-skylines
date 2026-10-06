using System;
using Skylines.Core.Geometry;
using Xunit;

namespace Skylines.Core.Tests
{
    public class ConvexCutTests
    {
        private const ushort F = 1;

        // A 10 x 10 m square at x, z in [0, 10] on the plane y = 100 + 0.1 x + 0.2 z, as two triangles.
        private static TriangleBuffer Square()
        {
            var b = new TriangleBuffer();
            b.Add(0, Y(0, 0), 0, 0, Y(0, 10), 10, 10, Y(10, 10), 10, F);
            b.Add(0, Y(0, 0), 0, 10, Y(10, 10), 10, 10, Y(10, 0), 0, F);
            return b;
        }

        private static float Y(float x, float z) { return 100f + 0.1f * x + 0.2f * z; }

        private static double XzArea(TriangleBuffer b)
        {
            double sum = 0;
            for (int t = 0; t < b.Count; t++)
            {
                float[] p = b.Positions;
                int o = t * 9;
                sum += Math.Abs((p[o + 3] - p[o]) * (p[o + 8] - p[o + 2]) - (p[o + 6] - p[o]) * (p[o + 5] - p[o + 2])) / 2.0;
            }
            return sum;
        }

        [Fact]
        public void NoAreaKeepsTrianglesUnchanged()
        {
            var src = Square();
            var dst = new TriangleBuffer();
            Assert.Equal(2, new ConvexCut().Apply(src, 0, src.Count, dst));
            Assert.Equal(src.Positions[0], dst.Positions[0]);
            Assert.Equal(100.0, XzArea(dst), 3);
        }

        [Fact]
        public void CentralSquareIsRemovedExactly()
        {
            var cut = new ConvexCut();
            cut.Add(new float[] { 3, 3, 7, 3, 7, 7, 3, 7 }, 4);
            var src = Square();
            var dst = new TriangleBuffer();
            cut.Apply(src, 0, src.Count, dst);
            Assert.Equal(100.0 - 16.0, XzArea(dst), 3);
            for (int t = 0; t < dst.Count; t++)
            {
                int o = t * 9;
                float[] p = dst.Positions;
                float cx = (p[o] + p[o + 3] + p[o + 6]) / 3, cz = (p[o + 2] + p[o + 5] + p[o + 8]) / 3;
                Assert.False(cx > 3.001f && cx < 6.999f && cz > 3.001f && cz < 6.999f, "piece centroid inside the cut");
                for (int v = 0; v < 3; v++)
                    Assert.Equal(Y(p[o + 3 * v], p[o + 3 * v + 2]), p[o + 3 * v + 1], 3); // heights stay on the plane
                Assert.Equal(F, dst.Flags[t]);
            }
        }

        [Fact]
        public void WindingOfTheAreaDoesNotMatter()
        {
            var cw = new ConvexCut();
            cw.Add(new float[] { 3, 3, 3, 7, 7, 7, 7, 3 }, 4);
            var src = Square();
            var dst = new TriangleBuffer();
            cw.Apply(src, 0, src.Count, dst);
            Assert.Equal(84.0, XzArea(dst), 3);
        }

        [Fact]
        public void AreaCoveringEverythingLeavesNothing()
        {
            var cut = new ConvexCut();
            cut.Add(new float[] { -1, -1, 11, -1, 11, 11, -1, 11 }, 4);
            var src = Square();
            var dst = new TriangleBuffer();
            Assert.Equal(0, cut.Apply(src, 0, src.Count, dst));
        }

        [Fact]
        public void AdjacentTrianglesCutLikeTheirUnion()
        {
            // A road quad split into two triangles along its diagonal: no sliver survives along the diagonal.
            var cut = new ConvexCut();
            cut.AddTriangle(2, -1, 8, -1, 8, 11);
            cut.AddTriangle(2, -1, 8, 11, 2, 11);
            var src = Square();
            var dst = new TriangleBuffer();
            cut.Apply(src, 0, src.Count, dst);
            Assert.Equal(40.0, XzArea(dst), 2);
        }

        [Fact]
        public void PartialRangeOnly()
        {
            var cut = new ConvexCut();
            cut.Add(new float[] { -1, -1, 11, -1, 11, 11, -1, 11 }, 4);
            var src = Square();
            src.Add(20, 0, 20, 20, 0, 21, 21, 0, 21, F); // outside every area
            var dst = new TriangleBuffer();
            Assert.Equal(1, cut.Apply(src, 2, 3, dst));
        }

        [Fact]
        public void DegenerateAreasAreIgnored()
        {
            var cut = new ConvexCut();
            cut.Add(new float[] { 0, 0, 1, 1, 2, 2 }, 3);
            cut.Add(new float[] { 0, 0, 1, 1 }, 2);
            Assert.Equal(0, cut.Count);
        }
    }
}
