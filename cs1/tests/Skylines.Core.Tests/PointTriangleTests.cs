using System;
using Skylines.Core.Geometry;
using Xunit;

namespace Skylines.Core.Tests
{
    public class PointTriangleTests
    {
        // Triangle in the y = 0 plane: (0,0,0), (4,0,0), (0,0,4).
        private static float D(float x, float y, float z)
        {
            return (float)Math.Sqrt(PointTriangle.DistanceSquared(x, y, z, 0, 0, 0, 4, 0, 0, 0, 0, 4));
        }

        [Theory]
        [InlineData(1f, 2f, 1f, 2f)]      // above the face
        [InlineData(1f, -0.5f, 1f, 0.5f)] // below the face
        [InlineData(-3f, 0f, -4f, 5f)]    // nearest is corner a
        [InlineData(6f, 0f, 0f, 2f)]      // corner b
        [InlineData(0f, 0f, 7f, 3f)]      // corner c
        [InlineData(2f, 0f, -1f, 1f)]     // edge ab
        [InlineData(-1f, 0f, 2f, 1f)]     // edge ac
        [InlineData(3f, 0f, 3f, 1.41421356f)] // edge bc
        [InlineData(1f, 0f, 1f, 0f)]      // on the face
        public void Distance(float x, float y, float z, float expected)
        {
            Assert.Equal(expected, D(x, y, z), 4);
        }

        [Fact]
        public void NearestPicksTheClosestTriangleAndRespectsTheLimit()
        {
            float[] flat = { 0, 0, 0, 4, 0, 0, 0, 0, 4, 0, 5, 0, 4, 5, 0, 0, 5, 4 };
            Assert.Equal(0.5f, PointTriangle.Nearest(flat, 2, 1, 0.5f, 1, 1f), 4);
            Assert.Equal(float.PositiveInfinity, PointTriangle.Nearest(flat, 2, 1, 2.5f, 1, 1f));
            Assert.Equal(float.PositiveInfinity, PointTriangle.Nearest(flat, 0, 1, 0.5f, 1, 1f));
        }

        [Fact]
        public void IndexedMatchesFlat()
        {
            float[] verts = { 0, 0, 0, 4, 0, 0, 0, 0, 4, 4, 0, 4 };
            int[] idx = { 0, 1, 2, 1, 3, 2 };
            Assert.Equal(1f, PointTriangle.NearestIndexed(verts, idx, 3, 1, 3, 2f), 4);
            Assert.Equal(float.PositiveInfinity, PointTriangle.NearestIndexed(verts, idx, 3, 3, 3, 2f));
        }
    }
}
