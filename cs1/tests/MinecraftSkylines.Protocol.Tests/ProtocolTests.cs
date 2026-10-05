using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Skylines.Bridge;
using Xunit;

namespace MinecraftSkylines.Protocol.Tests
{
    public class ProtocolTests
    {
        private static JsonDocument Load(string name)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string p = Path.Combine(dir.FullName, "protocol", "vectors", name);
                if (File.Exists(p)) return JsonDocument.Parse(File.ReadAllText(p));
                dir = dir.Parent;
            }
            throw new FileNotFoundException("protocol/vectors/" + name);
        }

        private static byte[] Hex(string h)
        {
            var b = new byte[h.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(2 * i, 2), 16);
            return b;
        }

        public static IEnumerable<object[]> AppVectors()
        {
            using (JsonDocument d = Load("frames.json"))
                foreach (JsonElement v in d.RootElement.GetProperty("valid").EnumerateArray())
                    if (v.GetProperty("type").GetInt32() >= 0x100)
                        yield return new object[] { v.GetProperty("name").GetString() };
        }

        [Theory]
        [MemberData(nameof(AppVectors))]
        public void StatusVectorsDecodeAndEncode(string name)
        {
            JsonElement v = default;
            using (JsonDocument d = Load("frames.json"))
                foreach (JsonElement e in d.RootElement.GetProperty("valid").EnumerateArray())
                    if (e.GetProperty("name").GetString() == name) v = e.Clone();
            string hex = v.GetProperty("hex").GetString();
            JsonElement f = v.GetProperty("fields");
            Frame frame = FrameCodec.Decode(Hex(hex));
            byte[] payload;
            if (frame.Type == AppProtocol.HostStatusType)
            {
                HostStatus s = HostStatus.Decode(frame.Payload);
                Assert.Equal(f.GetProperty("flags").GetUInt32(), s.Flags);
                Assert.Equal(f.GetProperty("cityName").GetString(), s.CityName);
                Assert.Equal(new Guid(f.GetProperty("saveId").GetString()), s.SaveId);
                Assert.Equal(f.GetProperty("gameVersion").GetString(), s.GameVersion);
                payload = s.Encode();
            }
            else
            {
                Assert.Equal(AppProtocol.GuestStatusType, frame.Type);
                GuestStatus s = GuestStatus.Decode(frame.Payload);
                Assert.Equal(f.GetProperty("flags").GetUInt32(), s.Flags);
                Assert.Equal(f.GetProperty("worldName").GetString(), s.WorldName);
                Assert.Equal(new Guid(f.GetProperty("pairedSaveId").GetString()), s.PairedSaveId);
                payload = s.Encode();
            }
            Assert.Equal(hex, Convert.ToHexString(FrameCodec.Encode(frame.Type, payload)).ToLowerInvariant());
        }

        [Fact]
        public void TruncatedStatusIsProtocolError()
        {
            Assert.Throws<ProtocolException>(() => HostStatus.Decode(new byte[] { 1, 0, 0 }));
            Assert.Throws<ProtocolException>(() => GuestStatus.Decode(new byte[] { 1, 0, 0, 0, 5, 0, 1 }));
        }

        [Fact]
        public void ConstantsMatchSpec()
        {
            Assert.Equal("minecraft-skylines", AppProtocol.Name);
            Assert.Equal(1, AppProtocol.Major);
            Assert.Equal(0, AppProtocol.Minor);
            Assert.Equal(8u, HostStatusFlags.PlayerMode);
            Assert.Equal(2u, GuestStatusFlags.ScreenOpen);
        }

        private static double AngleDiff(double a, double b)
        {
            return Math.Abs(MinecraftFrame.Wrap180(a - b));
        }

        [Fact]
        public void CoordinateVectorsConvertBothWays()
        {
            using (JsonDocument d = Load("coords.json"))
            {
                double tol = d.RootElement.GetProperty("tolerance").GetDouble();
                Assert.Equal(MinecraftFrame.YOffset, d.RootElement.GetProperty("yOffset").GetDouble());
                foreach (JsonElement c in d.RootElement.GetProperty("cases").EnumerateArray())
                {
                    JsonElement cs = c.GetProperty("cs"), mc = c.GetProperty("mc");
                    var csPos = new Vec3d(cs.GetProperty("x").GetDouble(), cs.GetProperty("y").GetDouble(), cs.GetProperty("z").GetDouble());
                    var mcPos = new Vec3d(mc.GetProperty("x").GetDouble(), mc.GetProperty("y").GetDouble(), mc.GetProperty("z").GetDouble());
                    double ex = cs.GetProperty("eulerX").GetDouble(), ey = cs.GetProperty("eulerY").GetDouble();
                    double yaw = mc.GetProperty("yaw").GetDouble(), pitch = mc.GetProperty("pitch").GetDouble();

                    Vec3d m = MinecraftFrame.CsToMc(csPos);
                    Assert.InRange(Math.Abs(m.X - mcPos.X), 0, tol);
                    Assert.InRange(Math.Abs(m.Y - mcPos.Y), 0, tol);
                    Assert.InRange(Math.Abs(m.Z - mcPos.Z), 0, tol);
                    McLook look = MinecraftFrame.UnityEulerToMc(ex, ey);
                    Assert.InRange(AngleDiff(look.Yaw, yaw), 0, tol);
                    Assert.InRange(AngleDiff(look.Pitch, pitch), 0, tol);

                    Vec3d back = MinecraftFrame.McToCs(mcPos);
                    Assert.InRange(Math.Abs(back.X - csPos.X), 0, tol);
                    Assert.InRange(Math.Abs(back.Y - csPos.Y), 0, tol);
                    Assert.InRange(Math.Abs(back.Z - csPos.Z), 0, tol);
                    double bx, by;
                    MinecraftFrame.McToUnityEuler(new McLook(yaw, pitch), out bx, out by);
                    Assert.InRange(AngleDiff(bx, ex), 0, tol);
                    Assert.InRange(AngleDiff(by, ey), 0, tol);
                }
            }
        }
    }
}
