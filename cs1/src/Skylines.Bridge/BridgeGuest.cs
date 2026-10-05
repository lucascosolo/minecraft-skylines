using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Skylines.Bridge
{
    /// <summary>Settings for <see cref="BridgeGuest"/>.</summary>
    public sealed class GuestOptions
    {
        /// <summary>Host TCP port on 127.0.0.1.</summary>
        public int Port = BridgeConstants.DefaultPort;
        /// <summary>The application protocol name announced in HELLO.</summary>
        public string AppProtocol = "";
        /// <summary>The application major version announced in HELLO.</summary>
        public ushort AppMajor = 1;
        /// <summary>The application minor version announced in HELLO.</summary>
        public ushort AppMinor;
        /// <summary>Human-readable guest name.</summary>
        public string PeerName = "SKBR guest";
        /// <summary>The guest mod's version.</summary>
        public string PeerVersion = "0";
        /// <summary>Delay between connection attempts.</summary>
        public RetrySchedule Retry = new RetrySchedule();
        /// <summary>How long to wait for WELCOME.</summary>
        public int HandshakeTimeoutMs = 5000;
        /// <summary>Outbound queue bound in bytes (default 64 MiB).</summary>
        public long MaxQueuedBytes = 64L * 1024 * 1024;
        /// <summary>Receives diagnostic lines from any thread; may be null.</summary>
        public Action<string> Log;
    }

    /// <summary>
    /// SKBR guest: connects to a host on loopback, retrying on <see cref="GuestOptions.Retry"/>, and
    /// reconnects after a session ends. Stops for good when the host rejects it for a version mismatch.
    /// All socket work happens on a background thread; <see cref="Send"/> and <see cref="Poll"/> never block.
    /// </summary>
    public sealed class BridgeGuest
    {
        private const int Failed = 0, Established = 1, Fatal = 2;

        private readonly GuestOptions _o;
        private readonly Action<string> _log;
        private readonly EventSink _sink = new EventSink();
        private readonly object _sync = new object();
        private Socket _connecting;
        private Session _active;
        private bool _started;
        private bool _stopping;

        /// <summary>Creates a guest; call <see cref="Start"/> to begin connecting.</summary>
        public BridgeGuest(GuestOptions options)
        {
            _o = options;
            Action<string> l = options.Log;
            _log = l ?? (s => { });
            PeerName = "";
            PeerVersion = "";
            Clock.NowMs();
        }

        /// <summary>The current connection state.</summary>
        public BridgeState State { get { return _sink.State; } }

        /// <summary>The host's name (empty when not connected).</summary>
        public string PeerName { get; private set; }

        /// <summary>The host mod's version (empty when not connected).</summary>
        public string PeerVersion { get; private set; }

        /// <summary>min(guest, host) application minor version of the current session.</summary>
        public ushort NegotiatedAppMinor { get; private set; }

        /// <summary>Starts connecting on a background thread.</summary>
        public void Start()
        {
            lock (_sync)
            {
                if (_started || _stopping) return;
                _started = true;
            }
            var t = new Thread(Loop) { IsBackground = true, Name = "SKBR guest" };
            t.Start();
        }

        /// <summary>
        /// Queues an application frame for the host. Never blocks. Returns false when not connected (the
        /// frame is dropped) or the queue bound was exceeded (the session is being closed).
        /// </summary>
        public bool Send(ushort type, byte[] payload)
        {
            if (type < BridgeConstants.AppTypeMin) throw new ArgumentOutOfRangeException("type", "application types start at 0x0100");
            Session s;
            lock (_sync) s = _active;
            return s != null && s.Send(FrameCodec.Encode(type, payload));
        }

        /// <summary>Moves all pending events into <paramref name="into"/> (call once per frame from the game thread).</summary>
        public void Poll(List<BridgeEvent> into)
        {
            _sink.Drain(into);
        }

        /// <summary>Sends GOODBYE(code, reason) to a connected host, closes and stops retrying; returns within about a second.</summary>
        public void Shutdown(ushort code, string reason)
        {
            Session s;
            Socket c;
            lock (_sync)
            {
                if (_stopping) return;
                _stopping = true;
                s = _active;
                c = _connecting;
                Monitor.PulseAll(_sync);
            }
            _sink.SetState(BridgeState.Closing, "");
            if (c != null) try { c.Close(); } catch (Exception) { }
            if (s != null) s.Shutdown(code, reason);
            _sink.SetState(BridgeState.Disconnected, "");
        }

        private bool Stopping { get { lock (_sync) return _stopping; } }

        private void WaitMs(int ms)
        {
            long end = Clock.NowMs() + ms;
            lock (_sync)
            {
                while (!_stopping)
                {
                    long left = end - Clock.NowMs();
                    if (left <= 0) return;
                    Monitor.Wait(_sync, (int)left);
                }
            }
        }

        private void Loop()
        {
            int attempt = 0;
            while (!Stopping)
            {
                int delay = _o.Retry.GetDelayMs(attempt++);
                _sink.SetState(BridgeState.Connecting, "127.0.0.1:" + _o.Port);
                Socket s = Connect();
                if (s == null) { WaitMs(delay); continue; }
                Session session;
                int outcome = Handshake(s, out session);
                if (outcome == Fatal) return;
                if (outcome == Established)
                {
                    attempt = 0;
                    session.Run();
                    delay = _o.Retry.GetDelayMs(0);
                }
                if (!Stopping) _sink.SetState(BridgeState.Disconnected, "");
                WaitMs(delay);
            }
        }

        private Socket Connect()
        {
            var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            lock (_sync)
            {
                if (_stopping) { s.Close(); return null; }
                _connecting = s;
            }
            try
            {
                s.NoDelay = true;
                s.Connect(new IPEndPoint(IPAddress.Loopback, _o.Port));
                return s;
            }
            catch (Exception e)
            {
                _log("connect failed: " + e.Message);
                lock (_sync) _connecting = null;
                try { s.Close(); } catch (Exception) { }
                return null;
            }
        }

        private int Handshake(Socket s, out Session session)
        {
            session = null;
            try
            {
                var hello = new Hello
                {
                    AppProtocol = _o.AppProtocol, AppMajor = _o.AppMajor, AppMinor = _o.AppMinor,
                    PeerName = _o.PeerName, PeerVersion = _o.PeerVersion,
                    SessionNonce = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0)
                };
                SocketUtil.SendAll(s, FrameCodec.Encode(MessageTypes.Hello, hello.Encode()));
                _sink.SetState(BridgeState.Handshaking, "");

                Frame f;
                try
                {
                    f = FrameCodec.Read(s, Clock.NowMs() + _o.HandshakeTimeoutMs);
                }
                catch (TimeoutException)
                {
                    return Lost(s, "no WELCOME within " + _o.HandshakeTimeoutMs + " ms");
                }
                if (f == null) return Lost(s, "connection closed during handshake");
                if (f.Type == MessageTypes.Goodbye)
                {
                    Goodbye g = Goodbye.Decode(f.Payload);
                    return End(s, DisconnectCause.PeerGoodbye, g.Code, g.Reason, Failed);
                }
                if (f.Type != MessageTypes.Welcome) throw new ProtocolException("expected WELCOME, got 0x" + f.Type.ToString("x4"));
                Welcome w = Welcome.Decode(f.Payload);
                if (!w.Accepted)
                {
                    _sink.SetState(BridgeState.Rejected, w.RejectReason);
                    bool fatal = w.RejectCode >= RejectCodes.BridgeVersion && w.RejectCode <= RejectCodes.AppMajor;
                    return End(s, DisconnectCause.Rejected, w.RejectCode, w.RejectReason, fatal ? Fatal : Failed);
                }
                PeerName = w.PeerName;
                PeerVersion = w.PeerVersion;
                NegotiatedAppMinor = Math.Min(w.AppMinor, _o.AppMinor);
                session = new Session(s, w.HeartbeatIntervalMs == 0 ? 1000 : (int)w.HeartbeatIntervalMs,
                    w.PeerTimeoutMs == 0 ? 5000 : (int)w.PeerTimeoutMs, _o.MaxQueuedBytes, _log, OnMessage, OnEnded);
                lock (_sync)
                {
                    _connecting = null;
                    if (_stopping) { SocketUtil.CloseGracefully(s, 0); session = null; return Fatal; }
                    _active = session;
                }
                _sink.SetState(BridgeState.Connected, "peer='" + w.PeerName + "' version='" + w.PeerVersion + "'");
                return Established;
            }
            catch (ProtocolException e)
            {
                try
                {
                    var g = new Goodbye { Code = GoodbyeCodes.ProtocolError, Reason = e.Message };
                    SocketUtil.SendAll(s, FrameCodec.Encode(MessageTypes.Goodbye, g.Encode()));
                }
                catch (Exception) { }
                return End(s, DisconnectCause.ProtocolError, GoodbyeCodes.ProtocolError, e.Message, Failed);
            }
            catch (Exception e)
            {
                return Lost(s, e.Message);
            }
        }

        private int Lost(Socket s, string why)
        {
            return End(s, DisconnectCause.ConnectionLost, -1, why, Failed);
        }

        private int End(Socket s, DisconnectCause cause, int code, string reason, int outcome)
        {
            lock (_sync) _connecting = null;
            SocketUtil.CloseGracefully(s, 0);
            if (Stopping) return Fatal;
            _log("handshake ended: " + BridgeEvent.NameOf(cause) + " " + code + " " + reason);
            _sink.Add(BridgeEvent.DisconnectedBecause(cause, code, reason));
            return outcome;
        }

        private void OnMessage(ushort type, byte[] payload)
        {
            _sink.Add(BridgeEvent.MessageReceived(type, payload));
        }

        private void OnEnded(DisconnectCause cause, int code, string reason)
        {
            bool stopping;
            lock (_sync)
            {
                _active = null;
                stopping = _stopping;
            }
            _log("session ended: " + BridgeEvent.NameOf(cause) + " " + code + " " + reason);
            _sink.Add(BridgeEvent.DisconnectedBecause(cause, code, reason));
            if (!stopping) _sink.SetState(BridgeState.Disconnected, "");
        }
    }
}
