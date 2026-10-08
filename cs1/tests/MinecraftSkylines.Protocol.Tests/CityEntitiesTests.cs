using System;
using System.IO;
using System.Text.Json;
using Skylines.Bridge;
using Xunit;

namespace MinecraftSkylines.Protocol.Tests
{
    public class CityEntitiesTests
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
            Assert.Equal(21, AppProtocol.Minor);
            Assert.Equal(0x01D0, AppProtocol.CityEntitiesType);
            Assert.Equal(0x01D1, AppProtocol.CityFocusType);
            Assert.Equal(4194304, CityEntities.MaxLength);
            Assert.Equal((byte)1, CityFocus.Active);
        }

        [Theory]
        [InlineData("city_entities")]
        [InlineData("city_entities_empty")]
        public void CityEntitiesVectorRoundTrips(string name)
        {
            JsonElement f = Vector("valid", name).GetProperty("fields");
            byte[] payload = Payload("valid", name);
            CityEntities m = CityEntities.Decode(payload);
            Assert.Equal(f.GetProperty("openSeq").GetUInt32(), m.OpenSeq);
            Assert.Equal(Hex(f.GetProperty("dataHex").GetString()), m.Data);
            Assert.Equal(payload, m.Encode());
        }

        [Theory]
        [InlineData("city_entities_too_long")]
        [InlineData("city_entities_overruns")]
        public void InvalidCityEntitiesThrow(string name)
        {
            byte[] p = Payload("invalid", name);
            Assert.Throws<ProtocolException>(() => CityEntities.Decode(p));
        }

        [Fact]
        public void CityEntitiesOverMaxLengthRefusedOnEncode()
        {
            var big = new CityEntities { OpenSeq = 1, Data = new byte[CityEntities.MaxLength + 1] };
            Assert.Throws<ArgumentException>(() => big.Encode());
        }

        [Theory]
        [InlineData("city_focus_active")]
        [InlineData("city_focus_off")]
        public void CityFocusVectorRoundTrips(string name)
        {
            JsonElement f = Vector("valid", name).GetProperty("fields");
            byte[] payload = Payload("valid", name);
            CityFocus m = CityFocus.Decode(payload);
            Assert.Equal((float)f.GetProperty("x").GetDouble(), m.X);
            Assert.Equal((float)f.GetProperty("z").GetDouble(), m.Z);
            Assert.Equal(f.GetProperty("flags").GetByte(), m.Flags);
            Assert.Equal(m.Flags == 1, m.IsActive);
            Assert.Equal(9, payload.Length);
            Assert.Equal(payload, m.Encode());
        }

        [Theory]
        [InlineData("city_focus_short")]
        [InlineData("city_focus_long")]
        [InlineData("city_focus_active_nan")]
        public void InvalidCityFocusThrow(string name)
        {
            byte[] p = Payload("invalid", name);
            Assert.Throws<ProtocolException>(() => CityFocus.Decode(p));
        }
    }
}
