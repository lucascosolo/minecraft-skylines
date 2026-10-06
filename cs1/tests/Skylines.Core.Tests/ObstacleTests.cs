using System;
using Skylines.Core.Geometry;
using Xunit;

namespace Skylines.Core.Tests
{
    public class ObstacleTests
    {
        private const ushort F = 7;
        private const float Eps = 1e-4f;

        private static float[] Extents(TriangleBuffer b, int from = 0)
        {
            var e = new[] { float.MaxValue, float.MaxValue, float.MaxValue, float.MinValue, float.MinValue, float.MinValue };
            for (int i = from * 3; i < b.Count * 3; i++)
                for (int k = 0; k < 3; k++)
                {
                    float v = b.Positions[i * 3 + k];
                    e[k] = Math.Min(e[k], v);
                    e[3 + k] = Math.Max(e[3 + k], v);
                }
            return e;
        }

        private static void AssertBox(TriangleBuffer b, float x0, float x1, float y0, float y1, float z0, float z1, int from = 0)
        {
            var e = Extents(b, from);
            Assert.Equal(x0, e[0], 4); Assert.Equal(x1, e[3], 4);
            Assert.Equal(y0, e[1], 4); Assert.Equal(y1, e[4], 4);
            Assert.Equal(z0, e[2], 4); Assert.Equal(z1, e[5], 4);
        }

        private static void AssertFlags(TriangleBuffer b, int from = 0)
        {
            for (int i = from; i < b.Count; i++) Assert.Equal(F, b.Flags[i]);
        }

        [Fact]
        public void TallTreeIsATrunk()
        {
            var b = new TriangleBuffer();
            int n = Obstacle.Tree(10, 2, 20, 4, 10, 4, 1, F, b);
            Assert.Equal(12, n); Assert.Equal(12, b.Count);
            AssertBox(b, 9.7f, 10.3f, 1.5f, 6f, 19.7f, 20.3f);
            AssertFlags(b);
        }

        [Fact]
        public void BushUsesScaledFootprint()
        {
            var b = new TriangleBuffer();
            int n = Obstacle.Tree(0, 1, 0, 2, 1.5f, 4, 1.2f, F, b);
            Assert.Equal(12, n); Assert.Equal(12, b.Count);
            AssertBox(b, -1.2f, 1.2f, 0.5f, 2.8f, -2.4f, 2.4f);
            AssertFlags(b);
        }

        [Fact]
        public void HeightExactlyTallHeightIsABush()
        {
            var b = new TriangleBuffer();
            Assert.Equal(12, Obstacle.Tree(0, 0, 0, 3, 2.5f, 3, 1, F, b));
            AssertBox(b, -1.5f, 1.5f, -0.5f, 2.5f, -1.5f, 1.5f);
        }

        [Fact]
        public void ZeroHeightTreeAddsNothing()
        {
            var b = new TriangleBuffer();
            Assert.Equal(0, Obstacle.Tree(0, 0, 0, 2, 0, 2, 1, F, b));
            Assert.Equal(0, b.Count);
        }

        [Fact]
        public void TreeAppendsAfterExistingTriangles()
        {
            var b = new TriangleBuffer();
            b.Add(1, 2, 3, 4, 5, 6, 7, 8, 9, 99);
            Obstacle.Tree(0, 0, 0, 1, 1, 1, 1, F, b);
            Assert.Equal(13, b.Count);
            Assert.Equal(99, b.Flags[0]);
            Assert.Equal(1f, b.Positions[0]); Assert.Equal(9f, b.Positions[8]);
            AssertFlags(b, 1);
        }

        [Fact]
        public void BenchIsAxisAlignedBox()
        {
            var b = new TriangleBuffer();
            int n = Obstacle.Prop(5, 1, 5, 0, 0, 0.25f, 0, 2, 0.5f, 0.6f, 1, F, b);
            Assert.Equal(12, n); Assert.Equal(12, b.Count);
            AssertBox(b, 4, 6, 0.5f, 1.5f, 4.7f, 5.3f);
            AssertFlags(b);
        }

        [Fact]
        public void RotatedPropMovesCentreByLocalOffset()
        {
            var b = new TriangleBuffer();
            // angle pi/2: local x -> world +z; local centre (1,0) -> (x, z+1). Box 2 along local x, 1 along local z.
            Assert.Equal(12, Obstacle.Prop(10, 0, 20, (float)(Math.PI / 2), 1, 0.5f, 0, 2, 1, 1, 1, F, b));
            AssertBox(b, 9.5f, 10.5f, -0.5f, 1f, 20f, 22f);
        }

        [Fact]
        public void StreetLightBecomesPostAtPivot()
        {
            var b = new TriangleBuffer();
            Assert.Equal(12, Obstacle.Prop(3, 1, 4, 0, 0, 4, 2.5f, 1, 8, 6, 1, F, b));
            AssertBox(b, 2.7f, 3.3f, 0.5f, 9f, 3.7f, 4.3f);
            AssertFlags(b);
        }

        [Fact]
        public void TallCentredPropKeepsFullBox()
        {
            var b = new TriangleBuffer();
            Assert.Equal(12, Obstacle.Prop(0, 0, 0, 0, 0, 1.5f, 0, 4, 3, 2, 1, F, b));
            AssertBox(b, -2, 2, -0.5f, 3, -1, 1);
        }

        [Fact]
        public void FlatPropIsSkipped()
        {
            var b = new TriangleBuffer();
            Assert.Equal(0, Obstacle.Prop(0, 0, 0, 0, 0, 0.025f, 0, 3, 0.05f, 3, 1, F, b));
            Assert.Equal(0, b.Count);
        }

        [Fact]
        public void RaisedMeshKeepsItsBottom()
        {
            var b = new TriangleBuffer();
            Assert.Equal(12, Obstacle.Prop(0, 0, 0, 0, 0, 3.5f, 0, 1, 1, 1, 1, F, b));
            AssertBox(b, -0.5f, 0.5f, 3f, 4f, -0.5f, 0.5f);
        }

        [Fact]
        public void PropAppendsAfterExistingTriangles()
        {
            var b = new TriangleBuffer();
            b.Add(1, 2, 3, 4, 5, 6, 7, 8, 9, 99);
            Assert.Equal(12, Obstacle.Prop(0, 0, 0, 0, 0, 0.5f, 0, 1, 1, 1, 1, F, b));
            Assert.Equal(13, b.Count);
            Assert.Equal(99, b.Flags[0]);
            Assert.Equal(1f, b.Positions[0]);
            AssertFlags(b, 1);
        }

        [Theory]
        [InlineData(0.05f)]
        [InlineData(0.5f)]
        [InlineData(1.5f)]
        public void KeepPassesInRange(float raw) { Assert.Equal(raw, PavementStep.Keep(raw)); }

        [Theory]
        [InlineData(0.049f)]
        [InlineData(1.51f)]
        [InlineData(0f)]
        [InlineData(-0.3f)]
        [InlineData(float.NaN)]
        public void KeepDropsOutOfRange(float raw) { Assert.Equal(0f, PavementStep.Keep(raw)); }

        [Fact]
        public void VerdictExplainsEachOutcome()
        {
            Assert.Contains("kept", PavementStep.Verdict(0.15f));
            Assert.Contains("no pedestrian lane", PavementStep.Verdict(float.NaN));
            Assert.Contains("below", PavementStep.Verdict(0.01f));
            Assert.Contains("below", PavementStep.Verdict(-1f));
            Assert.Contains("above", PavementStep.Verdict(2f));
            Assert.False(string.IsNullOrEmpty(PavementStep.Verdict(0.5f)));
        }
    }
}
