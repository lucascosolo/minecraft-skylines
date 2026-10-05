using System;
using Xunit;
using MinecraftSkylines.Mod.SelfTest;

namespace MinecraftSkylines.Mod.Tests
{
    public class BlockRowPlanTests
    {
        [Fact]
        public void BlocksListIsFixed()
        {
            Assert.Equal(new[]
            {
                "minecraft:stone", "minecraft:grass_block", "minecraft:oak_planks", "minecraft:glass",
                "minecraft:oak_leaves[persistent=true]", "minecraft:water"
            }, BlockRowPlan.Blocks);
        }

        [Theory]
        [InlineData(0.0, 0.0)]
        [InlineData(44.0, 0.0)]
        [InlineData(46.0, 90.0)]
        [InlineData(90.0, 90.0)]
        [InlineData(180.0, 180.0)]
        [InlineData(271.0, 270.0)]
        [InlineData(359.0, 0.0)]
        [InlineData(-90.0, 270.0)]
        [InlineData(-10.0, 0.0)]
        [InlineData(450.0, 90.0)]
        [InlineData(720.0, 0.0)]
        public void SnapYawWrapsToNearestQuarterTurn(double input, double expected)
        {
            Assert.Equal(expected, BlockRowPlan.SnapYaw(input));
        }

        [Theory]
        [InlineData(0.0, 0, -1)]
        [InlineData(90.0, 1, 0)]
        [InlineData(180.0, 0, 1)]
        [InlineData(270.0, -1, 0)]
        [InlineData(271.0, -1, 0)]
        [InlineData(-90.0, -1, 0)]
        public void ForwardFollowsSnappedYaw(double yaw, int fx, int fz)
        {
            BlockRowPlan p = BlockRowPlan.Create(10.5, 64.0, -3.5, yaw, 3);
            Assert.Equal(fx, p.ForwardX);
            Assert.Equal(fz, p.ForwardZ);
        }

        [Fact]
        public void SpecExampleYaw90()
        {
            BlockRowPlan p = BlockRowPlan.Create(10.5, 64.0, -3.5, 90.0, 3);
            Assert.Equal(10, p.FeetBlockX); Assert.Equal(64, p.FeetBlockY); Assert.Equal(-4, p.FeetBlockZ);
            Assert.Equal(new[] { 13, 13, 13, 13, 13, 13 }, p.BlockX);
            Assert.Equal(new[] { -6, -5, -4, -3, -2, -1 }, p.BlockZ);
            string[] cmds = p.BuildCommands();
            Assert.Equal(6, cmds.Length);
            Assert.Equal("fill 13 64 -6 13 65 -6 minecraft:stone", cmds[0]);
            Assert.Equal("fill 13 64 -5 13 65 -5 minecraft:grass_block", cmds[1]);
            Assert.Equal("fill 13 64 -4 13 65 -4 minecraft:oak_planks", cmds[2]);
            Assert.Equal("fill 13 64 -3 13 65 -3 minecraft:glass", cmds[3]);
            Assert.Equal("fill 13 64 -2 13 65 -2 minecraft:oak_leaves[persistent=true]", cmds[4]);
            Assert.Equal("fill 13 64 -1 13 65 -1 minecraft:water", cmds[5]);
            Assert.Equal("fill 13 64 -6 13 65 -1 minecraft:air", p.ClearCommand());
            Assert.Equal(2.2, p.GapToRow(10.5, -3.5), 9);
            Assert.Equal(2.2, p.GapToRow(10.5, 100.0), 9);
            Assert.Equal(0.0, p.GapToRow(12.7, -3.5), 9);
            Assert.True(p.GapToRow(12.9, -3.5) < 0);
        }

        [Fact]
        public void Yaw0FacesMinusZ()
        {
            BlockRowPlan p = BlockRowPlan.Create(10.5, 64.0, -3.5, 0.0, 3);
            Assert.Equal(new[] { 8, 9, 10, 11, 12, 13 }, p.BlockX);
            Assert.Equal(new[] { -7, -7, -7, -7, -7, -7 }, p.BlockZ);
            Assert.Equal("fill 8 64 -7 8 65 -7 minecraft:stone", p.BuildCommands()[0]);
            Assert.Equal("fill 8 64 -7 13 65 -7 minecraft:air", p.ClearCommand());
            // near face of centre block z=-7 facing -z is its far side z+1 = -6
            Assert.Equal(2.2, p.GapToRow(10.5, -3.5), 9);
            Assert.Equal(0.0, p.GapToRow(10.5, -5.7), 9);
        }

