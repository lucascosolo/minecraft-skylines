using System;
using System.Collections.Generic;
using System.Linq;
using Skylines.Host.Rendering;
using Xunit;

namespace Skylines.Host.Tests
{
    public class MeshSplitTests
    {
        private static int[] Range(int n) { return Enumerable.Range(0, n).ToArray(); }

        private static int[] Reconstruct(List<MeshPart> parts)
        {
            var r = new List<int>();
            foreach (MeshPart p in parts)
                foreach (int i in p.Indices) r.Add(p.VertexMap[i]);
            return r.ToArray();
        }

        [Fact]
        public void MaxVerticesConstant()
        {
            Assert.Equal(65000, MeshSplit.MaxVertices);
        }

        [Fact]
        public void ZeroVerticesGivesNoParts()
        {
            Assert.Empty(MeshSplit.Split(new int[0], 0, 6));
        }

        [Fact]
        public void FastPathIsOnePartWithIdentityMapAndCopiedIndices()
        {
            int[] idx = { 0, 2, 1, 3, 4, 5 };
            List<MeshPart> parts = MeshSplit.Split(idx, 6, 6);
            MeshPart p = Assert.Single(parts);
            Assert.Equal(Range(6), p.VertexMap);
            Assert.Equal(idx, p.Indices);
            Assert.NotSame(idx, p.Indices);
        }

        [Fact]
        public void LargeNonIndexedMeshSplitsIntoThreeParts()
        {
            List<MeshPart> parts = MeshSplit.Split(Range(147456), 147456, MeshSplit.MaxVertices);
            Assert.Equal(new[] { 64998, 64998, 17460 }, parts.Select(p => p.VertexMap.Length).ToArray());
            Assert.Equal(Range(147456), Reconstruct(parts));
        }

        [Fact]
        public void SharedVerticesAreMappedOncePerPartAcrossASplit()
        {
            // quad A (0,1,2 / 2,1,3) uses 4 vertices; quad B (4,5,6 / 6,5,7) four more; cap 6 cannot hold both.
            int[] idx = { 0, 1, 2, 2, 1, 3, 4, 5, 6, 6, 5, 7 };
            List<MeshPart> parts = MeshSplit.Split(idx, 8, 6);
            Assert.Equal(2, parts.Count);
            Assert.Equal(new[] { 0, 1, 2, 3 }, parts[0].VertexMap);
            Assert.Equal(new[] { 0, 1, 2, 2, 1, 3 }, parts[0].Indices);
            Assert.Equal(new[] { 4, 5, 6, 7 }, parts[1].VertexMap);
            Assert.Equal(new[] { 0, 1, 2, 2, 1, 3 }, parts[1].Indices);
        }

        [Fact]
        public void VerticesAreNumberedInFirstUseOrder()
        {
            int[] idx = { 7, 3, 5, 5, 3, 1 };
            List<MeshPart> parts = MeshSplit.Split(idx, 10, 4);
            MeshPart p = Assert.Single(parts);
            Assert.Equal(new[] { 7, 3, 5 }, p.VertexMap.Take(3).ToArray());
            Assert.Equal(new[] { 0, 1, 2 }, p.Indices.Take(3).ToArray());
            Assert.Equal(idx, Reconstruct(parts));
        }

        [Fact]
        public void PartsRespectCapAndKeepTrianglesWholeAndInOrder()
        {
            var rnd = new Random(12345);
            int n = 50;
            var idx = new int[300];
            for (int i = 0; i < idx.Length; i++) idx[i] = rnd.Next(n);
            for (int i = 0; i < idx.Length; i += 3)
                if (idx[i] == idx[i + 1] || idx[i] == idx[i + 2] || idx[i + 1] == idx[i + 2]) idx[i + 1] = (idx[i] + 1) % n;
            List<MeshPart> parts = MeshSplit.Split(idx, n, 9);
            Assert.True(parts.Count > 1);
            foreach (MeshPart p in parts)
            {
                Assert.True(p.VertexMap.Length <= 9);
                Assert.Equal(0, p.Indices.Length % 3);
                Assert.Equal(p.VertexMap.Length, p.VertexMap.Distinct().Count());
                Assert.All(p.Indices, i => Assert.InRange(i, 0, p.VertexMap.Length - 1));
            }
            Assert.Equal(idx, Reconstruct(parts));
        }

        [Fact]
        public void IndicesNotMultipleOfThreeThrows()
        {
            Assert.Throws<ArgumentException>(() => MeshSplit.Split(new[] { 0, 1 }, 3, 6));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(3)]
        public void IndexOutOfRangeThrows(int bad)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MeshSplit.Split(new[] { 0, 1, bad }, 3, 6));
        }

        [Fact]
        public void MaxVerticesBelowThreeThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MeshSplit.Split(new[] { 0, 1, 2 }, 3, 2));
        }
    }
}
