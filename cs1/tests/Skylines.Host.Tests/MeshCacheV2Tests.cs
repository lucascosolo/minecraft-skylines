using System;
using System.IO;
using System.Text;
using Skylines.Host.Geometry;
using Xunit;

namespace Skylines.Host.Tests
{
    public class MeshCacheV2Tests
    {
        private sealed class E
        {
            public string Name;
            public int Vc;
            public float[] Pos;
            public ushort[] Idx;
            public uint Mask;
            public float[] Normals, Tangents, Uv, Uv2, Uv3, Uv4;
            public byte[] Colors;
        }

        private static E Make(string name, int vc, uint mask)
        {
            var e = new E { Name = name, Vc = vc, Mask = mask };
            e.Pos = Seq(vc * 3, 1f);
            e.Idx = new ushort[] { 0, 1, 2 };
            if ((mask & 1) != 0) e.Normals = Seq(vc * 3, 100f);
            if ((mask & 2) != 0) e.Tangents = Seq(vc * 4, 200f);
            if ((mask & 4) != 0) { e.Colors = new byte[vc * 4]; for (int i = 0; i < e.Colors.Length; i++) e.Colors[i] = (byte)(i * 7 + 3); }
            if ((mask & 8) != 0) e.Uv = Seq(vc * 2, 300f);
            if ((mask & 16) != 0) e.Uv2 = Seq(vc * 2, 400f);
            if ((mask & 32) != 0) e.Uv3 = Seq(vc * 2, 500f);
            if ((mask & 64) != 0) e.Uv4 = Seq(vc * 2, 600f);
            return e;
        }

        private static float[] Seq(int n, float start)
        {
            var a = new float[n];
            for (int i = 0; i < n; i++) a[i] = start + i * 0.25f;
            return a;
        }

        private static void F(BinaryWriter w, float[] a) { if (a != null) foreach (float f in a) w.Write(f); }

