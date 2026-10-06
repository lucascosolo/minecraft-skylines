using System;
using System.Collections.Generic;
using System.Linq;
using Skylines.Core.Voxels;
using Xunit;

namespace Skylines.Core.Tests
{
    public class VoxelEditSetTests
    {
        private static List<VoxelEdit> Edits(int n, Func<int, string> state)
        {
            var l = new List<VoxelEdit>();
            for (int i = 0; i < n; i++) l.Add(new VoxelEdit(new VoxelPos(i, 0, 0), state(i)));
            return l;
        }

        [Fact]
        public void PosComparesByXThenZThenY()
        {
            Assert.True(new VoxelPos(0, 9, 9).CompareTo(new VoxelPos(1, 0, 0)) < 0);
            Assert.True(new VoxelPos(1, 9, 0).CompareTo(new VoxelPos(1, 0, 1)) < 0);
            Assert.True(new VoxelPos(1, 0, 1).CompareTo(new VoxelPos(1, 1, 1)) < 0);
            Assert.Equal(0, new VoxelPos(1, 2, 3).CompareTo(new VoxelPos(1, 2, 3)));
            Assert.True(new VoxelPos(-5, 0, 0).CompareTo(new VoxelPos(-4, 0, 0)) < 0);
        }

        [Fact]
        public void PosEquality()
        {
            Assert.True(new VoxelPos(1, 2, 3).Equals(new VoxelPos(1, 2, 3)));
            Assert.False(new VoxelPos(1, 2, 3).Equals(new VoxelPos(1, 3, 2)));
            Assert.Equal(new VoxelPos(1, 2, 3).GetHashCode(), new VoxelPos(1, 2, 3).GetHashCode());
            Assert.Equal(1, new VoxelPos(1, 2, 3).X);
            Assert.Equal(2, new VoxelPos(1, 2, 3).Y);
            Assert.Equal(3, new VoxelPos(1, 2, 3).Z);
        }

        [Fact]
        public void Constants()
        {
            Assert.Equal("minecraft:air", VoxelEditSet.Air);
            Assert.Equal(65536, VoxelEditSet.MaxBatchEdits);
        }

