using System;
using System.IO;
using System.Text.Json;
using Skylines.Bridge;
using Xunit;

namespace MinecraftSkylines.Protocol.Tests
{
    public class TreesTests
    {
        private static byte[] Hex(string h)
        {
            var b = new byte[h.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(2 * i, 2), 16);
            return b;
        }

        private static byte[] Payload(string list, string name)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string p = Path.Combine(dir.FullName, "protocol", "vectors", "frames.json");
                if (File.Exists(p))
                {
                    using (JsonDocument d = JsonDocument.Parse(File.ReadAllText(p)))
                        foreach (JsonElement v in d.RootElement.GetProperty(list).EnumerateArray())
                            if (v.GetProperty("name").GetString() == name)
                                return FrameCodec.Decode(Hex(v.GetProperty("hex").GetString())).Payload;
                    throw new InvalidOperationException(name);
                }
                dir = dir.Parent;
            }
            throw new FileNotFoundException("protocol/vectors/frames.json");
        }

        [Theory]
        [InlineData("trees_empty", 0)]
        [InlineData("trees_one", 1)]
        [InlineData("trees_two", 2)]
        public void TreesVectorRoundTrips(string name, int count)
        {
            byte[] payload = Payload("valid", name);
            Trees m = Trees.Decode(payload);
            Assert.Equal(7u, m.Epoch);
            Assert.Equal(-3, m.RegionX);
            Assert.Equal(128, m.RegionZ);
            Assert.Equal(count, m.Items.Length);
            if (count == 2) Assert.Equal(TreeRecord.KindBush, m.Items[1].Kind);
            Assert.Equal(payload, m.Encode());
        }

        [Fact]
        public void TreeFelledVectorRoundTrips()
        {
            byte[] payload = Payload("valid", "tree_felled");
            TreeFelled m = TreeFelled.Decode(payload);
            Assert.Equal(3u, m.OpenSeq);
            Assert.Equal(70000u, m.TreeId);
            Assert.Equal(payload, m.Encode());
        }

        [Theory]
        [InlineData("trees_too_many")]
        [InlineData("trees_truncated")]
        [InlineData("trees_trailing_bytes")]
        public void InvalidTreesVectorsThrow(string name)
        {
            byte[] p = Payload("invalid", name);
            Assert.Throws<ProtocolException>(() => Trees.Decode(p));
        }

        [Theory]
        [InlineData("Pine Tree 2", 12f, 1)]
        [InlineData("Birch", 8f, 2)]
        [InlineData("Jungle Palm", 9f, 3)]
        [InlineData("SAVANNA acacia", 7f, 4)]
        [InlineData("Dead Tree", 6f, 5)]
        [InlineData("Hedge", 4f, 6)]
        [InlineData("Tree1", 1.5f, 6)]
        [InlineData("Tree1", 2.5f, 0)]
        [InlineData(null, 10f, 0)]
        public void KindOfMapsNames(string name, float height, int kind)
        {
            Assert.Equal((byte)kind, TreeRecord.KindOf(name, height));
        }

        [Fact]
        public void EveryTruncationThrows()
        {
            byte[] full = Payload("valid", "trees_two");
            for (int len = 0; len < full.Length; len++)
            {
                var cut = new byte[len];
                Array.Copy(full, cut, len);
                Assert.Throws<ProtocolException>(() => Trees.Decode(cut));
            }
            byte[] felled = Payload("valid", "tree_felled");
            for (int len = 0; len < felled.Length; len++)
            {
                var cut = new byte[len];
                Array.Copy(felled, cut, len);
                Assert.Throws<ProtocolException>(() => TreeFelled.Decode(cut));
            }
        }
    }
}
