using System;
using System.IO;
using System.Text.Json;
using Skylines.Bridge;
using Xunit;

namespace MinecraftSkylines.Protocol.Tests
{
    public class WaterTests
    {
        private static WaterSurface Sample()
        {
            return new WaterSurface
            {
                OriginX = -33,
                OriginZ = 2015,
                Size = 2,
                Surface = new[] { 40.5f, 38f, 12.25f, -1f },
                Bottom = new[] { 30f, 31.5f, 12.25f, -1f },
            };
        }

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

        [Fact]
        public void ConstantsMatchSpec()
        {
            Assert.Equal(0x01A0, AppProtocol.WaterSurfaceType);
            Assert.Equal(128, WaterSurface.MaxSize);
        }

        [Fact]
        public void RoundTripAndEveryTruncationThrows()
        {
            byte[] full = Sample().Encode();
            Assert.Equal(10 + 4 * 8, full.Length);
            WaterSurface m = WaterSurface.Decode(full);
            Assert.Equal(-33, m.OriginX);
            Assert.Equal(2015, m.OriginZ);
            Assert.Equal(2, m.Size);
            Assert.Equal(new[] { 40.5f, 38f, 12.25f, -1f }, m.Surface);
            Assert.Equal(new[] { 30f, 31.5f, 12.25f, -1f }, m.Bottom);
            Assert.Equal(full, m.Encode());
            for (int len = 0; len < full.Length; len++)
            {
                byte[] cut = new byte[len];
                Array.Copy(full, cut, len);
                Assert.Throws<ProtocolException>(() => WaterSurface.Decode(cut));
            }
        }

        [Fact]
        public void EmptyGridRoundTrips()
        {
            byte[] p = new WaterSurface().Encode();
            Assert.Equal(10, p.Length);
            WaterSurface m = WaterSurface.Decode(p);
            Assert.Equal(0, m.Size);
            Assert.Empty(m.Surface);
            Assert.Empty(m.Bottom);
        }

        [Fact]
        public void Size129IsRejectedBeforeColumnsAreRead()
        {
            byte[] p = new byte[10];
            p[8] = 129;
            Assert.Throws<ProtocolException>(() => WaterSurface.Decode(p));
        }

        [Fact]
        public void Size128WithoutColumnDataIsTruncation()
        {
            byte[] p = new byte[10];
            p[8] = 128;
            Assert.Throws<ProtocolException>(() => WaterSurface.Decode(p));
        }

        [Theory]
        [InlineData("water_surface")]
        [InlineData("water_surface_empty")]
        public void ValidVectorDecodesToFieldsAndReEncodesToSamePayload(string name)
        {
            JsonElement v = Vector("valid", name);
            JsonElement f = v.GetProperty("fields");
            Frame frame = FrameCodec.Decode(Hex(v.GetProperty("hex").GetString()));
            Assert.Equal(AppProtocol.WaterSurfaceType, frame.Type);
            WaterSurface m = WaterSurface.Decode(frame.Payload);
            Assert.Equal(f.GetProperty("originX").GetInt32(), m.OriginX);
            Assert.Equal(f.GetProperty("originZ").GetInt32(), m.OriginZ);
            Assert.Equal(f.GetProperty("size").GetUInt16(), m.Size);
            Assert.Equal(f.GetProperty("surface").GetArrayLength(), m.Surface.Length);
            Assert.Equal(f.GetProperty("bottom").GetArrayLength(), m.Bottom.Length);
            int i = 0;
            foreach (JsonElement e in f.GetProperty("surface").EnumerateArray()) Assert.Equal(e.GetSingle(), m.Surface[i++]);
            i = 0;
            foreach (JsonElement e in f.GetProperty("bottom").EnumerateArray()) Assert.Equal(e.GetSingle(), m.Bottom[i++]);
            Assert.Equal(frame.Payload, m.Encode());
        }

        [Fact]
        public void InvalidSize129VectorRaisesProtocolException()
        {
            JsonElement v = Vector("invalid", "water_surface_size_129");
            byte[] p = FrameCodec.Decode(Hex(v.GetProperty("hex").GetString())).Payload;
            Assert.Throws<ProtocolException>(() => WaterSurface.Decode(p));
        }
    }
}
