using System.Diagnostics;
using System.Threading;
using MinecraftSkylines.Mod.City;
using Xunit;

namespace MinecraftSkylines.Mod.Tests
{
    public class EditSyncBarrierTests
    {
        [Fact]
        public void TokensCountFromOne()
        {
            var b = new EditSyncBarrier();
            Assert.Equal(1u, b.Begin());
            Assert.Equal(2u, b.Begin());
            Assert.Equal(3u, b.Begin());
        }

        [Fact]
        public void AckBeforeWaitReturnsTrueImmediately()
        {
            var b = new EditSyncBarrier();
            var t = b.Begin();
            b.Acknowledge(t);
            var sw = Stopwatch.StartNew();
            Assert.True(b.Wait(t, 5000));
            Assert.True(sw.ElapsedMilliseconds < 1000);
        }

        [Fact]
        public void UnackedWaitTimesOut()
        {
            var b = new EditSyncBarrier();
            var t = b.Begin();
            Assert.False(b.Wait(t, 50));
        }

        [Fact]
        public void AckCoversEarlierTokens()
        {
            var b = new EditSyncBarrier();
            var t1 = b.Begin();
            var t2 = b.Begin();
            var t3 = b.Begin();
            b.Acknowledge(t2);
            Assert.True(b.Wait(t1, 50));
            Assert.True(b.Wait(t2, 50));
            Assert.False(b.Wait(t3, 50));
        }

        [Fact]
        public void NeverIssuedTokenAckIsIgnored()
        {
            var b = new EditSyncBarrier();
            var t = b.Begin();
            b.Acknowledge(999);
            Assert.False(b.Wait(t, 50));
        }

        [Fact]
        public void WaiterOnOtherThreadReleasedByAck()
        {
            var b = new EditSyncBarrier();
            var t = b.Begin();
            bool? result = null;
            var th = new Thread(() => result = b.Wait(t, 10000));
            th.Start();
            Thread.Sleep(100);
            Assert.Null(result);
            b.Acknowledge(t);
            Assert.True(th.Join(5000));
            Assert.True(result);
        }

        [Fact]
        public void CancelReleasesWaiterWithFalse()
        {
            var b = new EditSyncBarrier();
            var t = b.Begin();
            bool? result = null;
            var th = new Thread(() => result = b.Wait(t, 10000));
            th.Start();
            Thread.Sleep(100);
            b.Cancel();
            Assert.True(th.Join(5000));
            Assert.False(result);
        }

        [Fact]
        public void CancelDoesNotUndoAck()
        {
            var b = new EditSyncBarrier();
            var t = b.Begin();
            b.Acknowledge(t);
            b.Cancel();
            Assert.True(b.Wait(t, 50));
        }

        [Fact]
        public void CancelReleasesPendingTokenForLaterWait()
        {
            var b = new EditSyncBarrier();
            var t = b.Begin();
            b.Cancel();
            var sw = Stopwatch.StartNew();
            Assert.False(b.Wait(t, 5000));
            Assert.True(sw.ElapsedMilliseconds < 2000);
        }

        [Fact]
        public void TokensAfterCancelAreUnaffected()
        {
            var b = new EditSyncBarrier();
            b.Begin();
            b.Cancel();
            var t = b.Begin();
            Assert.False(b.Wait(t, 50));
            b.Acknowledge(t);
            Assert.True(b.Wait(t, 50));
        }
    }
}
