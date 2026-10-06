using Skylines.Core.Geometry;
using Xunit;

namespace Skylines.Core.Tests
{
    public class VerticalRayTests
    {
        private const ushort Solid = 1;

        // Two triangles covering [x0,x1] x [z0,z1] at height h; up-facing unless down is true.
        private static void Quad(TriangleBuffer t, float x0, float x1, float z0, float z1, float h, bool down, ushort flags)
        {
            if (!down)
            {
                t.Add(x0, h, z0, x0, h, z1, x1, h, z1, flags);
                t.Add(x0, h, z0, x1, h, z1, x1, h, z0, flags);
            }
            else
            {
                t.Add(x0, h, z0, x1, h, z1, x0, h, z1, flags);
                t.Add(x0, h, z0, x1, h, z0, x1, h, z1, flags);
            }
        }

        [Fact]
        public void HitFlatUpFacingTriangle()
        {
            var t = new TriangleBuffer();
            t.Add(0, 2, 0, 0, 2, 1, 1, 2, 1, 0);
            Assert.True(VerticalRay.Hit(t, 0, 0.25f, 0.75f, out float y, out float ny));
            Assert.Equal(2f, y, 4);
            Assert.Equal(1f, ny, 4);
        }

        [Fact]
        public void HitInterpolatesSlopeHeightAndNormal()
        {
            var t = new TriangleBuffer();
            t.Add(0, 0, 0, 0, 0, 1, 1, 1, 1, 0);
            Assert.True(VerticalRay.Hit(t, 0, 0.25f, 0.75f, out float y, out float ny));
            Assert.Equal(0.25f, y, 4);
            Assert.Equal(0.7071f, ny, 3);
        }

        [Fact]
        public void HitReversedWindingGivesNegativeNormal()
        {
            var t = new TriangleBuffer();
            t.Add(0, 0, 0, 1, 0, 1, 0, 0, 1, 0);
            Assert.True(VerticalRay.Hit(t, 0, 0.25f, 0.75f, out float y, out float ny));
            Assert.Equal(0f, y, 4);
            Assert.Equal(-1f, ny, 4);
        }

        [Fact]
        public void HitMissesOutsideProjection()
        {
            var t = new TriangleBuffer();
            t.Add(0, 0, 0, 0, 0, 1, 1, 0, 1, 0);
            Assert.False(VerticalRay.Hit(t, 0, 2f, 2f, out _, out _));
            Assert.False(VerticalRay.Hit(t, 0, 0.75f, 0.25f, out _, out _));
        }

        [Fact]
        public void HitVerticalTriangleIsFalse()
        {
            var t = new TriangleBuffer();
            t.Add(0, 0, 0, 1, 0, 0, 1, 1, 0, 0);
            Assert.False(VerticalRay.Hit(t, 0, 0.5f, 0f, out _, out _));
        }

        [Fact]
        public void HitCountsEdgesAndTolerance()
        {
            var t = new TriangleBuffer();
            t.Add(0, 0, 0, 0, 0, 1, 1, 0, 1, 0);
            Assert.True(VerticalRay.Hit(t, 0, 0f, 0.5f, out _, out _));
            Assert.True(VerticalRay.Hit(t, 0, 0.5f, 0.5f, out _, out _));
            Assert.True(VerticalRay.Hit(t, 0, -1e-6f, 0.5f, out _, out _));
            Assert.False(VerticalRay.Hit(t, 0, -1e-3f, 0.5f, out _, out _));
        }

        [Fact]
        public void WalkableFlatGround()
        {
            var t = new TriangleBuffer();
            Quad(t, -5, 5, -5, 5, 0, false, 0);
            Assert.True(VerticalRay.HighestWalkable(t, 1, 2, 10, 0.7f, Solid, out float y));
            Assert.Equal(0f, y, 4);
        }

        [Fact]
        public void WalkableEmptyBufferAndMissReturnFalse()
        {
            var t = new TriangleBuffer();
            Assert.False(VerticalRay.HighestWalkable(t, 0, 0, 10, 0.7f, Solid, out _));
            Quad(t, 0, 1, 0, 1, 0, false, 0);
            Assert.False(VerticalRay.HighestWalkable(t, 5, 5, 10, 0.7f, Solid, out _));
        }

        [Fact]
        public void WalkableSlopeAcceptedAboveMinNy()
        {
            var t = new TriangleBuffer();
            t.Add(0, 0, 0, 0, 0, 1, 1, 1, 1, 0);
            Assert.True(VerticalRay.HighestWalkable(t, 0.25f, 0.75f, 10, 0.7f, Solid, out float y));
            Assert.Equal(0.25f, y, 4);
        }

        [Fact]
        public void WalkableSlopeRejectedBelowMinNy()
        {
            var t = new TriangleBuffer();
            t.Add(0, 0, 0, 0, 0, 1, 1, 3, 1, 0);
            Assert.False(VerticalRay.HighestWalkable(t, 0.25f, 0.75f, 10, 0.7f, Solid, out _));
        }

