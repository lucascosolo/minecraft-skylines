using System;
using System.IO;
using System.Text.Json;
using Skylines.Bridge;
using Xunit;

namespace MinecraftSkylines.Protocol.Tests
{
    public class PlayerDataTests
    {
        private static JsonElement Vector(string list, string name)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string p = Path.Combine(dir.FullName, "protocol", "vectors", "frames.json");
                if (File.Exists(p))
                {
                    using (JsonDocument d = JsonDocument.Parse(File.ReadAllText(p)))
                        foreach (JsonElement v in d.RootElement.GetProperty(list).EnumerateArray())
                            if (v.GetProperty("name").GetString() == name) return v.Clone();
                    throw new InvalidOperationException(name);
                }
                dir = dir.Parent;
            }
            throw new FileNotFoundException("protocol/vectors/frames.json");
        }

        private static byte[] Hex(string h)
        {
            var b = new byte[h.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(2 * i, 2), 16);
            return b;
        }

        private static byte[] Payload(string list, string name)
        {
            return FrameCodec.Decode(Hex(Vector(list, name).GetProperty("hex").GetString())).Payload;
        }

        [Fact]
        public void ConstantsMatchSpec()
        {
            Assert.Equal(14, AppProtocol.Minor);
            Assert.Equal(0x01B0, AppProtocol.PlayerDataType);
            Assert.Equal(0x01B1, AppProtocol.RespawnRequestType);
            Assert.Equal(4194304, PlayerData.MaxLength);
        }

        [Theory]
        [InlineData("player_data")]
        [InlineData("player_data_fresh")]
        public void PlayerDataVectorRoundTrips(string name)
        {
            JsonElement f = Vector("valid", name).GetProperty("fields");
            byte[] payload = Payload("valid", name);
            PlayerData m = PlayerData.Decode(payload);
            Assert.Equal(f.GetProperty("openSeq").GetUInt32(), m.OpenSeq);
            Assert.Equal(Hex(f.GetProperty("dataHex").GetString()), m.Data);
            Assert.Equal(payload, m.Encode());
        }

        [Fact]
        public void RespawnRequestVectorRoundTrips()
        {
            JsonElement v = Vector("valid", "respawn_request");
            byte[] payload = Payload("valid", "respawn_request");
            RespawnRequest m = RespawnRequest.Decode(payload);
            Assert.Equal(v.GetProperty("fields").GetProperty("openSeq").GetUInt32(), m.OpenSeq);
            Assert.Equal(payload, m.Encode());
        }

        [Theory]
        [InlineData("player_data_too_long")]
        [InlineData("player_data_overruns")]
        public void InvalidVectorsThrow(string name)
        {
            byte[] p = Payload("invalid", name);
            Assert.Throws<ProtocolException>(() => PlayerData.Decode(p));
        }

        [Theory]
        [InlineData("player_data")]
        [InlineData("player_data_fresh")]
        public void EveryPlayerDataTruncationThrows(string name)
        {
            byte[] full = Payload("valid", name);
            for (int len = 0; len < full.Length; len++)
            {
                var cut = new byte[len];
                Array.Copy(full, cut, len);
                Assert.Throws<ProtocolException>(() => PlayerData.Decode(cut));
            }
        }

        [Fact]
        public void EveryRespawnRequestTruncationThrows()
        {
            byte[] full = Payload("valid", "respawn_request");
            for (int len = 0; len < full.Length; len++)
            {
                var cut = new byte[len];
                Array.Copy(full, cut, len);
                Assert.Throws<ProtocolException>(() => RespawnRequest.Decode(cut));
            }
        }

        [Fact]
        public void TrailingBytesAfterDataAreIgnored()
        {
            var m = new PlayerData { OpenSeq = 9, Data = new byte[] { 1, 2, 3 } };
            byte[] enc = m.Encode();
            var padded = new byte[enc.Length + 5];
            Array.Copy(enc, padded, enc.Length);
            PlayerData d = PlayerData.Decode(padded);
            Assert.Equal(9u, d.OpenSeq);
            Assert.Equal(new byte[] { 1, 2, 3 }, d.Data);
        }

        [Fact]
        public void MaxLengthIsAcceptedAndOneMoreIsRefused()
        {
            var ok = new PlayerData { OpenSeq = 1, Data = new byte[PlayerData.MaxLength] };
            Assert.Equal(PlayerData.MaxLength, PlayerData.Decode(ok.Encode()).Data.Length);
            var big = new PlayerData { OpenSeq = 1, Data = new byte[PlayerData.MaxLength + 1] };
            Assert.Throws<ArgumentException>(() => big.Encode());
        }
    }
}
