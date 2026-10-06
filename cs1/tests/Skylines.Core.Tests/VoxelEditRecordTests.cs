using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Skylines.Core.Voxels;
using Xunit;

namespace Skylines.Core.Tests
{
    public class VoxelEditRecordTests
    {
        // Hand-built: edits (-3,64,0) stone, (-3,65,0) "mod:café", (10,-2,7) stone.
        // CRC 0x15BAEAE8 computed with zlib.crc32 (independent of the code under test).
        private static readonly byte[] Golden =
        {
            0x01, 0x02, 0x00, 0x0F, 0x00, 0x6D, 0x69, 0x6E, 0x65, 0x63, 0x72, 0x61, 0x66, 0x74, 0x3A, 0x73,
            0x74, 0x6F, 0x6E, 0x65, 0x09, 0x00, 0x6D, 0x6F, 0x64, 0x3A, 0x63, 0x61, 0x66, 0xC3, 0xA9, 0x03,
            0x00, 0x00, 0x00, 0xFD, 0xFF, 0xFF, 0xFF, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0xFD, 0xFF, 0xFF, 0xFF, 0x41, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x0A,
            0x00, 0x00, 0x00, 0xFE, 0xFF, 0xFF, 0xFF, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xE8, 0xEA, 0xBA, 0x15
        };

        // Independent bitwise CRC-32 for sealing crafted records.
        private static uint RefCrc(byte[] d, int n)
        {
            uint c = 0xFFFFFFFF;
            for (int i = 0; i < n; i++)
            {
                c ^= d[i];
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320 : c >> 1;
            }
            return ~c;
        }

        private static byte[] Seal(byte[] body)
        {
            var r = new byte[body.Length + 4];
            Array.Copy(body, r, body.Length);
            var c = RefCrc(body, body.Length);
            for (int i = 0; i < 4; i++) r[body.Length + i] = (byte)(c >> (8 * i));
            return r;
        }

