using Skylines.Core.Geometry;
using Xunit;

namespace Skylines.Core.Tests
{
    public class VehicleProfileTests
    {
        // Quad in the plane x 0..1 spanning z0..z1, y rising from y0 (at z0) to y1 (at z1).
        private static void Quad(float z0, float z1, float y0, float y1, out float[] xyz, out int[] idx)
        {
            xyz = new float[] { 0, y0, z0, 1, y0, z0, 1, y1, z1, 0, y1, z1 };
            idx = new[] { 0, 1, 2, 0, 2, 3 };
        }

        private static void AssertHeights(float[] expected, float[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], actual[i], 4);
        }

        [Theory]
        [InlineData(4.0f, 16)]
        [InlineData(4.01f, 17)]
        [InlineData(0.1f, 1)]
        [InlineData(0.0f, 1)]
        [InlineData(-3.0f, 1)]
        [InlineData(1000.0f, 128)]
        public void SliceCountCeilsAndClamps(float length, int expected)
        {
            Assert.Equal(expected, VehicleProfile.SliceCount(length));
        }

        [Fact]
        public void Constants()
        {
            Assert.Equal(0.25f, VehicleProfile.SliceLength);
            Assert.Equal(128, VehicleProfile.MaxSlices);
        }

        [Fact]
        public void FlatQuadGivesEverySliceItsHeight()
        {
            float[] xyz; int[] idx;
            Quad(0, 1, 1.5f, 1.5f, out xyz, out idx);
            AssertHeights(new[] { 1.5f, 1.5f, 1.5f, 1.5f }, VehicleProfile.Heights(xyz, idx, 0, 1, 0, 4));
        }

        [Fact]
        public void MinYIsSubtractedAndResultClampedAtZero()
        {
            float[] xyz; int[] idx;
            Quad(0, 1, 1.5f, 1.5f, out xyz, out idx);
            AssertHeights(new[] { 0.5f, 0.5f }, VehicleProfile.Heights(xyz, idx, 0, 1, 1.0f, 2));
            AssertHeights(new[] { 0f, 0f }, VehicleProfile.Heights(xyz, idx, 0, 1, 2.0f, 2));
        }

        [Fact]
        public void RampGivesEachSliceTheHeightAtItsHigherBoundary()
        {
            float[] xyz; int[] idx;
            Quad(0, 1, 0, 1, out xyz, out idx); // y == z
            AssertHeights(new[] { 0.25f, 0.5f, 0.75f, 1.0f }, VehicleProfile.Heights(xyz, idx, 0, 1, 0, 4));
        }

        [Fact]
        public void LongTriangleCoversEverySliceItSpans()
        {
            float[] xyz = { 0, 2, 0, 1, 2, 0, 0, 2, 8 };
            AssertHeights(new[] { 2f, 2f, 2f, 2f, 2f, 2f, 2f, 2f }, VehicleProfile.Heights(xyz, new[] { 0, 1, 2 }, 0, 8, 0, 8));
        }

        [Fact]
        public void GapLeavesEmptyMiddleSlicesZero()
        {
            float[] a, b; int[] ia, ib;
            Quad(0, 1, 2, 2, out a, out ia);
            Quad(3, 4, 2, 2, out b, out ib);
            var xyz = new float[a.Length + b.Length];
            a.CopyTo(xyz, 0); b.CopyTo(xyz, a.Length);
            var idx = new int[ia.Length + ib.Length];
            ia.CopyTo(idx, 0);
            for (int i = 0; i < ib.Length; i++) idx[ia.Length + i] = ib[i] + 4;
            // 8 slices of 0.5 m; slices touching z=1 or z=3 count (closed boundaries).
            AssertHeights(new[] { 2f, 2f, 2f, 0f, 0f, 2f, 2f, 2f }, VehicleProfile.Heights(xyz, idx, 0, 4, 0, 8));
        }

        [Fact]
        public void VerticalTriangleAtOneZTouchesSlicesOnBothSidesOfThatBoundary()
        {
            float[] xyz = { 0, 0, 0.5f, 1, 0, 0.5f, 0, 3, 0.5f };
            AssertHeights(new[] { 0f, 3f, 3f, 0f }, VehicleProfile.Heights(xyz, new[] { 0, 1, 2 }, 0, 1, 0, 4));
        }

        [Fact]
        public void NoTrianglesGivesZeros()
        {
            AssertHeights(new[] { 0f, 0f, 0f }, VehicleProfile.Heights(new float[0], new int[0], 0, 1, 0, 3));
        }

        [Fact]
        public void QuantizeScalesAndRoundsUp()
        {
            byte[] q = VehicleProfile.Quantize(new[] { 0f, 0.5f, 1f, 2f, 0.000001f }, 1f);
            Assert.Equal(new byte[] { 0, 128, 255, 255, 1 }, q);
        }

        [Fact]
        public void QuantizeExactMultipleDoesNotRoundUp()
        {
            Assert.Equal(new byte[] { 51 }, VehicleProfile.Quantize(new[] { 0.2f }, 1f));
        }

        [Fact]
        public void QuantizeNonPositiveFullHeightGivesZeros()
        {
            Assert.Equal(new byte[] { 0, 0 }, VehicleProfile.Quantize(new[] { 1f, 2f }, 0f));
            Assert.Equal(new byte[] { 0 }, VehicleProfile.Quantize(new[] { 1f }, -1f));
        }
    }
}
