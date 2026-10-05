using System.Collections.Generic;

namespace Skylines.Bridge
{
    /// <summary>Connection states reported by <see cref="BridgeEventKind.StateChanged"/>.</summary>
    public enum BridgeState
    {
        /// <summary>Not connected and not trying.</summary>
        Disconnected,
        /// <summary>Host only: waiting for a guest.</summary>
        Listening,
        /// <summary>Guest only: trying to reach the host.</summary>
        Connecting,
        /// <summary>HELLO/WELCOME in progress.</summary>
        Handshaking,
        /// <summary>Handshake complete; application frames flow.</summary>
        Connected,
        /// <summary>Guest only: the host rejected the guest.</summary>
        Rejected,
        /// <summary>Shutdown in progress.</summary>
        Closing
    }

    /// <summary>Why a session ended.</summary>
    public enum DisconnectCause
    {
        /// <summary>The peer sent GOODBYE.</summary>
        PeerGoodbye,
        /// <summary>This side called Shutdown.</summary>
        LocalGoodbye,
        /// <summary>The peer was silent for the liveness timeout.</summary>
        Timeout,
        /// <summary>A frame violated the protocol.</summary>
        ProtocolError,
        /// <summary>The connection broke without GOODBYE.</summary>
        ConnectionLost,
        /// <summary>The host rejected the guest (code is the reject code).</summary>
        Rejected,
        /// <summary>The outbound queue overflowed.</summary>
        Backpressure
    }

    /// <summary>The three kinds of events an application drains with Poll.</summary>
    public enum BridgeEventKind
    {
        /// <summary>The connection state changed.</summary>
        StateChanged,
        /// <summary>An application frame arrived.</summary>
        Message,
        /// <summary>A session ended or a handshake was refused.</summary>
        Disconnected
    }

    /// <summary>An event delivered to the application thread. Only the fields of its <see cref="Kind"/> are meaningful.</summary>
    public sealed class BridgeEvent
    {
        /// <summary>Which kind of event this is.</summary>
        public BridgeEventKind Kind { get; private set; }
        /// <summary>StateChanged: the new state.</summary>
        public BridgeState State { get; private set; }
        /// <summary>StateChanged: optional detail (peer name, reject reason).</summary>
        public string Detail { get; private set; }
        /// <summary>Message: the application message type (at least 0x0100).</summary>
        public ushort MessageType { get; private set; }
        /// <summary>Message: the payload, unchanged.</summary>
        public byte[] Payload { get; private set; }
        /// <summary>Disconnected: why.</summary>
        public DisconnectCause Cause { get; private set; }
        /// <summary>Disconnected: GOODBYE or reject code, or -1 when there is none.</summary>
        public int Code { get; private set; }
        /// <summary>Disconnected: GOODBYE or reject reason, possibly empty.</summary>
        public string Reason { get; private set; }

        /// <summary>The spec's lowercase state name, e.g. "connected".</summary>
        public string StateName { get { return NameOf(State); } }

        /// <summary>The spec's snake_case cause name, e.g. "peer_goodbye".</summary>
        public string CauseName { get { return NameOf(Cause); } }

        internal static BridgeEvent StateChanged(BridgeState state, string detail)
        {
            return new BridgeEvent { Kind = BridgeEventKind.StateChanged, State = state, Detail = detail ?? "" };
        }

        internal static BridgeEvent MessageReceived(ushort type, byte[] payload)
        {
            return new BridgeEvent { Kind = BridgeEventKind.Message, MessageType = type, Payload = payload };
        }

        internal static BridgeEvent DisconnectedBecause(DisconnectCause cause, int code, string reason)
        {
            return new BridgeEvent { Kind = BridgeEventKind.Disconnected, Cause = cause, Code = code, Reason = reason ?? "" };
        }

        /// <summary>The spec's lowercase name of a state.</summary>
        public static string NameOf(BridgeState s)
        {
            return s.ToString().ToLowerInvariant();
        }

        /// <summary>The spec's snake_case name of a cause.</summary>
        public static string NameOf(DisconnectCause c)
        {
            switch (c)
            {
                case DisconnectCause.PeerGoodbye: return "peer_goodbye";
                case DisconnectCause.LocalGoodbye: return "local_goodbye";
                case DisconnectCause.Timeout: return "timeout";
                case DisconnectCause.ProtocolError: return "protocol_error";
                case DisconnectCause.ConnectionLost: return "connection_lost";
                case DisconnectCause.Rejected: return "rejected";
                default: return "backpressure";
            }
        }
    }

    /// <summary>Thread-safe event queue plus state tracking shared by host and guest.</summary>
    internal sealed class EventSink
    {
        private readonly object _sync = new object();
        private readonly List<BridgeEvent> _events = new List<BridgeEvent>();
        private BridgeState _state = BridgeState.Disconnected;

        public BridgeState State { get { lock (_sync) return _state; } }

        /// <summary>Records a state change; repeated identical states produce no event.</summary>
        public void SetState(BridgeState state, string detail)
        {
            lock (_sync)
            {
                if (state == _state) return;
                _state = state;
                _events.Add(BridgeEvent.StateChanged(state, detail));
            }
        }

        public void Add(BridgeEvent e)
        {
            lock (_sync) _events.Add(e);
        }

        public void Drain(List<BridgeEvent> into)
        {
            lock (_sync)
            {
                into.AddRange(_events);
                _events.Clear();
            }
        }
    }
}