        private static byte[] Build(uint version, params E[] entries)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("CS1MESH\0"));
            w.Write(version);
            w.Write((uint)entries.Length);
            foreach (E e in entries)
            {
                byte[] name = Encoding.UTF8.GetBytes(e.Name);
                w.Write((ushort)name.Length); w.Write(name);
                w.Write((uint)e.Vc);
                w.Write(0f); w.Write(0f); w.Write(0f); w.Write(1f); w.Write(1f); w.Write(1f);
                w.Write((uint)e.Idx.Length);
                if (version >= 2) w.Write(e.Mask);
                F(w, e.Pos);
                foreach (ushort i in e.Idx) w.Write(i);
                F(w, e.Normals); F(w, e.Tangents);
                if (e.Colors != null) w.Write(e.Colors);
                F(w, e.Uv); F(w, e.Uv2); F(w, e.Uv3); F(w, e.Uv4);
            }
            w.Flush();
            return ms.ToArray();
        }

        private static MeshCache Open(byte[] data)
        {
            string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string p = Path.Combine(dir, "v2.bin");
            File.WriteAllBytes(p, data);
            return MeshCache.Open(p);
        }

        private static bool Get(MeshCache c, string name, int vc, out CachedMesh m)
        {
            return c.TryGetMesh(name, vc, 0, 0, 0, 1, 1, 1, out m);
        }

        [Fact]
        public void ChannelConstantsMatchContract()
        {
            Assert.Equal(1, MeshCache.ChannelNormals);
            Assert.Equal(2, MeshCache.ChannelTangents);
            Assert.Equal(4, MeshCache.ChannelColors);
            Assert.Equal(8, MeshCache.ChannelUv);
            Assert.Equal(16, MeshCache.ChannelUv2);
            Assert.Equal(32, MeshCache.ChannelUv3);
            Assert.Equal(64, MeshCache.ChannelUv4);
        }

        [Fact]
        public void V2AllChannelsRoundTripExactly()
        {
            E e = Make("all", 3, 127);
            MeshCache c = Open(Build(2, e));
            Assert.True(c.Available);
            CachedMesh m;
            Assert.True(Get(c, "all", 3, out m));
            Assert.Equal(e.Pos, m.Positions);
            Assert.Equal(new[] { 0, 1, 2 }, m.Indices);
            Assert.Equal(e.Normals, m.Normals);
            Assert.Equal(e.Tangents, m.Tangents);
            Assert.Equal(e.Colors, m.Colors);
            Assert.Equal(e.Uv, m.Uv);
            Assert.Equal(e.Uv2, m.Uv2);
            Assert.Equal(e.Uv3, m.Uv3);
            Assert.Equal(e.Uv4, m.Uv4);
        }

        [Fact]
        public void V2OnlyNormalsAndUvLeavesOthersNull()
        {
            E e = Make("nu", 4, 1 | 8);
            MeshCache c = Open(Build(2, e));
            CachedMesh m;
            Assert.True(Get(c, "nu", 4, out m));
            Assert.Equal(e.Normals, m.Normals);
            Assert.Equal(e.Uv, m.Uv);
            Assert.Null(m.Tangents);
            Assert.Null(m.Colors);
            Assert.Null(m.Uv2);
            Assert.Null(m.Uv3);
            Assert.Null(m.Uv4);
        }

        [Fact]
        public void V2SecondEntryIsFoundAfterChannelDataOfFirst()
        {
            E a = Make("first", 3, 127), b = Make("second", 5, 4 | 16);
            MeshCache c = Open(Build(2, a, b));
            Assert.True(c.Available);
            Assert.Equal(2, c.Count);
            CachedMesh m;
            Assert.True(Get(c, "second", 5, out m));
            Assert.Equal(b.Pos, m.Positions);
            Assert.Equal(b.Colors, m.Colors);
            Assert.Equal(b.Uv2, m.Uv2);
            Assert.Null(m.Normals);
        }

        [Fact]
        public void LegacyTryGetWorksOnV2File()
        {
            E a = Make("first", 3, 127), b = Make("second", 5, 1);
            MeshCache c = Open(Build(2, a, b));
            float[] pos; int[] idx;
            Assert.True(c.TryGet("second", 5, 0, 0, 0, 1, 1, 1, out pos, out idx));
            Assert.Equal(b.Pos, pos);
            Assert.Equal(new[] { 0, 1, 2 }, idx);
        }

        [Fact]
        public void V1FileStillReadAndTryGetMeshHasNullChannels()
        {
            E e = Make("old", 3, 0);
            MeshCache c = Open(Build(1, e));
            Assert.True(c.Available);
            CachedMesh m;
            Assert.True(Get(c, "old", 3, out m));
            Assert.Equal(e.Pos, m.Positions);
            Assert.Equal(new[] { 0, 1, 2 }, m.Indices);
            Assert.Null(m.Normals); Assert.Null(m.Tangents); Assert.Null(m.Colors);
            Assert.Null(m.Uv); Assert.Null(m.Uv2); Assert.Null(m.Uv3); Assert.Null(m.Uv4);
            float[] pos; int[] idx;
            Assert.True(c.TryGet("old", 3, 0, 0, 0, 1, 1, 1, out pos, out idx));
        }

        [Fact]
        public void UnknownMaskBitIsUnavailableAndStatusMentionsEntry()
        {
            E e = Make("bad", 3, 1);
            e.Mask = 1 | 128;
            MeshCache c = Open(Build(2, e));
            Assert.False(c.Available);
            Assert.Equal(0, c.Count);
            Assert.Contains("entry", c.Status, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Version3IsUnavailable()
        {
            MeshCache c = Open(Build(3, Make("x", 3, 1)));
            Assert.False(c.Available);
            Assert.False(string.IsNullOrEmpty(c.Status));
        }

        [Fact]
        public void TruncatedChannelDataIsUnavailable()
        {
            byte[] b = Build(2, Make("t", 3, 127));
            byte[] cut = new byte[b.Length - 5];
            Array.Copy(b, cut, cut.Length);
            MeshCache c = Open(cut);
            Assert.False(c.Available);
            Assert.Equal(0, c.Count);
        }
    }
}
