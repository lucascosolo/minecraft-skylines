using System;
using Skylines.Core.Geometry;
using Xunit;

namespace Skylines.Core.Tests
{
    public class NormalBendTests
    {
        private static void Bend(float x, float y, float z, float k, out float ox, out float oy, out float oz)
        {
            NormalBend.Apply(x, y, z, k, out ox, out oy, out oz);
        }

        [Fact]
        public void ZeroKeepsTheNormal()
        {
            float x, y, z;
            Bend(1, 0, 0, 0f, out x, out y, out z);
            Assert.Equal(1f, x); Assert.Equal(0f, y); Assert.Equal(0f, z);
        }

        [Fact]
        public void OneMakesEveryNormalUp()
        {
            float x, y, z;
            Bend(0, 0, -1, 1f, out x, out y, out z);
            Assert.Equal(0f, x, 5); Assert.Equal(1f, y, 5); Assert.Equal(0f, z, 5);
        }

        [Fact]
        public void HalfTiltsASideFaceBy45DegreesAndKeepsItsDirection()
        {
            float x, y, z;
            Bend(1, 0, 0, 0.5f, out x, out y, out z);
            Assert.Equal((float)(1 / Math.Sqrt(2)), x, 5);
            Assert.Equal((float)(1 / Math.Sqrt(2)), y, 5);
            Assert.Equal(0f, z, 5);
        }

        [Fact]
        public void ResultIsUnitLengthAndOpposingSidesStayDistinct()
        {
            float ax, ay, az, bx, by, bz;
            Bend(0, 0, 1, 0.6f, out ax, out ay, out az);
            Bend(0, 0, -1, 0.6f, out bx, out by, out bz);
            Assert.Equal(1f, (float)Math.Sqrt(ax * ax + ay * ay + az * az), 5);
            Assert.True(az > 0 && bz < 0, "the sunny and the shaded side must still face opposite ways");
        }

        [Fact]
        public void BottomFaceAtFullBendDoesNotProduceNaN()
        {
            float x, y, z;
            Bend(0, -1, 0, 1f, out x, out y, out z);
            Assert.False(float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z));
            Assert.Equal(1f, y, 5);
        }

        [Fact]
        public void KAboveOneIsClamped()
        {
            float x, y, z;
            Bend(1, 0, 0, 3f, out x, out y, out z);
            Assert.Equal(0f, x, 5); Assert.Equal(1f, y, 5);
        }
    }
}
