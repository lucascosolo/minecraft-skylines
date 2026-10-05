using System;
using System.Net.Sockets;

namespace Skylines.Bridge
{
    /// <summary>One decoded frame: the 8-byte header fields and the payload.</summary>
    public sealed class Frame
    {
        /// <summary>Message type.</summary>
        public ushort Type;
        /// <summary>Header flags (always 0 once validated).</summary>
        public ushort Flags;
        /// <summary>Payload bytes.</summary>
        public byte[] Payload;
    }

    /// <summary>Frame header encoding, decoding and validation.</summary>
    public static class FrameCodec
    {
        /// <summary>Size of the frame header in bytes.</summary>
        public const int HeaderSize = 8;

        /// <summary>Encodes a complete frame (flags 0).</summary>
        public static byte[] Encode(ushort type, byte[] payload)
        {
            return Encode(type, 0, payload);
        }

        /// <summary>Encodes a complete frame with explicit flags (for tests of the receiving side).</summary>
        public static byte[] Encode(ushort type, ushort flags, byte[] payload)
        {
            payload = payload ?? new byte[0];
            if (payload.Length > BridgeConstants.MaxPayload) throw new ArgumentException("payload larger than 16 MiB");
            var f = new byte[HeaderSize + payload.Length];
            uint n = (uint)payload.Length;
            f[0] = (byte)n; f[1] = (byte)(n >> 8); f[2] = (byte)(n >> 16); f[3] = (byte)(n >> 24);
            f[4] = (byte)type; f[5] = (byte)(type >> 8);
            f[6] = (byte)flags; f[7] = (byte)(flags >> 8);
            Array.Copy(payload, 0, f, HeaderSize, payload.Length);
            return f;
        }

        /// <summary>
        /// Validates the length and type of an 8-byte header (oversize and unknown bridge types are
        /// protocol errors) and returns the payload length. Flags are checked by <see cref="CheckFlags"/>
        /// after the payload has been consumed.
        /// </summary>
        public static int ParseHeader(byte[] header, out ushort type, out ushort flags)
        {
            uint len = (uint)(header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24));
            type = (ushort)(header[4] | (header[5] << 8));
            flags = (ushort)(header[6] | (header[7] << 8));
            if (len > BridgeConstants.MaxPayload) throw new ProtocolException("payloadLength " + len + " exceeds 16 MiB");
            if (type < BridgeConstants.AppTypeMin && type != MessageTypes.Hello && type != MessageTypes.Welcome
                && type != MessageTypes.Heartbeat && type != MessageTypes.Goodbye)
                throw new ProtocolException("unknown bridge message type 0x" + type.ToString("x4"));
            return (int)len;
        }

        /// <summary>Throws <see cref="ProtocolException"/> if <paramref name="flags"/> is non-zero.</summary>
        public static void CheckFlags(ushort flags)
        {
            if (flags != 0) throw new ProtocolException("non-zero flags 0x" + flags.ToString("x4"));
        }

        /// <summary>
        /// Decodes exactly one frame from <paramref name="bytes"/> (header plus payload) and validates it.
        /// Message-level decoding is done by the message classes.
        /// </summary>
        public static Frame Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < HeaderSize) throw new ProtocolException("frame shorter than its header");
            var header = new byte[HeaderSize];
            Array.Copy(bytes, header, HeaderSize);
            ushort type, flags;
            int len = ParseHeader(header, out type, out flags);
            if (bytes.Length - HeaderSize != len) throw new ProtocolException("frame length does not match payloadLength");
            CheckFlags(flags);
            var payload = new byte[len];
            Array.Copy(bytes, HeaderSize, payload, 0, len);
            return new Frame { Type = type, Flags = flags, Payload = payload };
        }

        /// <summary>
        /// Reads and validates one frame from a blocking socket. Returns null when the peer closed the
        /// connection. <paramref name="deadlineMs"/> is a <see cref="Clock.NowMs"/> value after which the
        /// read throws <see cref="TimeoutException"/>; 0 means no deadline.
        /// </summary>
        internal static Frame Read(Socket s, long deadlineMs)
        {
            var header = new byte[HeaderSize];
            if (!ReceiveExact(s, header, deadlineMs)) return null;
            ushort type, flags;
            int len = ParseHeader(header, out type, out flags);
            var payload = new byte[len];
            if (len > 0 && !ReceiveExact(s, payload, deadlineMs)) return null;
            CheckFlags(flags);
            return new Frame { Type = type, Flags = flags, Payload = payload };
        }

        private static bool ReceiveExact(Socket s, byte[] buf, long deadlineMs)
        {
            int got = 0;
            while (got < buf.Length)
            {
                if (deadlineMs != 0)
                {
                    long left = deadlineMs - Clock.NowMs();
                    if (left <= 0) throw new TimeoutException();
                    s.ReceiveTimeout = (int)Math.Min(left, int.MaxValue);
                }
                int n;
                try
                {
                    n = s.Receive(buf, got, buf.Length - got, SocketFlags.None);
                }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.TimedOut) throw new TimeoutException();
                    throw;
                }
                if (n == 0) return false;
                got += n;
            }
            return true;
        }
    }
}
