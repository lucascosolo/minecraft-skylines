using System;
using System.IO;
using System.Text.Json;
using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Xunit;

namespace MinecraftSkylines.Protocol.Tests
{
    public class CitizenEventsTests
    {
        private static byte[] Hex(string h)
        {
            var b = new byte[h.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(2 * i, 2), 16);
            return b;
        }

        private static JsonElement Vector(string list, string name, out JsonDocument doc)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string p = Path.Combine(dir.FullName, "protocol", "vectors", "frames.json");
                if (File.Exists(p))
                {
                    doc = JsonDocument.Parse(File.ReadAllText(p));
                    foreach (JsonElement v in doc.RootElement.GetProperty(list).EnumerateArray())
                        if (v.GetProperty("name").GetString() == name) return v;
                    throw new InvalidOperationException(name);
                }
                dir = dir.Parent;
            }
            throw new FileNotFoundException("protocol/vectors/frames.json");
        }

        private static byte[] Payload(string list, string name)
        {
            JsonDocument d;
            JsonElement v = Vector(list, name, out d);
            using (d) return FrameCodec.Decode(Hex(v.GetProperty("hex").GetString())).Payload;
        }

        [Fact]
        public void Constants()
        {
            Assert.Equal(19, AppProtocol.Minor);
            Assert.Equal(0x01D2, AppProtocol.CitizenEventsType);
            Assert.Equal(1, CitizenEvents.Panic);
            Assert.Equal(2, CitizenEvents.Killed);
            Assert.Equal(3, CitizenEvents.Converted);
            Assert.Equal(1024, CitizenEvents.MaxEvents);
        }

        [Theory]
        [InlineData("citizen_events_empty")]
        [InlineData("citizen_events_three")]
        public void VectorDecodesToFields(string name)
        {
            JsonDocument d;
            JsonElement v = Vector("valid", name, out d);
            using (d)
            {
                byte[] payload = FrameCodec.Decode(Hex(v.GetProperty("hex").GetString())).Payload;
                CitizenEvents m = CitizenEvents.Decode(payload);
                JsonElement f = v.GetProperty("fields");
                Assert.Equal(f.GetProperty("openSeq").GetUInt32(), m.OpenSeq);
                JsonElement list = f.GetProperty("events");
                Assert.Equal(list.GetArrayLength(), m.Events.Length);
                int i = 0;
                foreach (JsonElement e in list.EnumerateArray())
                {
                    CitizenEvent o = m.Events[i++];
                    Assert.Equal(e.GetProperty("kind").GetInt32(), (int)o.Kind);
                    Assert.Equal(e.GetProperty("id").GetUInt32(), o.Id);
                    Assert.Equal((float)e.GetProperty("x").GetDouble(), o.X);
                    Assert.Equal((float)e.GetProperty("y").GetDouble(), o.Y);
                    Assert.Equal((float)e.GetProperty("z").GetDouble(), o.Z);
                }
            }
        }

        [Theory]
        [InlineData("citizen_events_empty")]
        [InlineData("citizen_events_three")]
        public void EncodingFieldsEqualsVectorPayload(string name)
        {
            JsonDocument d;
            JsonElement v = Vector("valid", name, out d);
            using (d)
            {
                byte[] payload = FrameCodec.Decode(Hex(v.GetProperty("hex").GetString())).Payload;
                JsonElement f = v.GetProperty("fields");
                JsonElement list = f.GetProperty("events");
                var events = new CitizenEvent[list.GetArrayLength()];
                int i = 0;
                foreach (JsonElement e in list.EnumerateArray())
                {
                    events[i++] = new CitizenEvent
                    {
                        Kind = (byte)e.GetProperty("kind").GetInt32(),
                        Id = e.GetProperty("id").GetUInt32(),
                        X = (float)e.GetProperty("x").GetDouble(),
                        Y = (float)e.GetProperty("y").GetDouble(),
                        Z = (float)e.GetProperty("z").GetDouble(),
                    };
                }
                var m = new CitizenEvents { OpenSeq = f.GetProperty("openSeq").GetUInt32(), Events = events };
                Assert.Equal(payload, m.Encode());
            }
        }

        [Theory]
        [InlineData("citizen_events_too_many")]
        [InlineData("citizen_events_bad_kind")]
        [InlineData("citizen_events_truncated")]
        [InlineData("citizen_events_trailing_bytes")]
        public void InvalidVectorThrows(string name)
        {
            byte[] p = Payload("invalid", name);
            Assert.Throws<ProtocolException>(() => CitizenEvents.Decode(p));
        }
    }
}
