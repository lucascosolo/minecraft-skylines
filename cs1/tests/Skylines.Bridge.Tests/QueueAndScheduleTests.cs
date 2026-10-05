using Xunit;

namespace Skylines.Bridge.Tests
{
    public class QueueAndScheduleTests
    {
        [Fact]
        public void OutboundQueueRejectsFramesBeyondTheBound()
        {
            var q = new OutboundQueue(100);
            Assert.True(q.TryEnqueue(new byte[60], false));
            Assert.False(q.TryEnqueue(new byte[41], false));
            Assert.Equal(60, q.QueuedBytes);
            Assert.True(q.TryEnqueue(new byte[40], false));
            Assert.Equal(100, q.QueuedBytes);
            Assert.False(q.TryEnqueue(new byte[1], false));
        }

        [Fact]
        public void OutboundQueueFreesSpaceWhenDrainedAndForceIgnoresTheBound()
        {
            var q = new OutboundQueue(100);
            Assert.True(q.TryEnqueue(new byte[100], false));
            Assert.False(q.TryEnqueue(new byte[10], false));
            Assert.True(q.TryEnqueue(new byte[10], true));
            Assert.Equal(100, q.Take().Length);
            Assert.Equal(10, q.QueuedBytes);
            Assert.True(q.TryEnqueue(new byte[90], false));
        }

        [Fact]
        public void OutboundQueueTakeReturnsNullAfterClose()
        {
            var q = new OutboundQueue(100);
            q.TryEnqueue(new byte[5], false);
            q.Close();
            Assert.Null(q.Take());
            Assert.Equal(0, q.QueuedBytes);
        }

        [Fact]
        public void DefaultRetryScheduleIsOneSecondTenTimesThenFive()
        {
            var r = new RetrySchedule();
            for (int i = 0; i < 10; i++) Assert.Equal(1000, r.GetDelayMs(i));
            Assert.Equal(5000, r.GetDelayMs(10));
            Assert.Equal(5000, r.GetDelayMs(500));
        }

        [Fact]
        public void CustomRetryScheduleSwitchesAfterItsFastAttempts()
        {
            var r = new RetrySchedule(200, 3, 900);
            Assert.Equal(new[] { 200, 200, 200, 900, 900 },
                new[] { r.GetDelayMs(0), r.GetDelayMs(1), r.GetDelayMs(2), r.GetDelayMs(3), r.GetDelayMs(4) });
        }
    }
}
