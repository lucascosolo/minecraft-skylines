using System.Collections.Generic;
using System.Threading;

namespace Skylines.Bridge
{
    /// <summary>
    /// Bounded FIFO of encoded frames shared between producers (game thread, heartbeat thread) and the
    /// writer thread. The bound counts encoded bytes; <see cref="TryEnqueue"/> never blocks.
    /// </summary>
    internal sealed class OutboundQueue
    {
        private readonly object _sync = new object();
        private readonly Queue<byte[]> _frames = new Queue<byte[]>();
        private readonly long _limitBytes;
        private long _bytes;
        private bool _closed;

        public OutboundQueue(long limitBytes) { _limitBytes = limitBytes; }

        public long QueuedBytes { get { lock (_sync) return _bytes; } }

        /// <summary>Adds a frame. Returns false (and adds nothing) when it would exceed the bound, unless <paramref name="force"/>.</summary>
        public bool TryEnqueue(byte[] frame, bool force)
        {
            lock (_sync)
            {
                if (_closed) return true;
                if (!force && _bytes + frame.Length > _limitBytes) return false;
                _frames.Enqueue(frame);
                _bytes += frame.Length;
                Monitor.Pulse(_sync);
                return true;
            }
        }

        /// <summary>Blocks until a frame is available; returns null once the queue is closed.</summary>
        public byte[] Take()
        {
            lock (_sync)
            {
                while (_frames.Count == 0 && !_closed) Monitor.Wait(_sync);
                if (_closed) return null;
                byte[] f = _frames.Dequeue();
                _bytes -= f.Length;
                return f;
            }
        }

        /// <summary>Wakes the consumer and discards pending frames.</summary>
        public void Close()
        {
            lock (_sync)
            {
                _closed = true;
                _frames.Clear();
                _bytes = 0;
                Monitor.PulseAll(_sync);
            }
        }
    }
}
