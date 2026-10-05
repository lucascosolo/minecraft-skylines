using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Skylines.Bridge.Tests
{
    public class FrameVectorTests
    {
        public static IEnumerable<object[]> Valid()
        {
            using (JsonDocument d = VectorFiles.Load("frames.json"))
                foreach (JsonElement v in d.RootElement.GetProperty("valid").EnumerateArray())
                    yield return new object[] { v.GetProperty("name").GetString() };
        }

        public static IEnumerable<object[]> Invalid()
        {
            using (JsonDocument d = VectorFiles.Load("frames.json"))
                foreach (JsonElement v in d.RootElement.GetProperty("invalid").EnumerateArray())
                    yield return new object[] { v.GetProperty("name").GetString() };
        }

        private static JsonElement Find(JsonDocument d, string list, string name)
        {
            foreach (JsonElement v in d.RootElement.GetProperty(list).EnumerateArray())
                if (v.GetProperty("name").GetString() == name) return v.Clone();
            throw new InvalidOperationException(name);
        }

        [Theory]
        [MemberData(nameof(Valid))]
        public void ValidVectorDecodesToFieldsAndEncodesToHex(string name)
        {
            JsonElement v;
            using (JsonDocument d = VectorFiles.Load("frames.json")) v = Find(d, "valid", name);
            byte[] hex = VectorFiles.Hex(v.GetProperty("hex").GetString());
            JsonElement f = v.GetProperty("fields");
            ushort type = v.GetProperty("type").GetUInt16();

            Frame frame = FrameCodec.Decode(hex);
            Assert.Equal(type, frame.Type);
            Assert.Equal(0, frame.Flags);

            byte[] payload;
            switch (type)
            {
                case MessageTypes.Hello:
                    {
                        Hello m = Hello.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("magic").GetUInt32(), m.Magic);
                        Assert.Equal(f.GetProperty("bridgeVersion").GetUInt16(), m.BridgeVersion);
                        Assert.Equal(f.GetProperty("appProtocol").GetString(), m.AppProtocol);
                        Assert.Equal(f.GetProperty("appMajor").GetUInt16(), m.AppMajor);
                        Assert.Equal(f.GetProperty("appMinor").GetUInt16(), m.AppMinor);
                        Assert.Equal(f.GetProperty("peerName").GetString(), m.PeerName);
                        Assert.Equal(f.GetProperty("peerVersion").GetString(), m.PeerVersion);
                        Assert.Equal(ulong.Parse(f.GetProperty("sessionNonce").GetString()), m.SessionNonce);
                        payload = m.Encode();
                        break;
                    }
                case MessageTypes.Welcome:
                    {
                        Welcome m = Welcome.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("accepted").GetBoolean(), m.Accepted);
                        Assert.Equal(f.GetProperty("rejectCode").GetUInt16(), m.RejectCode);
                        Assert.Equal(f.GetProperty("rejectReason").GetString(), m.RejectReason);
                        Assert.Equal(f.GetProperty("bridgeVersion").GetUInt16(), m.BridgeVersion);
                        Assert.Equal(f.GetProperty("appProtocol").GetString(), m.AppProtocol);
                        Assert.Equal(f.GetProperty("appMajor").GetUInt16(), m.AppMajor);
                        Assert.Equal(f.GetProperty("appMinor").GetUInt16(), m.AppMinor);
                        Assert.Equal(f.GetProperty("peerName").GetString(), m.PeerName);
                        Assert.Equal(f.GetProperty("peerVersion").GetString(), m.PeerVersion);
                        Assert.Equal(f.GetProperty("heartbeatIntervalMs").GetUInt32(), m.HeartbeatIntervalMs);
                        Assert.Equal(f.GetProperty("peerTimeoutMs").GetUInt32(), m.PeerTimeoutMs);
                        Assert.Equal(ulong.Parse(f.GetProperty("sessionId").GetString()), m.SessionId);
                        payload = m.Encode();
                        break;
                    }
                case MessageTypes.Heartbeat:
                    {
                        Heartbeat m = Heartbeat.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("seq").GetUInt32(), m.Seq);
                        Assert.Equal(ulong.Parse(f.GetProperty("senderUptimeMs").GetString()), m.SenderUptimeMs);
                        payload = m.Encode();
                        break;
                    }
                case MessageTypes.Goodbye:
                    {
                        Goodbye m = Goodbye.Decode(frame.Payload);
                        Assert.Equal(f.GetProperty("code").GetUInt16(), m.Code);
                        Assert.Equal(f.GetProperty("reason").GetString(), m.Reason);
                        payload = m.Encode();
                        break;
                    }
                default:
                    // Application frames are opaque to the bridge: payload passes through unchanged.
                    Assert.True(type >= BridgeConstants.AppTypeMin);
                    payload = frame.Payload;
                    break;
            }

            if (name.Contains("trailing"))
            {
                // Trailing bytes are accepted on decode; encoding the fields yields only the defined prefix.
                Assert.True(frame.Payload.Length > payload.Length);
                Assert.Equal(payload, frame.Payload[..payload.Length]);
            }
            else
            {
                Assert.Equal(v.GetProperty("hex").GetString(), VectorFiles.ToHex(FrameCodec.Encode(type, payload)));
            }
        }

        [Theory]
        [MemberData(nameof(Invalid))]
        public void InvalidVectorRaisesProtocolException(string name)
        {
            JsonElement v;
            using (JsonDocument d = VectorFiles.Load("frames.json")) v = Find(d, "invalid", name);
            byte[] hex = VectorFiles.Hex(v.GetProperty("hex").GetString());

            Assert.Throws<ProtocolException>(() =>
            {
                Frame frame = FrameCodec.Decode(hex);
                switch (frame.Type)
                {
                    case MessageTypes.Hello: Hello.Decode(frame.Payload); break;
                    case MessageTypes.Welcome: Welcome.Decode(frame.Payload); break;
                    case MessageTypes.Heartbeat: Heartbeat.Decode(frame.Payload); break;
                    case MessageTypes.Goodbye: Goodbye.Decode(frame.Payload); break;
                }
            });
        }

        [Fact]
        public void UuidUsesRfc4122ByteOrder()
        {
            var g = new Guid("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");
            Assert.Equal("0f1e2d3c4b5a69788796a5b4c3d2e1f0", VectorFiles.ToHex(Uuid.ToBytes(g)));
            Assert.Equal(g, Uuid.FromBytes(Uuid.ToBytes(g)));
        }

        [Fact]
        public void StreamReadRejectsOversizeWithoutReadingPayload()
        {
            byte[] header = VectorFiles.Hex("0100000103000000");
            ushort type, flags;
            Assert.Throws<ProtocolException>(() => FrameCodec.ParseHeader(header, out type, out flags));
        }
    }
}
