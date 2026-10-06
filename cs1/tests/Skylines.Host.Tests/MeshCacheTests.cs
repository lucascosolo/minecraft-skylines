using System;
using System.IO;
using Skylines.Host.Geometry;
using Xunit;

namespace Skylines.Host.Tests
{
    public class MeshCacheTests
    {
        private static string GoldenPath
        {
            get { return Path.Combine(AppContext.BaseDirectory, "Data", "meshes-golden.bin"); }
        }

        private static string WriteTemp(byte[] data)
        {
            string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string p = Path.Combine(dir, "bad.bin");
            File.WriteAllBytes(p, data);
            return p;
        }

        private static byte[] Golden() { return File.ReadAllBytes(GoldenPath); }

        private static byte[] Drop(byte[] b, int n)
        {
            byte[] r = new byte[b.Length - n];
            Array.Copy(b, r, r.Length);
            return r;
        }

        private static readonly float[] BoxPos =
            { -1, 0, -2.25f, 2, 0, -2.25f, 2, 2, 1.75f, -1, 2, 1.75f };

        [Fact]
        public void GoldenOpensWithThreeEntries()
        {
            MeshCache c = MeshCache.Open(GoldenPath);
            Assert.True(c.Available);
            Assert.Equal(3, c.Count);
        }

        [Fact]
        public void TryGetByNameReturnsExactGeometry()
        {
            MeshCache c = MeshCache.Open(GoldenPath);
            float[] pos; int[] idx;
            Assert.True(c.TryGet("Golden Box", 4, 0.5f, 1f, -0.25f, 1.5f, 1f, 2f, out pos, out idx));
            Assert.Equal(BoxPos, pos);
            Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }, idx);
        }

        [Fact]
        public void TryGetUtf8Name()
        {
            MeshCache c = MeshCache.Open(GoldenPath);
            float[] pos; int[] idx;
            Assert.True(c.TryGet("Golden Tri é", 3, 0f, 0.5f, 0f, 0.5f, 0.5f, 0.5f, out pos, out idx));
            Assert.Equal(new float[] { 0, 0, 0, 0.5f, 1, 0, -0.5f, 0, 0.5f }, pos);
            Assert.Equal(new[] { 0, 1, 2 }, idx);
        }

        [Fact]
        public void TryGetTwinByNameDisambiguates()
        {
            MeshCache c = MeshCache.Open(GoldenPath);
            float[] pos; int[] idx;
            Assert.True(c.TryGet("Golden Tri twin", 3, 0f, 0.5f, 0f, 0.5f, 0.5f, 0.5f, out pos, out idx));
            Assert.Equal(new float[] { 0, 0, 0, -0.5f, 1, 0, 0.5f, 0, 0.5f }, pos);
            Assert.Equal(new[] { 0, 2, 1 }, idx);
        }

        [Fact]
        public void NameAndVertexCountMatchWhenTheGameReplacedTheBounds()
        {
            // CS1 gives net segment meshes a 128 m box at load (NetInfo.InitSegmentInfo); the name still identifies them.
            MeshCache c = MeshCache.Open(GoldenPath);
            float[] pos; int[] idx;
            Assert.True(c.TryGet("Golden Box", 4, 0f, 10f, 0f, 64f, 40f, 64f, out pos, out idx));
            Assert.Equal(BoxPos, pos);
            Assert.False(c.TryGet("Not In Cache", 4, 0f, 10f, 0f, 64f, 40f, 64f, out pos, out idx));
        }

        [Fact]
        public void AmbiguousUnknownNameFails()
        {
            MeshCache c = MeshCache.Open(GoldenPath);
            float[] pos; int[] idx;
            Assert.False(c.TryGet("unknown", 3, 0f, 0.5f, 0f, 0.5f, 0.5f, 0.5f, out pos, out idx));
            Assert.Null(pos);
            Assert.Null(idx);
        }

        [Fact]
        public void UniqueCandidateMatchesIgnoringName()
        {
            MeshCache c = MeshCache.Open(GoldenPath);
            float[] pos; int[] idx;
            Assert.True(c.TryGet("renamed box", 4, 0.5f, 1f, -0.25f, 1.5f, 1f, 2f, out pos, out idx));
            Assert.Equal(BoxPos, pos);
            Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }, idx);
        }

        [Fact]
        public void WrongVertexCountFails()
        {
            MeshCache c = MeshCache.Open(GoldenPath);
            float[] pos; int[] idx;
            Assert.False(c.TryGet("Golden Box", 5, 0.5f, 1f, -0.25f, 1.5f, 1f, 2f, out pos, out idx));
            Assert.Null(pos);
            Assert.Null(idx);
        }

        [Fact]
        public void BoundsToleranceBoundary()
        {
            MeshCache c = MeshCache.Open(GoldenPath);
            float[] pos; int[] idx;
            // A different name: outside the tolerance only the bounds could match, and they do not
            // (the same name would match by name alone, see NameAndVertexCountMatchWhenTheGameReplacedTheBounds).
            Assert.False(c.TryGet("Other Box", 4, 0.51f, 1f, -0.25f, 1.5f, 1f, 2f, out pos, out idx));
            Assert.False(c.TryGet("Other Box", 4, 0.5f, 1f, -0.25f, 1.5f, 1f, 2.01f, out pos, out idx));
            Assert.True(c.TryGet("Golden Box", 4, 0.5005f, 1f, -0.25f, 1.5f, 1f, 2f, out pos, out idx));
            Assert.True(c.TryGet("Golden Box", 4, 0.5f, 1f, -0.25f, 1.5f, 1f, 1.9995f, out pos, out idx));
        }

        [Fact]
        public void MissingFileIsUnavailable()
        {
            string p = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "nope.bin");
            MeshCache c = MeshCache.Open(p);
            Assert.NotNull(c);
            Assert.False(c.Available);
            Assert.Equal(0, c.Count);
            Assert.False(string.IsNullOrEmpty(c.Status));
            float[] pos; int[] idx;
            Assert.False(c.TryGet("Golden Box", 4, 0.5f, 1f, -0.25f, 1.5f, 1f, 2f, out pos, out idx));
        }

        private static void AssertUnavailable(byte[] data)
        {
            MeshCache c = MeshCache.Open(WriteTemp(data));
            Assert.NotNull(c);
            Assert.False(c.Available);
            Assert.Equal(0, c.Count);
            Assert.False(string.IsNullOrEmpty(c.Status));
        }

        [Fact]
        public void BadMagicIsUnavailable()
        {
            byte[] b = Golden();
            b[0] = (byte)'X';
            AssertUnavailable(b);
        }

        [Fact]
        public void WrongVersionIsUnavailable()
        {
            byte[] b = Golden();
            b[8] = 2;
            AssertUnavailable(b);
        }

        [Fact]
        public void TruncatedOneByteIsUnavailable()
        {
            AssertUnavailable(Drop(Golden(), 1));
        }

        [Fact]
        public void TruncatedTenBytesIsUnavailable()
        {
            AssertUnavailable(Drop(Golden(), 10));
        }

        [Fact]
        public void EmptyFileIsUnavailable()
        {
            AssertUnavailable(new byte[0]);
        }

        [Fact]
        public void DefaultPathFromHome()
        {
            Assert.Equal("/home/x/.cache/minecraft-skylines/cs1-meshes/meshes.bin",
                MeshCache.DefaultPath("/home/x"));
            Assert.Null(MeshCache.DefaultPath(null));
            Assert.Null(MeshCache.DefaultPath(""));
        }
    }
}
