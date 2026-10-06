using System;
using Skylines.Core.Geometry;
using Xunit;

namespace Skylines.Core.Tests
{
    public class HeightfieldHeightAtTests
    {
        private static float H(float x, float z) { return x * x + 3f * z; }

        // Interpolates y at (x, z) on the first triangle of the buffer whose xz footprint contains the point.
        private static float Expected(TriangleBuffer b, float x, float z)
        {
            foreach (Tri t in TriHelpers.All(b))
            {
                double d = (t.B[2] - t.C[2]) * (t.A[0] - t.C[0]) + (t.C[0] - t.B[0]) * (t.A[2] - t.C[2]);
                double l1 = ((t.B[2] - t.C[2]) * (x - t.C[0]) + (t.C[0] - t.B[0]) * (z - t.C[2])) / d;
                double l2 = ((t.C[2] - t.A[2]) * (x - t.C[0]) + (t.A[0] - t.C[0]) * (z - t.C[2])) / d;
                double l3 = 1 - l1 - l2;
                const double e = -1e-6;
                if (l1 >= e && l2 >= e && l3 >= e) return (float)(l1 * t.A[1] + l2 * t.B[1] + l3 * t.C[1]);
            }
            throw new InvalidOperationException("point not covered");
        }

        private static TriangleBuffer Build(float step)
        {
            var b = new TriangleBuffer();
            Heightfield.Triangulate(H, -4, -4, 4, 4, step, 1, b);
            return b;
        }

        [Fact]
        public void MatchesTriangulatedSurfaceOnDenseSamplePoints()
        {
            TriangleBuffer b = Build(2f);
            for (float x = -4f; x <= 4f; x += 0.375f)
                for (float z = -4f; z <= 4f; z += 0.5f)
                    Assert.True(Math.Abs(Heightfield.HeightAt(H, x, z, 2f) - Expected(b, x, z)) < 1e-4f, "at " + x + "," + z);
        }

        [Fact]
        public void BothSidesOfTheDiagonalOfOneCell()
        {
            TriangleBuffer b = Build(2f);
            foreach (float[] p in new[] { new[] { -1.5f, -0.5f }, new[] { -0.5f, -1.5f }, new[] { -1.9f, -0.2f }, new[] { -0.2f, -1.9f } })
                Assert.True(Math.Abs(Heightfield.HeightAt(H, p[0], p[1], 2f) - Expected(b, p[0], p[1])) < 1e-4f);
        }

        [Fact]
        public void GridVerticesReturnSampledHeight()
        {
            for (float x = -4f; x <= 4f; x += 2f)
                for (float z = -4f; z <= 4f; z += 2f)
                    Assert.True(Math.Abs(Heightfield.HeightAt(H, x, z, 2f) - H(x, z)) < 1e-4f, "at " + x + "," + z);
        }

        [Fact]
        public void NegativeCoordinatesUseTheCellBelow()
        {
            TriangleBuffer b = Build(1f);
            Assert.True(Math.Abs(Heightfield.HeightAt(H, -3.25f, -0.75f, 1f) - Expected(b, -3.25f, -0.75f)) < 1e-4f);
            Assert.True(Math.Abs(Heightfield.HeightAt(H, -0.25f, -3.75f, 1f) - Expected(b, -0.25f, -3.75f)) < 1e-4f);
        }
    }
}
