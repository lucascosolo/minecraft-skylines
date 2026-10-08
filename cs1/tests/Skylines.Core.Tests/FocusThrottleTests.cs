using Skylines.Core.Streaming;
using Xunit;

namespace Skylines.Core.Tests
{
    public class FocusThrottleTests
    {
        private static FocusThrottle Sent00()
        {
            var t = new FocusThrottle(8f, 0.25);
            t.Sent(true, 0f, 0f, 0.0);
            return t;
        }

        [Fact]
        public void TrueBeforeAnythingSentAndAfterReset()
        {
            var t = new FocusThrottle(8f, 0.25);
            Assert.True(t.ShouldSend(false, 0f, 0f, 0.0));
            Assert.True(t.ShouldSend(true, 0f, 0f, 0.0));
            t.Sent(true, 0f, 0f, 0.0);
            t.Reset();
            Assert.True(t.ShouldSend(true, 0f, 0f, 100.0));
        }

        [Fact]
        public void BelowMinMoveIsFalse()
        {
            Assert.False(Sent00().ShouldSend(true, 7.9f, 0f, 1.0));
        }

        [Fact]
        public void MinMoveBeforeIntervalIsFalse()
        {
            Assert.False(Sent00().ShouldSend(true, 8f, 0f, 0.1));
        }

        [Fact]
        public void MinMoveAtIntervalIsTrue()
        {
            Assert.True(Sent00().ShouldSend(true, 8f, 0f, 0.25));
        }

        [Fact]
        public void DistanceIsHorizontalOverBothAxes()
        {
            Assert.True(Sent00().ShouldSend(true, 6f, 6f, 1.0));
            Assert.True(Sent00().ShouldSend(true, 0f, -8f, 1.0));
        }

        [Fact]
        public void ActiveChangeSendsImmediately()
        {
            Assert.True(Sent00().ShouldSend(false, 0f, 0f, 0.01));
            var t = new FocusThrottle(8f, 0.25);
            t.Sent(false, 0f, 0f, 0.0);
            Assert.True(t.ShouldSend(true, 0f, 0f, 0.01));
        }

        [Fact]
        public void BothInactiveIsFalse()
        {
            var t = new FocusThrottle(8f, 0.25);
            t.Sent(false, 0f, 0f, 0.0);
            Assert.False(t.ShouldSend(false, 500f, 500f, 10.0));
        }

        [Fact]
        public void SentUpdatesReference()
        {
            var t = Sent00();
            t.Sent(true, 8f, 0f, 1.0);
            Assert.False(t.ShouldSend(true, 9f, 0f, 2.0));
            Assert.True(t.ShouldSend(true, 16f, 0f, 2.0));
        }
    }
}
