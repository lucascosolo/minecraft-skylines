using System;

namespace Skylines.Bridge
{
    /// <summary>Bridge-level constants from the SKBR spec.</summary>
    public static class BridgeConstants
    {
        /// <summary>HELLO magic, the bytes "SKBR".</summary>
        public const uint Magic = 0x52424B53;
        /// <summary>The bridge protocol version implemented here.</summary>
        public const ushort BridgeVersion = 1;
        /// <summary>Default TCP port.</summary>
        public const int DefaultPort = 47615;
        /// <summary>First application message type; lower types belong to the bridge.</summary>
        public const ushort AppTypeMin = 0x0100;
        /// <summary>Maximum payload length in bytes (16 MiB).</summary>
        public const int MaxPayload = 16777216;
    }

    /// <summary>Bridge message types.</summary>
    public static class MessageTypes
    {
        /// <summary>guest to host, first frame.</summary>
        public const ushort Hello = 0x0001;
        /// <summary>host to guest, reply to HELLO.</summary>
        public const ushort Welcome = 0x0002;
        /// <summary>Both directions, after the handshake.</summary>
        public const ushort Heartbeat = 0x0003;
        /// <summary>Both directions.</summary>
        public const ushort Goodbye = 0x0004;
    }

    /// <summary>GOODBYE codes.</summary>
    public static class GoodbyeCodes
    {
        /// <summary>The user or the app ended the session.</summary>
        public const ushort Normal = 0;
        /// <summary>The game is exiting or the mod is being disabled.</summary>
        public const ushort ShuttingDown = 1;
        /// <summary>The sender received something invalid.</summary>
        public const ushort ProtocolError = 2;
        /// <summary>The sender stopped hearing from the peer.</summary>
        public const ushort Timeout = 3;
        /// <summary>The sender's outbound queue overflowed.</summary>
        public const ushort Backpressure = 4;
    }

    /// <summary>WELCOME reject codes.</summary>
    public static class RejectCodes
    {
        /// <summary>bridgeVersion differs.</summary>
        public const ushort BridgeVersion = 1;
        /// <summary>appProtocol differs.</summary>
        public const ushort AppProtocol = 2;
        /// <summary>appMajor differs.</summary>
        public const ushort AppMajor = 3;
        /// <summary>Another guest is connected.</summary>
        public const ushort Busy = 4;
        /// <summary>The host refuses guests right now.</summary>
        public const ushort NotReady = 5;
    }

    /// <summary>HELLO, guest to host.</summary>
    public sealed class Hello
    {
        /// <summary>Must equal <see cref="BridgeConstants.Magic"/>.</summary>
        public uint Magic = BridgeConstants.Magic;
        /// <summary>Bridge protocol version.</summary>
        public ushort BridgeVersion = BridgeConstants.BridgeVersion;
        /// <summary>Application protocol name.</summary>
        public string AppProtocol = "";
        /// <summary>Application major version.</summary>
        public ushort AppMajor;
        /// <summary>Application minor version.</summary>
        public ushort AppMinor;
        /// <summary>Human-readable peer name.</summary>
        public string PeerName = "";
        /// <summary>The guest mod's version.</summary>
        public string PeerVersion = "";
        /// <summary>Random per connection attempt.</summary>
        public ulong SessionNonce;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            return new PayloadWriter().U32(Magic).U16(BridgeVersion).String(AppProtocol).U16(AppMajor).U16(AppMinor)
                .String(PeerName).String(PeerVersion).U64(SessionNonce).ToArray();
        }

        /// <summary>Decodes a payload; does not check the magic value.</summary>
        public static Hello Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var h = new Hello();
            h.Magic = r.U32();
            h.BridgeVersion = r.U16();
            h.AppProtocol = r.String();
            h.AppMajor = r.U16();
            h.AppMinor = r.U16();
            h.PeerName = r.String();
            h.PeerVersion = r.String();
            h.SessionNonce = r.U64();
            return h;
        }
    }

    /// <summary>WELCOME, host to guest.</summary>
    public sealed class Welcome
    {
        /// <summary>Whether the guest was accepted.</summary>
        public bool Accepted;
        /// <summary>0 when accepted, else a <see cref="RejectCodes"/> value.</summary>
        public ushort RejectCode;
        /// <summary>Empty when accepted; human-readable otherwise.</summary>
        public string RejectReason = "";
        /// <summary>The host's bridge version.</summary>
        public ushort BridgeVersion = BridgeConstants.BridgeVersion;
        /// <summary>The host's application protocol name.</summary>
        public string AppProtocol = "";
        /// <summary>The host's application major version.</summary>
        public ushort AppMajor;
        /// <summary>The host's application minor version.</summary>
        public ushort AppMinor;
        /// <summary>Human-readable host name.</summary>
        public string PeerName = "";
        /// <summary>The host mod's version.</summary>
        public string PeerVersion = "";
        /// <summary>Interval at which each side sends HEARTBEAT.</summary>
        public uint HeartbeatIntervalMs = 1000;
        /// <summary>Silence after which a peer is considered dead.</summary>
        public uint PeerTimeoutMs = 5000;
        /// <summary>Host-assigned, non-zero when accepted.</summary>
        public ulong SessionId;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            return new PayloadWriter().Bool(Accepted).U16(RejectCode).String(RejectReason).U16(BridgeVersion)
                .String(AppProtocol).U16(AppMajor).U16(AppMinor).String(PeerName).String(PeerVersion)
                .U32(HeartbeatIntervalMs).U32(PeerTimeoutMs).U64(SessionId).ToArray();
        }

        /// <summary>Decodes a payload.</summary>
        public static Welcome Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var w = new Welcome();
            w.Accepted = r.Bool();
            w.RejectCode = r.U16();
            w.RejectReason = r.String();
            w.BridgeVersion = r.U16();
            w.AppProtocol = r.String();
            w.AppMajor = r.U16();
            w.AppMinor = r.U16();
            w.PeerName = r.String();
            w.PeerVersion = r.String();
            w.HeartbeatIntervalMs = r.U32();
            w.PeerTimeoutMs = r.U32();
            w.SessionId = r.U64();
            return w;
        }
    }

    /// <summary>HEARTBEAT, both directions.</summary>
    public sealed class Heartbeat
    {
        /// <summary>Per-sender counter starting at 1.</summary>
        public uint Seq;
        /// <summary>Monotonic milliseconds since the sender's bridge started.</summary>
        public ulong SenderUptimeMs;

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            return new PayloadWriter().U32(Seq).U64(SenderUptimeMs).ToArray();
        }

        /// <summary>Decodes a payload.</summary>
        public static Heartbeat Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var h = new Heartbeat();
            h.Seq = r.U32();
            h.SenderUptimeMs = r.U64();
            return h;
        }
    }

    /// <summary>GOODBYE, both directions.</summary>
    public sealed class Goodbye
    {
        /// <summary>A <see cref="GoodbyeCodes"/> value.</summary>
        public ushort Code;
        /// <summary>Human-readable reason.</summary>
        public string Reason = "";

        /// <summary>Encodes the payload.</summary>
        public byte[] Encode()
        {
            return new PayloadWriter().U16(Code).String(Reason).ToArray();
        }

        /// <summary>Decodes a payload.</summary>
        public static Goodbye Decode(byte[] payload)
        {
            var r = new PayloadReader(payload);
            var g = new Goodbye();
            g.Code = r.U16();
            g.Reason = r.String();
            return g;
        }
    }
}
