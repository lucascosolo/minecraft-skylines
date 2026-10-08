using Skylines.Core.Resources;
using Xunit;

namespace Skylines.Core.Tests
{
    public class ResourceGridTests
    {
        [Fact]
        public void Constants()
        {
            Assert.Equal(33.75f, ResourceGrid.CellSize);
            Assert.Equal(512, ResourceGrid.Resolution);
        }

        [Theory]
        [InlineData(0f, 256)]
        [InlineData(33.75f, 257)]
        [InlineData(-0.01f, 255)]
        [InlineData(-33.75f, 255)]
        [InlineData(-1e9f, 0)]
        [InlineData(1e9f, 511)]
        public void CellClampsToGrid(float cs, int expected)
        {
            Assert.Equal(expected, ResourceGrid.Cell(cs));
        }

        [Theory]
        [InlineData(100, 30, 70)]
        [InlineData(100, 100, 0)]
        [InlineData(100, 500, 0)]
        [InlineData(100, 0, 100)]
        [InlineData(100, -5, 100)]
        [InlineData(0, 3, 0)]
        [InlineData(255, 1, 254)]
        public void DepleteSubtractsAndClampsAtZero(int current, int units, int expected)
        {
            Assert.Equal((byte)expected, ResourceGrid.Deplete((byte)current, units));
        }
    }
}
