using System.Threading;

namespace MinecraftSkylines.Mod.City
{
    /// <summary>
    /// The save barrier's rendezvous: the simulation thread takes a token and waits until the main thread has
    /// applied the guest's EDIT_SYNC_ACK for it (an ack also covers every earlier token: TCP keeps order).
    /// <see cref="Cancel"/> releases current waiters (link lost, city closed). Unity-free.
    /// </summary>
    internal sealed class EditSyncBarrier
    {
        private readonly object _sync = new object();
        private uint _next;
        private uint _acked;
        private uint _cancelledUpTo;

        /// <summary>A new token, 1, 2, ...</summary>
        public uint Begin()
        {
            lock (_sync) return ++_next;
        }

        /// <summary>Main thread, after the batches before this ack were applied.</summary>
        public void Acknowledge(uint token)
        {
            lock (_sync)
            {
                if (token > _next) return;
                if (token > _acked) _acked = token;
                Monitor.PulseAll(_sync);
            }
        }

        /// <summary>Releases every token issued so far; their <see cref="Wait"/> returns false unless already acked.</summary>
        public void Cancel()
        {
            lock (_sync)
            {
                _cancelledUpTo = _next;
                Monitor.PulseAll(_sync);
            }
        }

        /// <summary>True once <paramref name="token"/> is acknowledged; false on time-out or cancel.</summary>
        public bool Wait(uint token, int timeoutMs)
        {
            int deadline = System.Environment.TickCount + timeoutMs;
            lock (_sync)
            {
                while (true)
                {
                    if (_acked >= token) return true;
                    if (_cancelledUpTo >= token) return false;
                    int left = deadline - System.Environment.TickCount;
                    if (left <= 0) return false;
                    Monitor.Wait(_sync, left);
                }
            }
        }
    }
}
