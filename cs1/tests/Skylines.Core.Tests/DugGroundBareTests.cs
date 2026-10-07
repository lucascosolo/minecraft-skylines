using System.Collections.Generic;
using System.Linq;
using Skylines.Core.Voxels;
using Xunit;

namespace Skylines.Core.Tests
{
    /// <summary>Bare columns: the grass plant cell (top + 1) was broken, the column is not open.</summary>
    public class DugGroundBareTests
    {
        // Surface 10.3 gives solid top 9, so the plant cell is y = 10.
        private static DugGround Flat() { return new DugGround((x, z) => 10.3f); }

        private static List<KeyValuePair<int, int>> Bare(DugGround g, int a, int b, int c, int d)
        {
            return g.BareColumns(a, b, c, d);
        }

        private static int[] Flatten(List<KeyValuePair<int, int>> l)
        {
            return l.SelectMany(p => new[] { p.Key, p.Value }).ToArray();
        }

        [Fact]
        public void EmptiedEditAtPlantCellMakesColumnBare()
        {
            DugGround g = Flat();
            g.Set(4, 10, 6, true, true);
            Assert.True(g.IsBare(4, 6));
            Assert.False(g.IsBare(5, 6));
        }

        [Fact]
        public void NegativeCoordinatesWork()
        {
            DugGround g = Flat();
            g.Set(-3, 10, -5, true, true);
            Assert.True(g.IsBare(-3, -5));
            Assert.Equal(new[] { -3, -5 }, Flatten(Bare(g, -10, -10, 0, 0)));
        }

        [Fact]
        public void FourArgSetBehavesAsNotEmptied()
        {
            DugGround g = Flat();
            g.Set(1, 10, 1, true);
            Assert.False(g.IsBare(1, 1));
            Assert.Empty(Bare(g, -5, -5, 5, 5));
        }

        [Fact]
        public void RegrownGrassClearsBare()
        {
            DugGround g = Flat();
            g.Set(2, 10, 2, true, true);
            g.Set(2, 10, 2, true, false);
            Assert.False(g.IsBare(2, 2));
        }

        [Fact]
        public void UneditingPlantCellClearsBare()
        {
            DugGround g = Flat();
            g.Set(2, 10, 2, true, true);
            g.Set(2, 10, 2, false, false);
            Assert.False(g.IsBare(2, 2));
        }

        [Fact]
        public void EmptiedEditAboveThePlantCellIsNotBare()
        {
            DugGround g = Flat();
            g.Set(0, 11, 0, true, true);
            Assert.False(g.IsBare(0, 0));
            Assert.Equal(0, g.DugCount);
        }

        [Fact]
        public void EmptiedEditBelowTopIsDugButNotBare()
        {
            DugGround g = Flat();
            g.Set(0, 8, 0, true, true);
            Assert.False(g.IsBare(0, 0));
            Assert.Equal(1, g.DugCount);
            Assert.True(g.IsDug(0, 8, 0));
            Assert.False(g.IsOpen(0, 0));
        }

        [Fact]
        public void EmptiedEditAtTopMakesColumnOpenNotBare()
        {
            DugGround g = Flat();
            g.Set(0, 9, 0, true, true);
            Assert.True(g.IsOpen(0, 0));
            Assert.False(g.IsBare(0, 0));
        }

        [Fact]
        public void DiggingTopCellOfBareColumnMakesItNotBare()
        {
            DugGround g = Flat();
            g.Set(3, 10, 3, true, true);
            Assert.True(g.IsBare(3, 3));
            g.Set(3, 9, 3, true);
            Assert.True(g.IsOpen(3, 3));
            Assert.False(g.IsBare(3, 3));
            Assert.Empty(Bare(g, 0, 0, 10, 10));
        }

        [Fact]
        public void DugCountIgnoresEmptiedEditsAboveTop()
        {
            DugGround g = Flat();
            g.Set(0, 10, 0, true, true);
            g.Set(1, 9, 1, true, true);
            Assert.Equal(1, g.DugCount);
        }

        [Fact]
        public void BareColumnsFiltersByHalfOpenRangeAndSortsByXThenZ()
        {
            DugGround g = Flat();
            g.Set(5, 10, 1, true, true);
            g.Set(-2, 10, 7, true, true);
            g.Set(5, 10, -4, true, true);
            g.Set(-2, 10, -1, true, true);
            g.Set(9, 10, 9, true, true);

            Assert.Equal(new[] { -2, -1, -2, 7, 5, -4, 5, 1, 9, 9 }, Flatten(Bare(g, -10, -10, 10, 10)));
            // max bounds are exclusive, min bounds inclusive.
            Assert.Equal(new[] { -2, -1, 5, -4 }, Flatten(Bare(g, -2, -4, 9, 1)));
        }

        [Fact]
        public void ClearForgetsBareState()
        {
            DugGround g = Flat();
            g.Set(1, 10, 1, true, true);
            g.Clear();
            Assert.False(g.IsBare(1, 1));
            Assert.Empty(Bare(g, -5, -5, 5, 5));
        }

        [Fact]
        public void ForgetSurfaceReevaluatesPlantCellAgainstNewTop()
        {
            float surface = 10.3f;
            var g = new DugGround((x, z) => surface);
            g.Set(-1, 10, -1, true, true);
            Assert.True(g.IsBare(-1, -1));

            surface = 12.3f; // top 11, plant cell now 12
            g.ForgetSurface();
            Assert.False(g.IsBare(-1, -1));
            Assert.Equal(1, g.DugCount); // y = 10 is now below the top
        }
    }
}
