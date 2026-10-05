using System;
using Xunit;

namespace MinecraftSkylines.Protocol.Tests
{
    public class BlockMeshConversionTests
    {
        private struct V
        {
            public float X, Y, Z, U, Vv; public uint Color;
            public V(float x, float y, float z, float u = 0, float v = 0, uint color = 0xFFFFFFFFu)
            { X = x; Y = y; Z = z; U = u; Vv = v; Color = color; }
        }

        private static SectionMesh Mesh(int sx, int sy, int sz, params V[] vs)
        {
            var data = new byte[vs.Length * SectionMesh.BytesPerVertex];
            for (int i = 0; i < vs.Length; i++)
                SectionMesh.WriteVertex(data, i, vs[i].X, vs[i].Y, vs[i].Z, vs[i].U, vs[i].Vv, vs[i].Color, 0xF0u, 0u);
            return new SectionMesh { Sx = sx, Sy = sy, Sz = sz, VertexData = data };
        }

        private static float[] Pos(CsMeshData d, int i) { return new[] { d.Positions[3 * i], d.Positions[3 * i + 1], d.Positions[3 * i + 2] }; }

        private static float[] Cross(CsMeshData d, int t)
        {
            float[] a = Pos(d, d.Indices[3 * t]), b = Pos(d, d.Indices[3 * t + 1]), c = Pos(d, d.Indices[3 * t + 2]);
            float ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2];
            float vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
            return new[] { uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx };
        }

        [Fact]
        public void PositionsAreRelativeAndZIsMirrored()
        {
            CsMeshData d = BlockMeshConversion.Convert(Mesh(0, 0, 0, new V(1, 2, 3), new V(4, 5, 6), new V(0, 0, 0)));
            Assert.Equal(9, d.Positions.Length);
            Assert.Equal(1f, d.Positions[0]); Assert.Equal(2f, d.Positions[1]); Assert.Equal(-3f, d.Positions[2]);
            Assert.Equal(4f, d.Positions[3]); Assert.Equal(5f, d.Positions[4]); Assert.Equal(-6f, d.Positions[5]);
        }

        [Theory]
        [InlineData(0, 0, 0, 0.0, 0.0, 0.0)]
        [InlineData(1, 2, 3, 16.0, 32.0, -48.0)]
        [InlineData(-2, 4, 37, -32.0, 64.0, -592.0)]
        [InlineData(-1, -4, -1, -16.0, -64.0, 16.0)]
        public void OriginIsSectionCornerInCs(int sx, int sy, int sz, double ox, double oy, double oz)
        {
            CsMeshData d = BlockMeshConversion.Convert(Mesh(sx, sy, sz, new V(0, 0, 0), new V(1, 0, 0), new V(0, 1, 0)));
            Assert.Equal(ox, d.OriginX);
            Assert.Equal(oy - MinecraftFrame.YOffset, d.OriginY);
            Assert.Equal(oz, d.OriginZ);
        }

        [Fact]
        public void TopFaceKeepsPointingUp()
        {
            // MC top face, CCW seen from above: normal +y.
            CsMeshData d = BlockMeshConversion.Convert(Mesh(0, 0, 0, new V(0, 1, 0), new V(0, 1, 1), new V(1, 1, 1)));
            float[] n = Cross(d, 0);
            Assert.True(n[1] > 0 && Math.Abs(n[0]) < 1e-6 && Math.Abs(n[2]) < 1e-6);
        }

        [Fact]
        public void SouthFacePointsToMinusZInCs()
        {
            CsMeshData d = BlockMeshConversion.Convert(Mesh(0, 0, 0, new V(0, 0, 1), new V(1, 0, 1), new V(1, 1, 1)));
            float[] n = Cross(d, 0);
            Assert.True(n[2] < 0 && Math.Abs(n[0]) < 1e-6 && Math.Abs(n[1]) < 1e-6);
        }

        [Fact]
        public void EastFaceStaysPlusX()
        {
            CsMeshData d = BlockMeshConversion.Convert(Mesh(0, 0, 0, new V(1, 0, 0), new V(1, 1, 0), new V(1, 1, 1)));
            float[] n = Cross(d, 0);
            Assert.True(n[0] > 0 && Math.Abs(n[1]) < 1e-6 && Math.Abs(n[2]) < 1e-6);
        }

        [Fact]
        public void IndicesReverseWindingPerTriangle()
        {
            CsMeshData d = BlockMeshConversion.Convert(Mesh(0, 0, 0,
                new V(0, 0, 0), new V(1, 0, 0), new V(0, 1, 0),
                new V(2, 0, 0), new V(3, 0, 0), new V(2, 1, 0)));
            Assert.Equal(new[] { 0, 2, 1, 3, 5, 4 }, d.Indices);
        }

        [Fact]
        public void UvVIsFlipped()
        {
            CsMeshData d = BlockMeshConversion.Convert(Mesh(0, 0, 0,
                new V(0, 0, 0, 0.25f, 0.5f), new V(1, 0, 0, 0.3125f, 0f), new V(0, 1, 0, 1f, 1f)));
            Assert.Equal(6, d.Uvs.Length);
            Assert.Equal(0.25f, d.Uvs[0]); Assert.Equal(0.5f, d.Uvs[1]);
            Assert.Equal(0.3125f, d.Uvs[2]); Assert.Equal(1f, d.Uvs[3]);
            Assert.Equal(1f, d.Uvs[4]); Assert.Equal(0f, d.Uvs[5]);
        }

        [Fact]
        public void ColorBytesFollowWireOrder()
        {
            CsMeshData d = BlockMeshConversion.Convert(Mesh(0, 0, 0,
                new V(0, 0, 0, 0, 0, 0xFF80FFFFu), new V(1, 0, 0, 0, 0, 0x44332211u), new V(0, 1, 0, 0, 0, 0xFFFFFFFFu)));
            Assert.Equal(12, d.Colors.Length);
            Assert.Equal(new byte[] { 0xFF, 0xFF, 0x80, 0xFF }, new[] { d.Colors[0], d.Colors[1], d.Colors[2], d.Colors[3] });
            Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 }, new[] { d.Colors[4], d.Colors[5], d.Colors[6], d.Colors[7] });
            Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, new[] { d.Colors[8], d.Colors[9], d.Colors[10], d.Colors[11] });
        }

        [Fact]
        public void VertexOrderIsPreserved()
        {
            CsMeshData d = BlockMeshConversion.Convert(Mesh(0, 0, 0,
                new V(1, 0, 0, 0.1f, 0), new V(2, 0, 0, 0.2f, 0), new V(3, 0, 0, 0.3f, 0),
                new V(4, 0, 0, 0.4f, 0), new V(5, 0, 0, 0.5f, 0), new V(6, 0, 0, 0.6f, 0)));
            for (int i = 0; i < 6; i++)
            {
                Assert.Equal((float)(i + 1), d.Positions[3 * i]);
                Assert.Equal(0.1f * (i + 1), d.Uvs[2 * i], 5);
            }
        }

        [Fact]
        public void EmptyMeshGivesEmptyArrays()
        {
            CsMeshData d = BlockMeshConversion.Convert(new SectionMesh { Sx = 3, Sy = 1, Sz = -2, VertexData = new byte[0] });
            Assert.Empty(d.Positions); Assert.Empty(d.Uvs); Assert.Empty(d.Colors); Assert.Empty(d.Indices);
            Assert.Equal(48.0, d.OriginX);
            Assert.Equal(32.0, d.OriginZ);
        }
    }
}