        [Fact]
        public void Yaw180FacesPlusZ()
        {
            BlockRowPlan p = BlockRowPlan.Create(10.5, 64.0, -3.5, 180.0, 3);
            Assert.Equal(new[] { 12, 11, 10, 9, 8, 7 }, p.BlockX);
            Assert.Equal(new[] { -1, -1, -1, -1, -1, -1 }, p.BlockZ);
            Assert.Equal("fill 7 64 -1 12 65 -1 minecraft:air", p.ClearCommand());
            Assert.Equal(2.2, p.GapToRow(10.5, -3.5), 9);
        }

        [Fact]
        public void Yaw271SnapsTo270FacesMinusX()
        {
            BlockRowPlan p = BlockRowPlan.Create(10.5, 64.0, -3.5, 271.0, 3);
            Assert.Equal(new[] { 7, 7, 7, 7, 7, 7 }, p.BlockX);
            Assert.Equal(new[] { -2, -3, -4, -5, -6, -7 }, p.BlockZ);
            Assert.Equal("fill 7 64 -7 7 65 -2 minecraft:air", p.ClearCommand());
            Assert.Equal(2.2, p.GapToRow(10.5, 0.0), 9);
            Assert.Equal(0.0, p.GapToRow(8.3, 0.0), 9);
        }

        [Fact]
        public void FeetBlockYUsesSmallEpsilon()
        {
            Assert.Equal(64, BlockRowPlan.Create(0.5, 63.9995, 0.5, 0.0, 2).FeetBlockY);
            Assert.Equal(63, BlockRowPlan.Create(0.5, 63.99, 0.5, 0.0, 2).FeetBlockY);
        }

        [Fact]
        public void CommandsUseInvariantIntegers()
        {
            BlockRowPlan p = BlockRowPlan.Create(-100.5, -5.0, -3.5, 90.0, 3);
            foreach (string c in p.BuildCommands())
            {
                Assert.StartsWith("fill ", c);
                Assert.DoesNotContain("−", c);
                Assert.DoesNotContain(",", c);
            }
            Assert.Equal("fill -98 -5 -6 -98 -4 -6 minecraft:stone", p.BuildCommands()[0]);
        }

        [Fact]
        public void CoversIsTrueOnlyForSectionsHoldingRowBlocks()
        {
            BlockRowPlan p = BlockRowPlan.Create(10.5, 64.0, -3.5, 90.0, 3);
            // blocks x=13, z=-6..-1, y=64..65 -> section (0, 4, -1)
            Assert.True(p.Covers(0, 4, -1));
            Assert.False(p.Covers(0, 4, 0));
            Assert.False(p.Covers(1, 4, -1));
            Assert.False(p.Covers(0, 3, -1));
            Assert.False(p.Covers(0, 5, -1));
        }

        [Fact]
        public void CoversUsesFloorDivisionForNegativeCoordinates()
        {
            // yaw 0, feet block (-1,64,-1), centre (-1,-2); X = -3..2, z = -2
            BlockRowPlan p = BlockRowPlan.Create(-0.5, 64.0, -0.5, 0.0, 1);
            Assert.Equal(new[] { -3, -2, -1, 0, 1, 2 }, p.BlockX);
            Assert.True(p.Covers(-1, 4, -1));   // x -3..-1, z -2 -> sections -1,-1
            Assert.True(p.Covers(0, 4, -1));    // x 0..2
            Assert.False(p.Covers(-1, 4, 0));
            Assert.False(p.Covers(0, 4, 0));
            Assert.False(p.Covers(-2, 4, -1));
        }

        [Fact]
        public void CoversChecksBothLayersAcrossNegativeYSections()
        {
            // feet block (0,-1,0): column occupies y=-1 (section -1) and y=0 (section 0)
            BlockRowPlan p = BlockRowPlan.Create(0.5, -0.5, 0.5, 90.0, 2);
            Assert.Equal(-1, p.FeetBlockY);
            Assert.Equal(new[] { 2, 2, 2, 2, 2, 2 }, p.BlockX);
            Assert.Equal(new[] { -2, -1, 0, 1, 2, 3 }, p.BlockZ);
            Assert.True(p.Covers(0, -1, -1));
            Assert.True(p.Covers(0, 0, -1));
            Assert.True(p.Covers(0, 0, 0));
            Assert.False(p.Covers(0, -2, 0));
            Assert.False(p.Covers(0, 1, 0));
        }
    }
}
