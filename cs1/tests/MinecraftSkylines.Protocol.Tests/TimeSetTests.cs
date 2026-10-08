using System;
using Skylines.Bridge;
using Xunit;

namespace MinecraftSkylines.Protocol.Tests
{
    public class TimeSetTests
    {
        [Fact]
        public void Constants()
        {
            Assert.Equal(0x0161, AppProtocol.TimeSetType);
            Assert.Equal(18, AppProtocol.Minor);
        }

        [Theory]
        [InlineData(0f, (ushort)0)]
        [InlineData(12f, (ushort)0)]
        [InlineData(7.5f, (ushort)3)]
        [InlineData(23.5f, (ushort)65535)]
        public void RoundTrips(float hour, ushort days)
        {
            byte[] payload = new TimeSet { Hour = hour, Days = days }.Encode();
            Assert.Equal(6, payload.Length);
            TimeSet m = TimeSet.Decode(payload);
            Assert.Equal(hour, m.Hour);
            Assert.Equal(days, m.Days);
            Assert.Equal(payload, m.Encode());
        }

        [Fact]
        public void EncodeIsLittleEndianFloatThenUShort()
        {
            Assert.Equal(new byte[] { 0, 0, 0x40, 0x41, 0x01, 0x00 }, new TimeSet { Hour = 12f, Days = 1 }.Encode());
        }

        [Fact]
        public void DecodeTruncatedThrows()
        {
            byte[] p = new TimeSet { Hour = 12f, Days = 1 }.Encode();
            for (int len = 0; len < p.Length; len++)
            {
                byte[] cut = new byte[len];
                Array.Copy(p, cut, len);
                Assert.Throws<ProtocolException>(() => TimeSet.Decode(cut));
            }
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(24f)]
        [InlineData(100f)]
        [InlineData(-0.5f)]
        public void DecodeBadHourThrows(float hour)
        {
            byte[] h = BitConverter.GetBytes(hour);
            if (!BitConverter.IsLittleEndian) Array.Reverse(h);
            byte[] payload = new byte[6];
            Array.Copy(h, payload, 4);
            Assert.Throws<ProtocolException>(() => TimeSet.Decode(payload));
        }

        [Fact]
        public void OffsetSameHourIsZeroPlusDays()
        {
            // hour 10000*24/65536 maps back to frame 10000 (= 337680 mod 65536).
            float hour = 10000f * 24f / 65536f;
            Assert.Equal(0u, TimeSet.OffsetFrames(10000, hour, 0));
            Assert.Equal(2u * 65536u, TimeSet.OffsetFrames(10000, hour, 2));
        }

        [Fact]
        public void OffsetWrapsAcrossDayBoundary()
        {
            // target frame (uint)(1*65536/24) = 2730; (2730 - 60000) & 65535 = 8266.
            Assert.Equal(8266u, TimeSet.OffsetFrames(60000, 1f, 0));
            Assert.Equal(8266u + 2u * 65536u, TimeSet.OffsetFrames(60000, 1f, 2));
        }

        [Fact]
        public void OffsetHourJustBelow24IsCappedAtFrame65535()
        {
            Assert.Equal(65535u, TimeSet.OffsetFrames(0, 23.9999f, 0));
            Assert.Equal(65535u, TimeSet.OffsetFrames(0, 23.99999999f, 0));
        }

        [Fact]
        public void OffsetMatchesVectors()
        {
            Assert.Equal(22768u, TimeSet.OffsetFrames(337680, 12f, 0));
            Assert.Equal(207088u, TimeSet.OffsetFrames(337680, 7.5f, 3));
        }
    }
}
