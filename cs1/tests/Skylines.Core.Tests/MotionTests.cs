using System;
using Skylines.Core.Motion;
using Xunit;

namespace Skylines.Core.Tests
{
    public class TickInterpolatorTests
    {
        const float Tick = 50f;

        static Vec3d V(double x, double y = 0, double z = 0) { return new Vec3d { X = x, Y = y, Z = z }; }

        // Renders at now - (tickMs + 10) with the default extra delay.
        static double NowFor(double renderTime) { return renderTime + Tick + 10; }

        [Fact]
        public void EmptyReturnsFalse()
        {
            var t = new TickInterpolator();
            Vec3d p; float e;
            Assert.False(t.Sample(1000, out p, out e));
        }

        [Fact]
        public void ResetClears()
        {
            var t = new TickInterpolator();
            t.Push(1, V(1), 1.6f, 1000, Tick);
            Vec3d p; float e;
            Assert.True(t.Sample(2000, out p, out e));
            t.Reset();
            Assert.False(t.Sample(2000, out p, out e));
        }

        [Fact]
        public void InterpolatesExactlyAtMidpointOnIdealTimeline()
        {
            var t = new TickInterpolator();
            t.Push(1, V(0, 0, 0), 1.0f, 1000, Tick);
            t.Push(2, V(10, 20, -4), 2.0f, 1050, Tick);
            Vec3d p; float e;
            Assert.True(t.Sample(NowFor(1025), out p, out e));
            Assert.Equal(5.0, p.X, 6);
            Assert.Equal(10.0, p.Y, 6);
            Assert.Equal(-2.0, p.Z, 6);
            Assert.Equal(1.5f, e, 4);
        }

        [Fact]
        public void JitteredArrivalStillInterpolatesOnTheIdealTimeline()
        {
            var t = new TickInterpolator();
            t.Push(1, V(0), 0, 1000, Tick);
            t.Push(2, V(10), 0, 1070, Tick);   // 20 ms late: within tolerance, stamp stays 1050
            Vec3d p; float e;
            Assert.True(t.Sample(NowFor(1025), out p, out e));
            Assert.Equal(5.0, p.X, 6);
        }

        [Fact]
        public void ClampsBeforeOldestAndAfterNewestWithoutExtrapolating()
        {
            var t = new TickInterpolator();
            t.Push(1, V(0), 1f, 1000, Tick);
            t.Push(2, V(10), 2f, 1050, Tick);
            Vec3d p; float e;
            Assert.True(t.Sample(NowFor(900), out p, out e));
            Assert.Equal(0.0, p.X, 6);
            Assert.Equal(1f, e, 4);
            Assert.True(t.Sample(NowFor(5000), out p, out e));
            Assert.Equal(10.0, p.X, 6);
            Assert.Equal(2f, e, 4);
        }

        [Fact]
        public void DuplicateAndOlderSequencesAreIgnored()
        {
            var t = new TickInterpolator();
            t.Push(5, V(50), 0, 1000, Tick);
            t.Push(5, V(999), 0, 1010, Tick);
            t.Push(4, V(-999), 0, 1020, Tick);
            Vec3d p; float e;
            Assert.True(t.Sample(NowFor(2000), out p, out e));
            Assert.Equal(50.0, p.X, 6);
            t.Push(6, V(60), 0, 1050, Tick);
            Assert.True(t.Sample(NowFor(1025), out p, out e));
            Assert.Equal(55.0, p.X, 6);
        }

        [Fact]
        public void WrapAroundAtUintMaxValueKeepsOrdering()
        {
            var t = new TickInterpolator();
            t.Push(uint.MaxValue - 1, V(0), 0, 1000, Tick);
            t.Push(uint.MaxValue, V(10), 0, 1050, Tick);
            t.Push(0, V(20), 0, 1100, Tick);
            t.Push(1, V(30), 0, 1150, Tick);
            t.Push(uint.MaxValue, V(-1), 0, 1160, Tick);   // older than seq 1 in serial arithmetic
            Vec3d p; float e;
            Assert.True(t.Sample(NowFor(1075), out p, out e));
            Assert.Equal(15.0, p.X, 6);
            Assert.True(t.Sample(NowFor(9999), out p, out e));
            Assert.Equal(30.0, p.X, 6);
        }

        [Fact]
        public void KeepsAtMostSixteenSamples()
        {
            var t = new TickInterpolator();
            for (uint i = 1; i <= 20; i++) t.Push(i, V(i), 0, 1000 + (i - 1) * 50.0, Tick);
            Vec3d p; float e;
            Assert.True(t.Sample(NowFor(0), out p, out e));
            Assert.Equal(5.0, p.X, 6);   // seqs 1..4 were dropped; the oldest kept is 5
        }

        [Fact]
        public void ReAnchorsAfterAThreeHundredMillisecondStall()
        {
            var t = new TickInterpolator();
            t.Push(1, V(0), 0, 1000, Tick);
            t.Push(2, V(10), 0, 1050, Tick);
            t.Push(3, V(20), 0, 1400, Tick);   // ideal 1100, arrived 300 late: new anchor, stamp 1400
            t.Push(4, V(30), 0, 1450, Tick);   // stamp 1450 on the new timeline
            Vec3d p; float e;
            Assert.True(t.Sample(NowFor(1425), out p, out e));
            Assert.Equal(25.0, p.X, 6);        // would be clamped to 30 had the old anchor been kept
            Assert.True(t.Sample(NowFor(1400), out p, out e));
            Assert.Equal(20.0, p.X, 6);
        }

        [Fact]
        public void ExtraDelayIsConfigurable()
        {
            var t = new TickInterpolator(0);
            t.Push(1, V(0), 0, 1000, Tick);
            t.Push(2, V(10), 0, 1050, Tick);
            Vec3d p; float e;
            Assert.True(t.Sample(1025 + Tick, out p, out e));
            Assert.Equal(5.0, p.X, 6);
        }
    }
}
