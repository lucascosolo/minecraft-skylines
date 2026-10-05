using System;
using System.Net.Sockets;
using System.Threading;

namespace Skylines.Bridge
{
    /// <summary>
    /// One established (post-handshake) connection: a reader (the thread that calls <see cref="Run"/>),
    /// a writer thread draining the outbound queue, and a monitor thread that sends heartbeats and
    /// enforces the liveness timeout and the queue bound. Shared by host and guest.
    /// </summary>
    internal sealed class Session
    {
        private const int GoodbyeLockWaitMs = 400;
        private const int GoodbyeDrainMs = 300;

        private readonly Socket _sock;
        private readonly int _heartbeatMs;
        private readonly int _timeoutMs;
        private readonly OutboundQueue _queue;
        private readonly Action<string> _log;
        private readonly Action<ushort, byte[]> _onMessage;
        private readonly Action<DisconnectCause, int, string> _onEnded;

        private readonly object _state = new object();
        private readonly object _writeLock = new object();
        private readonly object _wake = new object();
        private bool _ended;
        private bool _overflow;
        private bool _woken;
        private long _lastRxMs;
        private uint _hbSeq;

        public Session(Socket sock, int heartbeatMs, int timeoutMs, long maxQueuedBytes, Action<string> log,
            Action<ushort, byte[]> onMessage, Action<DisconnectCause, int, string> onEnded)
        {
            _sock = sock;
            _heartbeatMs = Math.Max(heartbeatMs, 10);
            _timeoutMs = Math.Max(timeoutMs, 10);
            _queue = new OutboundQueue(maxQueuedBytes);
            _log = log;
            _onMessage = onMessage;
            _onEnded = onEnded;
            _lastRxMs = Clock.NowMs();
        }

        /// <summary>Starts the writer and monitor threads, then reads on the calling thread until the session ends.</summary>
        public void Run()
        {
            Spawn(WriterLoop, "SKBR writer");
            Spawn(MonitorLoop, "SKBR monitor");
            ReaderLoop();
        }

        /// <summary>Queues a frame without blocking. Returns false if the session ended or the queue bound was hit.</summary>
        public bool Send(byte[] frame)
        {
            lock (_state) if (_ended) return false;
            if (_queue.TryEnqueue(frame, false)) return true;
            lock (_state) _overflow = true;
            Wake();
            return false;
        }

        /// <summary>Sends GOODBYE(code) and closes within about a second.</summary>
        public void Shutdown(ushort code, string reason)
        {
            Terminate(DisconnectCause.LocalGoodbye, code, reason, code);
        }

        private static void Spawn(ThreadStart body, string name)
        {
            var t = new Thread(body) { IsBackground = true, Name = name };
            t.Start();
        }

        private void ReaderLoop()
        {
            while (true)
            {
                Frame f;
                try
                {
                    f = FrameCodec.Read(_sock, 0);
                }
                catch (ProtocolException e)
                {
                    Terminate(DisconnectCause.ProtocolError, GoodbyeCodes.ProtocolError, e.Message, GoodbyeCodes.ProtocolError);
                    return;
                }
                catch (Exception)
                {
                    Terminate(DisconnectCause.ConnectionLost, -1, "connection closed", -1);
                    return;
                }
                if (f == null)
                {
                    Terminate(DisconnectCause.ConnectionLost, -1, "connection closed", -1);
                    return;
                }
                lock (_state)
                {
                    if (_ended) return;
                    _lastRxMs = Clock.NowMs();
                }
                try
                {
                    if (!Dispatch(f)) return;
                }
                catch (ProtocolException e)
                {
                    Terminate(DisconnectCause.ProtocolError, GoodbyeCodes.ProtocolError, e.Message, GoodbyeCodes.ProtocolError);
                    return;
                }
            }
        }

        /// <summary>Handles one frame; returns false when the session ended.</summary>
        private bool Dispatch(Frame f)
        {
            if (f.Type == MessageTypes.Goodbye)
            {
                Goodbye g = Goodbye.Decode(f.Payload);
                Terminate(DisconnectCause.PeerGoodbye, g.Code, g.Reason, -1);
                return false;
            }
            if (f.Type == MessageTypes.Heartbeat)
            {
                Heartbeat.Decode(f.Payload);
                return true;
            }
            if (f.Type < BridgeConstants.AppTypeMin)
                throw new ProtocolException("unexpected bridge frame 0x" + f.Type.ToString("x4") + " after handshake");
            _onMessage(f.Type, f.Payload);
            return true;
        }

        private void WriterLoop()
        {
            while (true)
            {
                byte[] frame = _queue.Take();
                if (frame == null) return;
                try
                {
                    lock (_writeLock)
                    {
                        lock (_state) if (_ended) return;
                        SocketUtil.SendAll(_sock, frame);
                    }
                }
                catch (Exception)
                {
                    Terminate(DisconnectCause.ConnectionLost, -1, "write failed", -1);
                    return;
                }
            }
        }

        private void MonitorLoop()
        {
            long nextHb = Clock.NowMs() + _heartbeatMs;
            while (true)
            {
                long now = Clock.NowMs();
                bool overflow;
                long idle;
                lock (_state)
                {
                    if (_ended) return;
                    overflow = _overflow;
                    idle = now - _lastRxMs;
                }
                if (overflow)
                {
                    Terminate(DisconnectCause.Backpressure, GoodbyeCodes.Backpressure, "outbound queue overflow", GoodbyeCodes.Backpressure);
                    return;
                }
                if (idle >= _timeoutMs)
                {
                    Terminate(DisconnectCause.Timeout, GoodbyeCodes.Timeout, "no frame for " + idle + " ms", GoodbyeCodes.Timeout);
                    return;
                }
                if (now >= nextHb)
                {
                    var hb = new Heartbeat { Seq = ++_hbSeq, SenderUptimeMs = (ulong)now };
                    _queue.TryEnqueue(FrameCodec.Encode(MessageTypes.Heartbeat, hb.Encode()), true);
                    nextHb = now + _heartbeatMs;
                }
                long wait = Math.Min(nextHb - now, _timeoutMs - idle);
                lock (_wake)
                {
                    if (!_woken) Monitor.Wait(_wake, (int)Math.Max(wait, 1));
                    _woken = false;
                }
            }
        }

        private void Wake()
        {
            lock (_wake)
            {
                _woken = true;
                Monitor.PulseAll(_wake);
            }
        }

        /// <summary>
        /// Ends the session once. When <paramref name="goodbyeCode"/> is not negative a GOODBYE is sent
        /// best-effort first; the socket is then closed, normally within about 700 ms.
        /// </summary>
        public void Terminate(DisconnectCause cause, int code, string reason, int goodbyeCode)
        {
            lock (_state)
            {
                if (_ended) return;
                _ended = true;
            }
            Wake();
            _queue.Close();
            bool sentGoodbye = false;
            if (goodbyeCode >= 0 && Monitor.TryEnter(_writeLock, GoodbyeLockWaitMs))
            {
                try
                {
                    var g = new Goodbye { Code = (ushort)goodbyeCode, Reason = reason ?? "" };
                    SocketUtil.SendAll(_sock, FrameCodec.Encode(MessageTypes.Goodbye, g.Encode()));
                    sentGoodbye = true;
                }
                catch (Exception e)
                {
                    _log("goodbye not sent: " + e.Message);
                }
                finally
                {
                    Monitor.Exit(_writeLock);
                }
            }
            SocketUtil.CloseGracefully(_sock, sentGoodbye ? GoodbyeDrainMs : 0);
            _onEnded(cause, code, reason);
        }
    }
}
