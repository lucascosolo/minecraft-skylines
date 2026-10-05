using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Runtime.InteropServices;
using Skylines.Host.Overlay;
using Xunit;

namespace Skylines.Host.Tests
{
    public class OverlayReaderTests
    {
        private static JsonDocument Load()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string p = Path.Combine(dir.FullName, "protocol", "vectors", "overlay_layout.json");
                if (File.Exists(p)) return JsonDocument.Parse(File.ReadAllText(p));
                dir = dir.Parent;
            }
            throw new FileNotFoundException("protocol/vectors/overlay_layout.json");
        }

        private static byte[] Hex(string h)
        {
            var b = new byte[h.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(i * 2, 2), 16);
            return b;
        }

        private static string NewPath() { return Path.Combine(GuestWriter.NewTempDir(), "overlay.bin"); }

        private static byte[] Filled(int n, byte v)
        {
            var b = new byte[n];
            for (int i = 0; i < n; i++) b[i] = v;
            return b;
        }

        private static byte[] Header(uint magic, uint version, uint w, uint h, uint slots, int length)
        {
            var b = new byte[length];
            uint[] f = { magic, version, w, h, slots };
            for (int i = 0; i < f.Length && i * 4 + 4 <= length; i++) BitConverter.GetBytes(f[i]).CopyTo(b, i * 4);
            return b;
        }

        private static void AssertPermutation(int a, int b, int c)
        {
            Assert.Equal(3, a + b + c);
            Assert.Equal(3, new System.Collections.Generic.HashSet<int> { a, b, c }.Count);
        }

        [Fact]
        public void Golden_layout_opens_and_first_acquire_matches_vector()
        {
            var root = Load().RootElement;
            string path = NewPath();
            File.WriteAllBytes(path, Hex(root.GetProperty("hex").GetString()));
            var slot = root.GetProperty("slots")[0];
            byte[] expected = Hex(slot.GetProperty("rgbaHex").GetString());
            using (var r = SharedOverlayReader.Open(path))
            {
                Assert.Equal(root.GetProperty("maxWidth").GetUInt32(), r.MaxWidth);
                Assert.Equal(root.GetProperty("maxHeight").GetUInt32(), r.MaxHeight);
                Assert.Equal(root.GetProperty("slotCount").GetUInt32(), r.SlotCount);
                Assert.Equal(ulong.Parse(root.GetProperty("generation").GetString()), r.Generation);
                Assert.Equal(ulong.Parse(root.GetProperty("framesPublished").GetString()), r.FramesPublished);
                Assert.Equal(root.GetProperty("fileSize").GetInt64(), r.FileLength);
                Assert.Equal(2, r.FrontSlot);
                Assert.False(r.HasFrame);
                Assert.True(r.Acquire());
                Assert.Equal(root.GetProperty("middleSlot").GetInt32(), r.FrontSlot);
                Assert.True(r.HasFrame);
                Assert.Equal(slot.GetProperty("width").GetInt32(), r.Front.Width);
                Assert.Equal(slot.GetProperty("height").GetInt32(), r.Front.Height);
                Assert.Equal(slot.GetProperty("flags").GetUInt32(), r.Front.Flags);
                Assert.Equal(ulong.Parse(slot.GetProperty("frameId").GetString()), r.Front.FrameId);
                Assert.Equal(expected.Length, r.Front.ByteCount);
                var dest = new byte[expected.Length + 5];
                r.CopyFront(dest);
                Assert.Equal(expected, dest[..expected.Length]);
                var viaPtr = new byte[expected.Length];
                Marshal.Copy(r.Front.Pixels, viaPtr, 0, viaPtr.Length);
                Assert.Equal(expected, viaPtr);
                Assert.False(r.Acquire());
            }
            Assert.Equal(2u, BitConverter.ToUInt32(File.ReadAllBytes(path), 0x14));
        }

        [Fact]
        public void No_publish_means_no_frame()
        {
            string path = NewPath();
            using (var w = new GuestWriter(path, 8, 4))
            using (var r = SharedOverlayReader.Open(path))
            {
                Assert.False(r.Acquire());
                Assert.False(r.HasFrame);
                Assert.Equal(0, r.FramesAcquired);
            }
        }

        [Fact]
        public void One_publish_is_acquired_with_its_pixels()
        {
            string path = NewPath();
            using (var w = new GuestWriter(path, 8, 4))
            using (var r = SharedOverlayReader.Open(path))
            {
                w.Publish(3, 2, 0, 42, Filled(24, 0x5A));
                Assert.Equal(1UL, r.FramesPublished);
                Assert.True(r.Acquire());
                Assert.Equal(42UL, r.Front.FrameId);
                Assert.Equal(3, r.Front.Width);
                Assert.Equal(2, r.Front.Height);
                Assert.Equal(24, r.Front.ByteCount);
                var dest = new byte[24];
                r.CopyFront(dest);
                Assert.Equal(Filled(24, 0x5A), dest);
                Assert.Equal(1, r.FramesAcquired);
                Assert.False(r.Acquire());
            }
        }

        [Fact]
        public void Two_publishes_before_acquire_yield_only_the_latest()
        {
            string path = NewPath();
            using (var w = new GuestWriter(path, 8, 4))
            using (var r = SharedOverlayReader.Open(path))
            {
                w.Publish(2, 2, 0, 1, Filled(16, 1));
                w.Publish(2, 2, 0, 2, Filled(16, 2));
                Assert.True(r.Acquire());
                Assert.Equal(2UL, r.Front.FrameId);
                Assert.Equal(Filled(16, 2).Length, r.Front.ByteCount);
                Assert.False(r.Acquire());
            }
        }

        [Fact]
        public void Interleaved_publish_and_acquire_keep_order_and_distinct_slots()
        {
            string path = NewPath();
            using (var w = new GuestWriter(path, 8, 4))
            using (var r = SharedOverlayReader.Open(path))
            {
                AssertPermutation(w.Back, w.Middle, r.FrontSlot);
                for (ulong id = 1; id <= 5; id++)
                {
                    w.Publish(2, 2, 0, id, Filled(16, (byte)id));
                    AssertPermutation(w.Back, w.Middle, r.FrontSlot);
                    Assert.True(r.Acquire());
                    Assert.Equal(id, r.Front.FrameId);
                    var dest = new byte[16];
                    r.CopyFront(dest);
                    Assert.Equal(Filled(16, (byte)id), dest);
                    AssertPermutation(w.Back, w.Middle, r.FrontSlot);
                }
            }
        }

        [Theory]
        [InlineData(0u, false)]
        [InlineData(1u, true)]
        public void RowsBottomUp_follows_flag_bit_zero(uint flags, bool expected)
        {
            string path = NewPath();
            using (var w = new GuestWriter(path, 8, 4))
            using (var r = SharedOverlayReader.Open(path))
            {
                w.Publish(2, 2, flags, 1, Filled(16, 1));
                Assert.True(r.Acquire());
                Assert.Equal(expected, r.Front.RowsBottomUp);
            }
        }

        [Theory]
        [InlineData(0u, 2u)]
        [InlineData(2u, 0u)]
        [InlineData(9u, 2u)]
        [InlineData(2u, 5u)]
        public void Invalid_slot_header_is_rejected(uint width, uint height)
        {
            string path = NewPath();
            using (var w = new GuestWriter(path, 8, 4))
            using (var r = SharedOverlayReader.Open(path))
            {
                w.Publish(2, 2, 0, 1, Filled(16, 1));
                Assert.True(r.Acquire());
                Assert.True(r.HasFrame);
                w.Publish(width, height, 0, 2, Filled(16, 2));
                Assert.False(r.Acquire());
                Assert.False(r.HasFrame);
                Assert.Equal(1, r.FramesRejected);
                Assert.Equal(1, r.FramesAcquired);
            }
        }

        [Fact]
        public void CopyFront_checks_state_and_destination_size()
        {
            string path = NewPath();
            using (var w = new GuestWriter(path, 8, 4))
            using (var r = SharedOverlayReader.Open(path))
            {
                Assert.Throws<InvalidOperationException>(() => r.CopyFront(new byte[64]));
                w.Publish(2, 2, 0, 1, Filled(16, 1));
                Assert.True(r.Acquire());
                Assert.Throws<ArgumentException>(() => r.CopyFront(new byte[15]));
            }
        }

        [Theory]
        [InlineData(0u, 1u, 8u, 4u, 3u, 0)]
        [InlineData(0x564F534Du, 2u, 8u, 4u, 3u, 0)]
        [InlineData(0x564F534Du, 1u, 8u, 4u, 2u, 0)]
        [InlineData(0x564F534Du, 1u, 0u, 4u, 3u, 0)]
        [InlineData(0x564F534Du, 1u, 8u, 0u, 3u, 0)]
        [InlineData(0x564F534Du, 1u, 16385u, 4u, 3u, 0)]
        [InlineData(0x564F534Du, 1u, 8u, 4u, 3u, -1)]
        [InlineData(0x564F534Du, 1u, 8u, 4u, 3u, 0x100)]
        [InlineData(0x564F534Du, 1u, 8u, 4u, 3u, 0x80)]
        public void Malformed_files_throw_OverlayFormatException(uint magic, uint version, uint w, uint h, uint slots, int lengthDelta)
        {
            string path = NewPath();
            int full = (int)(0x100 + 3L * Math.Min(w, 64u) * Math.Min(h, 64u) * 4);
            int length = lengthDelta == 0 ? full : lengthDelta == -1 ? full - 1 : lengthDelta;
            File.WriteAllBytes(path, Header(magic, version, w, h, slots, length));
            Assert.Throws<OverlayFormatException>(() => SharedOverlayReader.Open(path));
        }

        [Fact]
        public void Missing_file_throws_plain_IOException()
        {
            string path = NewPath();
            var ex = Assert.ThrowsAny<IOException>(() => SharedOverlayReader.Open(path));
            Assert.IsNotType<OverlayFormatException>(ex);
        }

        [Fact]
        public void Close_is_idempotent_and_keeps_the_file()
        {
            string path = NewPath();
            byte[] before;
            long length;
            using (var w = new GuestWriter(path, 8, 4))
            {
                w.Publish(2, 2, 0, 1, Filled(16, 1));
                before = File.ReadAllBytes(path);
                length = before.Length;
            }
            var r = SharedOverlayReader.Open(path);
            Assert.True(r.IsOpen);
            Assert.True(r.Acquire());
            r.Close();
            r.Close();
            r.Dispose();
            Assert.False(r.IsOpen);
            Assert.Throws<ObjectDisposedException>(() => r.Acquire());
            Assert.True(File.Exists(path));
            byte[] after = File.ReadAllBytes(path);
            Assert.Equal(length, after.Length);
            for (int i = 0x14; i < 0x18; i++) { before[i] = 0; after[i] = 0; }
            Assert.Equal(before, after);
        }

        [Fact]
        public void Stress_no_torn_frames_and_monotonic_ids()
        {
            const int N = 20000;
            string path = NewPath();
            using (var w = new GuestWriter(path, 64, 32))
            using (var r = SharedOverlayReader.Open(path))
            {
                var t = new Thread(() =>
                {
                    for (int id = 1; id <= N; id++)
                    {
                        uint pw = (uint)(1 + id % 64), ph = (uint)(1 + id % 32);
                        w.Publish(pw, ph, 0, (ulong)id, Filled((int)(pw * ph * 4), (byte)id));
                    }
                });
                t.Start();
                ulong last = 0;
                int acquired = 0;
                var deadline = DateTime.UtcNow.AddSeconds(30);
                var buf = new byte[64 * 32 * 4];
                while (last < N && DateTime.UtcNow < deadline)
                {
                    if (!r.Acquire()) { Thread.Yield(); continue; }
                    acquired++;
                    Assert.True(r.Front.FrameId > last);
                    last = r.Front.FrameId;
                    r.CopyFront(buf);
                    for (int i = 0; i < r.Front.ByteCount; i++)
                        if (buf[i] != (byte)last) Assert.Fail("torn frame " + last + " at byte " + i);
                }
                t.Join();
                Assert.Equal((ulong)N, last);
                Assert.True(acquired > 0);
            }
        }
    }
}