        [Fact]
        public void SetNullStateThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new VoxelEditSet().Set(0, 0, 0, null));
        }

        [Fact]
        public void SetAndGetAndOverwrite()
        {
            var s = new VoxelEditSet();
            s.Set(1, 2, 3, "minecraft:stone");
            string v;
            Assert.True(s.TryGet(1, 2, 3, out v));
            Assert.Equal("minecraft:stone", v);
            s.Set(1, 2, 3, "minecraft:dirt");
            Assert.Equal(1, s.Count);
            Assert.True(s.TryGet(1, 2, 3, out v));
            Assert.Equal("minecraft:dirt", v);
            Assert.False(s.TryGet(3, 2, 1, out v));
        }

        [Fact]
        public void AirRemovesPosition()
        {
            var s = new VoxelEditSet();
            s.Set(1, 2, 3, "minecraft:stone");
            s.Set(1, 2, 3, VoxelEditSet.Air);
            string v;
            Assert.Equal(0, s.Count);
            Assert.False(s.TryGet(1, 2, 3, out v));
        }

        [Fact]
        public void AirOnEmptyPositionStoresNothing()
        {
            var s = new VoxelEditSet();
            s.Set(5, 5, 5, VoxelEditSet.Air);
            Assert.Equal(0, s.Count);
            Assert.Empty(s.Sorted());
        }

        [Fact]
        public void ClearEmpties()
        {
            var s = new VoxelEditSet();
            s.Set(0, 0, 0, "a:b");
            s.Set(1, 0, 0, "a:b");
            s.Clear();
            Assert.Equal(0, s.Count);
            Assert.Empty(s.Sorted());
        }

        [Fact]
        public void SortedOrdersByXZY()
        {
            var s = new VoxelEditSet();
            s.Set(1, 0, 0, "a:a");
            s.Set(0, 5, 1, "a:b");
            s.Set(0, 9, 0, "a:c");
            s.Set(0, 1, 0, "a:d");
            s.Set(-2, 0, 7, "a:e");
            var p = s.Sorted().Select(e => e.Pos).ToList();
            Assert.Equal(new[]
            {
                new VoxelPos(-2, 0, 7), new VoxelPos(0, 1, 0), new VoxelPos(0, 9, 0),
                new VoxelPos(0, 5, 1), new VoxelPos(1, 0, 0)
            }, p);
            Assert.Equal("a:d", s.Sorted()[1].State);
        }

        [Fact]
        public void BatchesEmptyInputGivesNone()
        {
            Assert.Empty(VoxelEditSet.Batches(new List<VoxelEdit>(), 10));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(65537)]
        public void BatchesRejectsBadMax(int max)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => VoxelEditSet.Batches(new List<VoxelEdit>(), max));
        }

        [Fact]
        public void BatchesSplitFiveBy2KeepOrderAndRestartPalette()
        {
            var states = new[] { "a:x", "a:y", "a:y", "a:z", "a:x" };
            var edits = Edits(5, i => states[i]);
            var b = VoxelEditSet.Batches(edits, 2);
            Assert.Equal(3, b.Count);
            Assert.Equal(new[] { 2, 2, 1 }, b.Select(x => x.Positions.Count).ToArray());
            Assert.Equal(new[] { "a:x", "a:y" }, b[0].Palette);
            Assert.Equal(new ushort[] { 0, 1 }, b[0].States);
            Assert.Equal(new[] { "a:y", "a:z" }, b[1].Palette);
            Assert.Equal(new ushort[] { 0, 1 }, b[1].States);
            Assert.Equal(new[] { "a:x" }, b[2].Palette);
            Assert.Equal(new ushort[] { 0 }, b[2].States);
            Assert.Equal(edits.Select(e => e.Pos), b.SelectMany(x => x.Positions));
        }

        [Fact]
        public void BatchPaletteIsFirstUseOrderWithoutDuplicates()
        {
            var states = new[] { "a:c", "a:a", "a:c", "a:b", "a:a" };
            var b = VoxelEditSet.Batches(Edits(5, i => states[i]), 100);
            Assert.Single(b);
            Assert.Equal(new[] { "a:c", "a:a", "a:b" }, b[0].Palette);
            Assert.Equal(new ushort[] { 0, 1, 0, 2, 1 }, b[0].States);
        }

        [Fact]
        public void BatchesDoNotSortInput()
        {
            var edits = new List<VoxelEdit>
            {
                new VoxelEdit(new VoxelPos(9, 0, 0), "a:a"),
                new VoxelEdit(new VoxelPos(1, 0, 0), "a:a"),
            };
            var b = VoxelEditSet.Batches(edits, 10);
            Assert.Equal(9, b[0].Positions[0].X);
            Assert.Equal(1, b[0].Positions[1].X);
        }

        [Fact]
        public void BatchesExactlyFullThenOverflow()
        {
            var b = VoxelEditSet.Batches(Edits(4, i => "a:a"), 2);
            Assert.Equal(2, b.Count);
            Assert.All(b, x => Assert.Equal(2, x.Positions.Count));
        }

        [Fact]
        public void BatchesAt65536Boundary()
        {
            var one = VoxelEditSet.Batches(Edits(65536, i => "a:a"), 65536);
            Assert.Single(one);
            Assert.Equal(65536, one[0].Positions.Count);
            var two = VoxelEditSet.Batches(Edits(65537, i => "a:a"), 65536);
            Assert.Equal(2, two.Count);
            Assert.Equal(65536, two[0].Positions.Count);
            Assert.Single(two[1].Positions);
        }

        [Fact]
        public void BatchSplitsWhenPaletteWouldExceed65535()
        {
            var b = VoxelEditSet.Batches(Edits(65536, i => "a:s" + i), 65536);
            Assert.True(b.Count >= 2);
            foreach (var x in b)
            {
                Assert.True(x.Palette.Count <= 65535);
                Assert.True(x.Positions.Count <= 65536);
                Assert.Equal(x.Positions.Count, x.States.Count);
                Assert.Equal(x.Palette.Count, x.Palette.Distinct().Count());
            }
            Assert.Equal(65536, b.Sum(x => x.Positions.Count));
            Assert.Equal(65535, b[0].Palette.Count);
        }
    }
}
