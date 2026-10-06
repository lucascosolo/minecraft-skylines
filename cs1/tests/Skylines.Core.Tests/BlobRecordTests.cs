using System;
using Skylines.Core.Saves;
using Xunit;

namespace Skylines.Core.Tests
{
    public class BlobRecordTests
    {
        // version 1, u32 LE length 3, payload 01 02 03, CRC-32 0x329D9C65 computed with python3 zlib.crc32.
        private static readonly byte[] Golden =
            { 0x01, 0x03, 0x00, 0x00, 0x00, 0x01, 0x02, 0x03, 0x65, 0x9C, 0x9D, 0x32 };

        [Fact]
        public void EncodePinsExactBytes()
        {
            Assert.Equal(1, BlobRecord.Version);
            Assert.Equal(Golden, BlobRecord.Encode(new byte[] { 1, 2, 3 }));
            Assert.Equal(new byte[] { 1, 2, 3 }, BlobRecord.Decode(Golden));
        }

        [Fact]
        public void EmptyPayloadRoundTrips()
        {
            byte[] rec = BlobRecord.Encode(new byte[0]);
            Assert.Equal(9, rec.Length);
            Assert.Empty(BlobRecord.Decode(rec));
        }

        [Fact]
        public void LargeRandomPayloadRoundTrips()
        {
            var data = new byte[100 * 1024];
            new Random(42).NextBytes(data);
            Assert.Equal(data, BlobRecord.Decode(BlobRecord.Encode(data)));
        }

        [Fact]
        public void EncodeNullThrows()
        {
            Assert.Throws<ArgumentNullException>(() => BlobRecord.Encode(null));
        }

        [Fact]
        public void DecodeNullThrows()
        {
            Assert.Throws<FormatException>(() => BlobRecord.Decode(null));
        }

        [Fact]
        public void ShorterThanNineBytesThrows()
        {
            for (int n = 0; n < 9; n++)
            {
                int len = n;
                Assert.Throws<FormatException>(() => BlobRecord.Decode(new byte[len]));
            }
        }

        [Fact]
        public void WrongVersionThrows()
        {
            byte[] rec = (byte[])Golden.Clone();
            rec[0] = 2;
            Assert.Throws<FormatException>(() => BlobRecord.Decode(rec));
        }

        [Fact]
        public void EveryTruncationThrows()
        {
            for (int len = 0; len < Golden.Length; len++)
            {
                var cut = new byte[len];
                Array.Copy(Golden, cut, len);
                Assert.Throws<FormatException>(() => BlobRecord.Decode(cut));
            }
        }

        [Fact]
        public void TrailingBytesThrow()
        {
            var rec = new byte[Golden.Length + 1];
            Array.Copy(Golden, rec, Golden.Length);
            Assert.Throws<FormatException>(() => BlobRecord.Decode(rec));
        }

        [Fact]
        public void FlippingAnySingleByteThrows()
        {
            for (int i = 0; i < Golden.Length; i++)
            {
                byte[] rec = (byte[])Golden.Clone();
                rec[i] ^= 0x01;
                Assert.Throws<FormatException>(() => BlobRecord.Decode(rec));
            }
        }
    }
}