        [Fact]
        public void WalkableRejectsDownFacingTriangle()
        {
            var t = new TriangleBuffer();
            Quad(t, -5, 5, -5, 5, 3, true, 0);
            Assert.False(VerticalRay.HighestWalkable(t, 0, 0, 10, 0.7f, Solid, out _));
        }

        [Fact]
        public void WalkableRoadSlabAboveGroundWins()
        {
            var t = new TriangleBuffer();
            Quad(t, -5, 5, -5, 5, 0, false, 0);
            Quad(t, -5, 5, -5, 5, 0.5f, false, 0);
            Assert.True(VerticalRay.HighestWalkable(t, 0, 0, 5, 0.7f, Solid, out float y));
            Assert.Equal(0.5f, y, 4);
        }

        [Fact]
        public void WalkableIgnoresHitsAboveFromYWhenOneBelowQualifies()
        {
            var t = new TriangleBuffer();
            Quad(t, -5, 5, -5, 5, 0, false, 0);
            Quad(t, -5, 5, -5, 5, 8, false, 0);
            Assert.True(VerticalRay.HighestWalkable(t, 0, 0, 5, 0.7f, Solid, out float y));
            Assert.Equal(0f, y, 4);
        }

        [Fact]
        public void WalkableFallsBackToLowestHitAboveFromY()
        {
            var t = new TriangleBuffer();
            Quad(t, -5, 5, -5, 5, 8, false, 0);
            Quad(t, -5, 5, -5, 5, 12, false, 0);
            Assert.True(VerticalRay.HighestWalkable(t, 0, 0, 5, 0.7f, Solid, out float y));
            Assert.Equal(8f, y, 4);
        }

        [Fact]
        public void WalkablePointOnSharedEdge()
        {
            var t = new TriangleBuffer();
            Quad(t, 0, 1, 0, 1, 2, false, 0);
            Assert.True(VerticalRay.HighestWalkable(t, 0.5f, 0.5f, 5, 0.7f, Solid, out float y));
            Assert.Equal(2f, y, 4);
        }

        // Canopy: solid slab, up-facing top at 10 and down-facing underside at 9.9, over ground at 0.
        private static TriangleBuffer Canopy(ushort flags)
        {
            var t = new TriangleBuffer();
            Quad(t, -5, 5, -5, 5, 0, false, 0);
            Quad(t, -2, 2, -2, 2, 10f, false, flags);
            Quad(t, -2, 2, -2, 2, 9.9f, true, flags);
            return t;
        }

        // Building: closed solid seen from inside, so only the up-facing roof at 10 matters.
        private static TriangleBuffer Building(ushort roofFlags)
        {
            var t = new TriangleBuffer();
            Quad(t, -5, 5, -5, 5, 0, false, 0);
            Quad(t, -2, 2, -2, 2, 10f, false, roofFlags);
            return t;
        }

        [Fact]
        public void GroundInsideBuildingIsRejectedUsesRoofViaFallback()
        {
            Assert.True(VerticalRay.HighestWalkable(Building(Solid), 0, 0, 5, 0.7f, Solid, out float y));
            Assert.Equal(10f, y, 4);
        }

        [Fact]
        public void ProbeAboveBuildingUsesRoofTop()
        {
            Assert.True(VerticalRay.HighestWalkable(Building(Solid), 0, 0, 60, 0.7f, Solid, out float y));
            Assert.Equal(10f, y, 4);
        }

        [Fact]
        public void GroundBesideBuildingStillWalkable()
        {
            Assert.True(VerticalRay.HighestWalkable(Building(Solid), 4, 4, 5, 0.7f, Solid, out float y));
            Assert.Equal(0f, y, 4);
        }

        [Fact]
        public void GroundUnderCanopyIsOutsideAndWalkable()
        {
            Assert.True(VerticalRay.HighestWalkable(Canopy(Solid), 0, 0, 5, 0.7f, Solid, out float y));
            Assert.Equal(0f, y, 4);
        }

        [Fact]
        public void ProbeAboveCanopyUsesTop()
        {
            Assert.True(VerticalRay.HighestWalkable(Canopy(Solid), 0, 0, 60, 0.7f, Solid, out float y));
            Assert.Equal(10f, y, 4);
        }

        [Fact]
        public void SolidWithOtherFlagsDoesNotMakePointInside()
        {
            Assert.True(VerticalRay.HighestWalkable(Building(2), 0, 0, 5, 0.7f, Solid, out float y));
            Assert.Equal(0f, y, 4);
        }

        [Fact]
        public void NonSolidUpFacingTriangleAboveDoesNotBlockGround()
        {
            var t = new TriangleBuffer();
            Quad(t, -5, 5, -5, 5, 0, false, 0);
            Quad(t, -5, 5, -5, 5, 5, false, 0);
            Assert.True(VerticalRay.HighestWalkable(t, 0, 0, 3, 0.7f, Solid, out float y));
            Assert.Equal(0f, y, 4);
        }
    }
}
