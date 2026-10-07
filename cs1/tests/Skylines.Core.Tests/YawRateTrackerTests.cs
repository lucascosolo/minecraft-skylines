using Skylines.Core.Motion;
using Xunit;

namespace Skylines.Core.Tests
{
    public class YawRateTrackerTests
    {
        private static double Round(YawRateTracker t, double now, long key, double yaw)
        {
            t.Begin(now);
            double r = t.Sample(key, yaw);
            t.End();
            return r;
        }

        [Fact]
        public void FirstSampleIsZero()
        {
            var t = new YawRateTracker();
            Assert.Equal(0.0, Round(t, 0, 1, 90));
            Assert.Equal(1, t.Count);
        }

        [Fact]
        public void SteadyTurnMeasuresDegreesPerSecond()
        {
            var t = new YawRateTracker();
            Round(t, 0.0, 1, 10);
            Assert.Equal(20.0, Round(t, 0.1, 1, 12), 6);
            Assert.Equal(-50.0, Round(t, 0.2, 1, 7), 6);
        }

        [Fact]
        public void DifferenceWrapsAcrossTheSeam()
        {
            var t = new YawRateTracker();
            Round(t, 0.0, 1, 179);
            Assert.Equal(20.0, Round(t, 0.1, 1, -179), 6);
            Assert.Equal(-20.0, Round(t, 0.2, 1, 179), 6);
        }

        [Fact]
        public void NonPositiveDtIsZero()
        {
            var t = new YawRateTracker();
            Round(t, 1.0, 1, 0);
            Assert.Equal(0.0, Round(t, 1.0, 1, 5));
        }

        [Fact]
        public void RateAboveMaxIsZero()
        {
            Assert.Equal(720.0, YawRateTracker.MaxRate);
            var t = new YawRateTracker();
            Round(t, 0.0, 1, 0);
            Assert.Equal(0.0, Round(t, 0.1, 1, 90)); // 900 deg/s
            Round(t, 0.2, 2, 0);
            Assert.Equal(700.0, Round(t, 0.3, 2, 70), 6);
        }

        [Fact]
        public void KeysAreIndependent()
        {
            var t = new YawRateTracker();
            t.Begin(0); t.Sample(1, 0); t.Sample(2, 0); t.End();
            t.Begin(0.1);
            double a = t.Sample(1, 1);
            double b = t.Sample(2, -2);
            t.End();
            Assert.Equal(10.0, a, 6);
            Assert.Equal(-20.0, b, 6);
            Assert.Equal(2, t.Count);
        }

        [Fact]
        public void EndForgetsKeysNotSampledThisRound()
        {
            var t = new YawRateTracker();
            Round(t, 0.0, 1, 0);
            t.Begin(0.1); t.Sample(2, 0); t.End();
            Assert.Equal(1, t.Count);
            Assert.Equal(0.0, Round(t, 0.2, 1, 5));
        }
    }
}