        private static byte[] Craft(byte version, string[] palette, int[][] edits)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms); // BinaryWriter is little endian
            w.Write(version);
            w.Write((ushort)palette.Length);
            foreach (var p in palette)
            {
                var b = Encoding.UTF8.GetBytes(p);
                w.Write((ushort)b.Length);
                w.Write(b);
            }
            w.Write((uint)edits.Length);
            foreach (var e in edits)
            {
                w.Write(e[0]); w.Write(e[1]); w.Write(e[2]); w.Write((ushort)e[3]);
            }
            w.Flush();
            return Seal(ms.ToArray());
        }

        private static VoxelEditSet GoldenSet()
        {
            var s = new VoxelEditSet();
            s.Set(10, -2, 7, "minecraft:stone");
            s.Set(-3, 65, 0, "mod:café");
            s.Set(-3, 64, 0, "minecraft:stone");
            return s;
        }

        [Fact]
        public void Crc32CheckValue()
        {
            var d = Encoding.ASCII.GetBytes("123456789");
            Assert.Equal(0xCBF43926u, VoxelEditRecord.Crc32(d, 0, d.Length));
        }

        [Fact]
        public void Crc32HonoursOffsetAndCount()
        {
            var d = Encoding.ASCII.GetBytes("xx123456789yy");
            Assert.Equal(0xCBF43926u, VoxelEditRecord.Crc32(d, 2, 9));
            Assert.Equal(0u, VoxelEditRecord.Crc32(d, 0, 0));
        }

        [Fact]
        public void VersionIsOne()
        {
            Assert.Equal((byte)1, VoxelEditRecord.Version);
        }

        [Fact]
        public void EncodeMatchesGoldenBytes()
        {
            Assert.Equal(Golden, VoxelEditRecord.Encode(GoldenSet()));
        }

        [Fact]
        public void DecodeGoldenBytes()
        {
            var s = VoxelEditRecord.Decode(Golden);
            Assert.Equal(3, s.Count);
            string v;
            Assert.True(s.TryGet(-3, 64, 0, out v)); Assert.Equal("minecraft:stone", v);
            Assert.True(s.TryGet(-3, 65, 0, out v)); Assert.Equal("mod:café", v);
            Assert.True(s.TryGet(10, -2, 7, out v)); Assert.Equal("minecraft:stone", v);
        }

        [Fact]
        public void EmptySetIsElevenBytes()
        {
            var b = VoxelEditRecord.Encode(new VoxelEditSet());
            Assert.Equal(11, b.Length);
            Assert.Equal(Seal(new byte[] { 1, 0, 0, 0, 0, 0, 0 }), b);
            Assert.Equal(0, VoxelEditRecord.Decode(b).Count);
        }

        [Fact]
        public void PaletteHasNoUnusedEntries()
        {
            var s = new VoxelEditSet();
            s.Set(0, 0, 0, "a:one");
            s.Set(0, 0, 0, "a:two"); // overwrite; "a:one" must not appear
            var b = VoxelEditRecord.Encode(s);
            Assert.Equal(Craft(1, new[] { "a:two" }, new[] { new[] { 0, 0, 0, 0 } }), b);
        }

        [Fact]
        public void RandomRoundTrip()
        {
            var rnd = new Random(12345);
            var s = new VoxelEditSet();
            var names = Enumerable.Range(0, 40).Select(i => "mod:bé" + i).ToArray();
            for (int i = 0; i < 5000; i++)
                s.Set(rnd.Next(-200, 200), rnd.Next(-64, 320), rnd.Next(-200, 200), names[rnd.Next(names.Length)]);
            var d = VoxelEditRecord.Decode(VoxelEditRecord.Encode(s));
            Assert.Equal(s.Count, d.Count);
            Assert.Equal(s.Sorted(), d.Sorted());
            Assert.Equal(VoxelEditRecord.Encode(s), VoxelEditRecord.Encode(d));
        }

        [Fact]
        public void DecodeNullThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => VoxelEditRecord.Decode(null));
        }

        [Fact]
        public void DecodeEmptyArrayThrowsFormat()
        {
            Assert.Throws<FormatException>(() => VoxelEditRecord.Decode(new byte[0]));
        }

        [Fact]
        public void CorruptCrcThrows()
        {
            var b = (byte[])Golden.Clone();
            b[b.Length - 1] ^= 0xFF;
            Assert.Throws<FormatException>(() => VoxelEditRecord.Decode(b));
        }

        [Fact]
        public void CorruptBodyThrows()
        {
            var b = (byte[])Golden.Clone();
            b[40] ^= 0x01;
            Assert.Throws<FormatException>(() => VoxelEditRecord.Decode(b));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(30)]
        [InlineData(50)]
        [InlineData(76)]
        [InlineData(77)]
        [InlineData(80)]
        public void TruncationThrows(int length)
        {
            var b = new byte[length];
            Array.Copy(Golden, b, length);
            Assert.Throws<FormatException>(() => VoxelEditRecord.Decode(b));
        }

        [Fact]
        public void TruncatedWithValidCrcThrows()
        {
            var body = new byte[Golden.Length - 4 - 5];
            Array.Copy(Golden, body, body.Length);
            Assert.Throws<FormatException>(() => VoxelEditRecord.Decode(Seal(body)));
        }

        [Fact]
        public void UnknownVersionThrows()
        {
            Assert.Throws<FormatException>(() => VoxelEditRecord.Decode(Craft(2, new[] { "a:b" }, new[] { new[] { 0, 0, 0, 0 } })));
            Assert.Throws<FormatException>(() => VoxelEditRecord.Decode(Craft(0, new string[0], new int[0][])));
        }

        [Fact]
        public void TrailingByteThrows()
        {
            var b = new byte[Golden.Length + 1];
            Array.Copy(Golden, b, Golden.Length);
            Assert.Throws<FormatException>(() => VoxelEditRecord.Decode(b));
        }

        [Fact]
        public void TrailingByteBeforeCrcThrows()
        {
            var body = new byte[Golden.Length - 4 + 1];
            Array.Copy(Golden, body, Golden.Length - 4);
            Assert.Throws<FormatException>(() => VoxelEditRecord.Decode(Seal(body)));
        }

        [Fact]
        public void ValidCraftedRecordDecodes()
        {
            var s = VoxelEditRecord.Decode(Craft(1, new[] { "a:b" }, new[] { new[] { 0, 0, 0, 0 }, new[] { 0, 1, 0, 0 } }));
            Assert.Equal(2, s.Count);
        }

        [Fact]
        public void PaletteIndexOutOfRangeThrows()
        {
            Assert.Throws<FormatException>(() => VoxelEditRecord.Decode(Craft(1, new[] { "a:b" }, new[] { new[] { 0, 0, 0, 1 } })));
            Assert.Throws<FormatException>(() => VoxelEditRecord.Decode(Craft(1, new string[0], new[] { new[] { 0, 0, 0, 0 } })));
        }

        [Fact]
        public void DuplicateEditThrows()
        {
            Assert.Throws<FormatException>(() => VoxelEditRecord.Decode(
                Craft(1, new[] { "a:b" }, new[] { new[] { 1, 2, 3, 0 }, new[] { 1, 2, 3, 0 } })));
        }

        [Fact]
        public void DescendingEditsThrow()
        {
            Assert.Throws<FormatException>(() => VoxelEditRecord.Decode(
                Craft(1, new[] { "a:b" }, new[] { new[] { 2, 0, 0, 0 }, new[] { 1, 0, 0, 0 } })));
        }

        [Fact]
        public void YOrderedBeforeZIsRejected()
        {
            // (x=0,y=0,z=1) then (x=0,y=5,z=0): ordered by y but not by (x,z,y).
            Assert.Throws<FormatException>(() => VoxelEditRecord.Decode(
                Craft(1, new[] { "a:b" }, new[] { new[] { 0, 0, 1, 0 }, new[] { 0, 5, 0, 0 } })));
        }

        [Fact]
        public void XzyOrderIsAccepted()
        {
            var s = VoxelEditRecord.Decode(
                Craft(1, new[] { "a:b" }, new[] { new[] { 0, 5, 0, 0 }, new[] { 0, 0, 1, 0 } }));
            Assert.Equal(2, s.Count);
        }

        [Fact]
        public void DuplicatePaletteEntryThrows()
        {
            Assert.Throws<FormatException>(() => VoxelEditRecord.Decode(
                Craft(1, new[] { "a:b", "a:b" }, new[] { new[] { 0, 0, 0, 0 } })));
        }

        [Fact]
        public void AirPaletteEntryThrows()
        {
            Assert.Throws<FormatException>(() => VoxelEditRecord.Decode(
                Craft(1, new[] { "minecraft:air" }, new[] { new[] { 0, 0, 0, 0 } })));
        }

        [Fact]
        public void InvalidUtf8Throws()
        {
            // palette entry bytes C3 28 are invalid UTF-8
            var body = new byte[]
            {
                1, 1, 0, 2, 0, 0xC3, 0x28,
                1, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
            };
            Assert.Throws<FormatException>(() => VoxelEditRecord.Decode(Seal(body)));
        }
    }
}
