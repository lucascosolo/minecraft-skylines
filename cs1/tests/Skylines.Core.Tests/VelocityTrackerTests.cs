using Skylines.Core.Motion;
using Xunit;

namespace Skylines.Core.Tests
{
    public class VelocityTrackerTests
    {
        private static Vec3d V(double x, double y, double z)
        {
            return new Vec3d { X = x, Y = y, Z = z };
        }

        private static Vec3d Round(VelocityTracker t, double now, long key, Vec3d p)
        {
            t.Begin(now);
            Vec3d v = t.Sample(key, p);
            t.End();
            return v;
        }

        [Fact]
        public void FirstSightIsZero()
        {
            var t = new VelocityTracker();
            Vec3d v = Round(t, 0, 1, V(5, 6, 7));
            Assert.Equal(0, v.X); Assert.Equal(0, v.Y); Assert.Equal(0, v.Z);
            Assert.Equal(1, t.Count);
        }

        [Fact]
        public void SteadyMotionMeasuresSpeed()
        {
            var t = new VelocityTracker();
            Round(t, 0.0, 1, V(0, 0, 0));
            Vec3d v = Round(t, 0.05, 1, V(1.0, 0, 0));
            Assert.Equal(20.0, v.X, 6);
            Assert.Equal(0.0, v.Y, 6);
            Assert.Equal(0.0, v.Z, 6);
        }

        [Fact]
        public void JumpOverMaxSpeedIsZeroAndNextSampleMeasuresFromJumpedPosition()
        {
            var t = new VelocityTracker();
            Round(t, 0.0, 1, V(0, 0, 0));
            Vec3d jump = Round(t, 0.05, 1, V(100, 0, 0));
            Assert.Equal(0, jump.X); Assert.Equal(0, jump.Y); Assert.Equal(0, jump.Z);
            Vec3d next = Round(t, 0.10, 1, V(101, 0, 0));
            Assert.Equal(20.0, next.X, 6);
        }

        [Fact]
        public void EndForgetsUnsampledKeys()
        {
            var t = new VelocityTracker();
            t.Begin(0.0); t.Sample(1, V(0, 0, 0)); t.Sample(2, V(0, 0, 0)); t.End();
            Assert.Equal(2, t.Count);
            t.Begin(0.05); t.Sample(1, V(1, 0, 0)); t.End();
            Assert.Equal(1, t.Count);
            Vec3d v = Round(t, 0.10, 2, V(1, 0, 0));
            Assert.Equal(0, v.X); Assert.Equal(0, v.Y); Assert.Equal(0, v.Z);
        }

        [Fact]
        public void NonPositiveDtIsZero()
        {
            var t = new VelocityTracker();
            Round(t, 1.0, 1, V(0, 0, 0));
            Vec3d same = Round(t, 1.0, 1, V(1, 0, 0));
            Assert.Equal(0, same.X);
            Vec3d back = Round(t, 0.5, 1, V(2, 0, 0));
            Assert.Equal(0, back.X);
        }

        [Fact]
        public void KeysAreIndependent()
        {
            var t = new VelocityTracker();
            t.Begin(0.0); t.Sample(1, V(0, 0, 0)); t.Sample(2, V(0, 0, 0)); t.End();
            t.Begin(0.1);
            Vec3d a = t.Sample(1, V(1, 0, 0));
            Vec3d b = t.Sample(2, V(0, 0, -2));
            t.End();
            Assert.Equal(10.0, a.X, 6);
            Assert.Equal(0.0, a.Z, 6);
            Assert.Equal(-20.0, b.Z, 6);
            Assert.Equal(0.0, b.X, 6);
        }
    }
}
