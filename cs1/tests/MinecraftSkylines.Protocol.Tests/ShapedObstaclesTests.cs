using System;
using System.IO;
using System.Text.Json;
using Skylines.Bridge;
using Xunit;

namespace MinecraftSkylines.Protocol.Tests
{
    public class ShapedObstaclesTests
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
            Assert.Equal(21, AppProtocol.Minor);
            Assert.Equal(0x0171, AppProtocol.ShapedObstaclesType);
        }

        [Theory]
        [InlineData("shaped_obstacles")]
        [InlineData("shaped_obstacles_empty")]
        public void VectorDecodesToFieldsAndReEncodesIdentically(string name)
        {
            JsonDocument d;
            JsonElement v = Vector("valid", name, out d);
            using (d)
            {
                byte[] payload = FrameCodec.Decode(Hex(v.GetProperty("hex").GetString())).Payload;
                DynamicObstacles m = DynamicObstacles.DecodeShaped(payload);
                JsonElement list = v.GetProperty("fields").GetProperty("obstacles");
                Assert.Equal(list.GetArrayLength(), m.Obstacles.Length);
                int i = 0;
                foreach (JsonElement e in list.EnumerateArray())
                {
                    MovingObstacle o = m.Obstacles[i++];
                    Assert.Equal(e.GetProperty("kind").GetInt32(), (int)o.Kind);
                    Assert.Equal(e.GetProperty("id").GetUInt32(), o.Id);
                    Assert.Equal((float)e.GetProperty("x").GetDouble(), o.X);
                    Assert.Equal((float)e.GetProperty("y").GetDouble(), o.Y);
                    Assert.Equal((float)e.GetProperty("z").GetDouble(), o.Z);
                    Assert.Equal((float)e.GetProperty("yaw").GetDouble(), o.Yaw);
                    Assert.Equal((float)e.GetProperty("halfWidth").GetDouble(), o.HalfWidth);
                    Assert.Equal((float)e.GetProperty("halfHeight").GetDouble(), o.HalfHeight);
                    Assert.Equal((float)e.GetProperty("halfLength").GetDouble(), o.HalfLength);
                    Assert.Equal((float)e.GetProperty("vx").GetDouble(), o.VX);
                    Assert.Equal((float)e.GetProperty("vy").GetDouble(), o.VY);
                    Assert.Equal((float)e.GetProperty("vz").GetDouble(), o.VZ);
                    Assert.Equal((float)e.GetProperty("yawRate").GetDouble(), o.YawRate);
                    JsonElement prof = e.GetProperty("profile");
                    Assert.NotNull(o.Profile);
                    Assert.Equal(prof.GetArrayLength(), o.Profile.Length);
                    int j = 0;
                    foreach (JsonElement b in prof.EnumerateArray()) Assert.Equal((byte)b.GetInt32(), o.Profile[j++]);
                }
                Assert.Equal(payload, m.EncodeShaped());
            }
        }

        [Fact]
        public void TruncatedVectorThrows()
        {
            byte[] p = Payload("invalid", "shaped_obstacles_truncated");
            Assert.Throws<ProtocolException>(() => DynamicObstacles.DecodeShaped(p));
        }

        [Fact]
        public void EveryTruncationOfAValidPayloadThrows()
        {
            byte[] full = Payload("valid", "shaped_obstacles");
            for (int len = 0; len < full.Length; len++)
            {
                var cut = new byte[len];
                Array.Copy(full, cut, len);
                Assert.Throws<ProtocolException>(() => DynamicObstacles.DecodeShaped(cut));
            }
        }

        [Fact]
        public void RoundTripsA255ByteProfile()
        {
            var prof = new byte[255];
            for (int i = 0; i < prof.Length; i++) prof[i] = (byte)(i + 1);
            var o = new MovingObstacle { Kind = DynamicObstacles.Vehicle, Id = 9, X = 1, Y = 2, Z = 3, Yaw = 4, HalfWidth = 5, HalfHeight = 6, HalfLength = 7, VX = 8, VY = 9, VZ = 10, YawRate = -33.5f, Profile = prof };
            byte[] payload = new DynamicObstacles { Obstacles = new[] { o } }.EncodeShaped();
            Assert.Equal(2 + 1 + 4 + 40 + 4 + 1 + 255, payload.Length);
            MovingObstacle r = DynamicObstacles.DecodeShaped(payload).Obstacles[0];
            Assert.Equal(-33.5f, r.YawRate);
            Assert.Equal(prof, r.Profile);
        }

        [Fact]
        public void NullProfileEncodesAsZeroStepsAndDecodesEmpty()
        {
            var o = new MovingObstacle { Kind = DynamicObstacles.Citizen, Id = 1, YawRate = 12f, Profile = null };
            byte[] payload = new DynamicObstacles { Obstacles = new[] { o } }.EncodeShaped();
            Assert.Equal(2 + 1 + 4 + 40 + 4 + 1, payload.Length);
            Assert.Equal(0, payload[payload.Length - 1]);
            MovingObstacle r = DynamicObstacles.DecodeShaped(payload).Obstacles[0];
            Assert.NotNull(r.Profile);
            Assert.Empty(r.Profile);
            Assert.Equal(12f, r.YawRate);
        }

        [Fact]
        public void LegacyEncodeIsUnchangedByNewFields()
        {
            var o = new MovingObstacle { Kind = 1, Id = 5, X = 1, YawRate = 99f, Profile = new byte[] { 1, 2, 3 } };
            byte[] p = new DynamicObstacles { Obstacles = new[] { o } }.Encode();
            Assert.Equal(2 + 1 + 4 + 40, p.Length);
            Assert.Equal(5u, DynamicObstacles.Decode(p).Obstacles[0].Id);
        }
    }
}
