using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Skylines.Bridge
{
    /// <summary>Settings for <see cref="BridgeHost"/>.</summary>
    public sealed class HostOptions
    {
        /// <summary>TCP port on 127.0.0.1; 0 picks a free port (see <see cref="BridgeHost.Port"/>).</summary>
        public int Port = BridgeConstants.DefaultPort;
        /// <summary>The application protocol name guests must announce.</summary>
        public string AppProtocol = "";
        /// <summary>The application major version guests must announce.</summary>
        public ushort AppMajor = 1;
        /// <summary>The host's application minor version.</summary>
        public ushort AppMinor;
        /// <summary>Human-readable host name sent in WELCOME.</summary>
        public string PeerName = "SKBR host";
        /// <summary>The host mod's version sent in WELCOME.</summary>
        public string PeerVersion = "0";
        /// <summary>Heartbeat interval announced to the guest.</summary>
        public int HeartbeatIntervalMs = 1000;
        /// <summary>Liveness timeout announced to the guest.</summary>
        public int PeerTimeoutMs = 5000;
        /// <summary>How long a connection may take to deliver HELLO.</summary>
        public int HandshakeTimeoutMs = 5000;
        /// <summary>Outbound queue bound in bytes (default 64 MiB).</summary>
        public long MaxQueuedBytes = 64L * 1024 * 1024;
        /// <summary>Receives diagnostic lines from any thread; may be null.</summary>
        public Action<string> Log;
    }

    /// <summary>
    /// SKBR host: listens on loopback, accepts one guest at a time and exchanges frames with it.
    /// All socket work happens on background threads; <see cref="Send"/> and <see cref="Poll"/> never block.
    /// </summary>
    public sealed class BridgeHost
    {
        private readonly HostOptions _o;
        private readonly Action<string> _log;
        private readonly EventSink _sink = new EventSink();
        private readonly object _sync = new object();
        private readonly List<Socket> _pending = new List<Socket>();
        private TcpListener _listener;
        private Session _active;
        private bool _stopping;
        private ulong _nextSessionId;

        /// <summary>Creates a host; call <see cref="Start"/> to listen.</summary>
        public BridgeHost(HostOptions options)
        {
            _o = options;
            Action<string> l = options.Log;
            _log = l ?? (s => { });
            _nextSessionId = (ulong)Clock.NowMs() << 16;
            PeerName = "";
            PeerVersion = "";
        }

        /// <summary>The port actually bound (valid after a successful <see cref="Start"/>).</summary>
        public int Port { get; private set; }

        /// <summary>When false, guests are rejected with NOT_READY. Defaults to true.</summary>
        public volatile bool AcceptGuests = true;

        /// <summary>The current connection state.</summary>
        public BridgeState State { get { return _sink.State; } }

        /// <summary>The connected guest's name (empty when none).</summary>
        public string PeerName { get; private set; }

        /// <summary>The connected guest's version (empty when none).</summary>
        public string PeerVersion { get; private set; }

        /// <summary>min(guest, host) application minor version of the current session.</summary>
        public ushort NegotiatedAppMinor { get; private set; }

        /// <summary>Starts listening on 127.0.0.1. Returns false (and logs) if the port cannot be bound.</summary>
        public bool Start()
        {
            lock (_sync)
            {
                if (_listener != null || _stopping) return false;
                try
                {
                    _listener = new TcpListener(IPAddress.Loopback, _o.Port);
                    _listener.Start(8);
                }
                catch (Exception e)
                {
                    _listener = null;
                    _log("cannot listen on 127.0.0.1:" + _o.Port + ": " + e.Message);
                    return false;
                }
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            }
            _sink.SetState(BridgeState.Listening, "127.0.0.1:" + Port);
            _log("listening on 127.0.0.1:" + Port);
            var t = new Thread(AcceptLoop) { IsBackground = true, Name = "SKBR accept" };
            t.Start();
            return true;
        }

        /// <summary>
        /// Queues an application frame for the connected guest. Never blocks. Returns false when no guest is
        /// connected (the frame is dropped) or the queue bound was exceeded (the session is being closed).
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

        /// <summary>Sends GOODBYE(code, reason) to a connected guest, closes everything and stops listening; returns within about a second.</summary>
        public void Shutdown(ushort code, string reason)
        {
            Session s;
            TcpListener l;
            Socket[] pending;
            lock (_sync)
            {
                if (_stopping) return;
                _stopping = true;
                s = _active;
                l = _listener;
                pending = _pending.ToArray();
            }
            _sink.SetState(BridgeState.Closing, "");
            if (l != null) try { l.Stop(); } catch (Exception) { }
            foreach (Socket p in pending) try { p.Close(); } catch (Exception) { }
            if (s != null) s.Shutdown(code, reason);
            _sink.SetState(BridgeState.Disconnected, "");
        }

        private void AcceptLoop()
        {
            while (true)
            {
                Socket s;
                try
                {
                    s = _listener.AcceptSocket();
                }
                catch (Exception)
                {
                    lock (_sync) if (_stopping) return;
                    Thread.Sleep(50);
                    continue;
                }
                lock (_sync)
                {
                    if (_stopping) { try { s.Close(); } catch (Exception) { } return; }
                    _pending.Add(s);
                }
                var t = new Thread(() => Handshake(s)) { IsBackground = true, Name = "SKBR handshake" };
                t.Start();
            }
        }

        private void Forget(Socket s)
        {
            lock (_sync) _pending.Remove(s);
        }

        private void Abort(Socket s)
        {
            Forget(s);
            SocketUtil.CloseGracefully(s, 0);
        }

        private void AbortWithProtocolError(Socket s, string reason)
        {
            Forget(s);
            try
            {
                var g = new Goodbye { Code = GoodbyeCodes.ProtocolError, Reason = reason };
                SocketUtil.SendAll(s, FrameCodec.Encode(MessageTypes.Goodbye, g.Encode()));
            }
            catch (Exception) { }
            SocketUtil.CloseGracefully(s, 300);
        }

        private void Handshake(Socket s)
        {
            try
            {
                s.NoDelay = true;
                Frame f;
                try
                {
                    f = FrameCodec.Read(s, Clock.NowMs() + _o.HandshakeTimeoutMs);
                }
                catch (TimeoutException) { _log("guest sent no HELLO in time"); Abort(s); return; }
                catch (ProtocolException e) { _log("bad first frame: " + e.Message); AbortWithProtocolError(s, e.Message); return; }
                if (f == null || f.Type == MessageTypes.Goodbye) { Abort(s); return; }
                if (f.Type != MessageTypes.Hello) { AbortWithProtocolError(s, "expected HELLO"); return; }
                if (f.Payload.Length < 4 || new PayloadReader(f.Payload).U32() != BridgeConstants.Magic)
                {
                    _log("wrong magic; closing without reply");
                    Abort(s);
                    return;
                }
                Hello h;
                try { h = Hello.Decode(f.Payload); }
                catch (ProtocolException e) { AbortWithProtocolError(s, e.Message); return; }
                HandleHello(s, h);
            }
            catch (Exception e)
            {
                _log("handshake failed: " + e.Message);
                Abort(s);
            }
        }

        private void HandleHello(Socket s, Hello h)
        {
            ushort code = 0;
            string why = "";
            Session session = null;
            if (h.BridgeVersion != BridgeConstants.BridgeVersion) { code = RejectCodes.BridgeVersion; why = "bridge version " + h.BridgeVersion + " != " + BridgeConstants.BridgeVersion; }
            else if (h.AppProtocol != _o.AppProtocol) { code = RejectCodes.AppProtocol; why = "app protocol '" + h.AppProtocol + "' != '" + _o.AppProtocol + "'"; }
            else if (h.AppMajor != _o.AppMajor) { code = RejectCodes.AppMajor; why = "app major " + h.AppMajor + " != " + _o.AppMajor; }
            else if (!AcceptGuests) { code = RejectCodes.NotReady; why = "the host is not accepting guests right now"; }
            else
            {
                lock (_sync)
                {
                    if (_stopping) { code = RejectCodes.NotReady; why = "the host is shutting down"; }
                    else if (_active != null) { code = RejectCodes.Busy; why = "another guest is connected"; }
                    else
                    {
                        session = new Session(s, _o.HeartbeatIntervalMs, _o.PeerTimeoutMs, _o.MaxQueuedBytes, _log, OnMessage, OnEnded);
                        _active = session;
                    }
                }
            }

            var w = new Welcome
            {
                Accepted = session != null, RejectCode = code, RejectReason = why,
                AppProtocol = _o.AppProtocol, AppMajor = _o.AppMajor, AppMinor = _o.AppMinor,
                PeerName = _o.PeerName, PeerVersion = _o.PeerVersion,
                HeartbeatIntervalMs = (uint)_o.HeartbeatIntervalMs, PeerTimeoutMs = (uint)_o.PeerTimeoutMs
            };
            if (session == null)
            {
                _log("rejected guest: code " + code + " (" + why + ")");
                try { SocketUtil.SendAll(s, FrameCodec.Encode(MessageTypes.Welcome, w.Encode())); } catch (Exception) { }
                Abort(s);
                return;
            }

            PeerName = h.PeerName;
            PeerVersion = h.PeerVersion;
            NegotiatedAppMinor = Math.Min(h.AppMinor, _o.AppMinor);
            _sink.SetState(BridgeState.Handshaking, h.PeerName);
            w.SessionId = ++_nextSessionId | 1;
            try
            {
                SocketUtil.SendAll(s, FrameCodec.Encode(MessageTypes.Welcome, w.Encode()));
            }
            catch (Exception)
            {
                Forget(s);
                session.Terminate(DisconnectCause.ConnectionLost, -1, "WELCOME not delivered", -1);
                return;
            }
            Forget(s);
            _sink.SetState(BridgeState.Connected, "peer='" + h.PeerName + "' version='" + h.PeerVersion + "'");
            session.Run();
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
            if (!stopping) _sink.SetState(BridgeState.Listening, "");
        }
    }
}
