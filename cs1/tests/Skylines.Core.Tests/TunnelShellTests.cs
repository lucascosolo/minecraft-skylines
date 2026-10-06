using System;
using Skylines.Core.Geometry;
using Xunit;

namespace Skylines.Core.Tests
{
    public class TunnelShellTests
    {
        private static Bezier3D Line(float x, float y0, float y1, float z0, float z1)
        {
            float dy = (y1 - y0) / 3f, dz = (z1 - z0) / 3f;
            return new Bezier3D
            {
                Ax = x, Ay = y0, Az = z0,
                Bx = x, By = y0 + dy, Bz = z0 + dz,
                Cx = x, Cy = y0 + 2 * dy, Cz = z0 + 2 * dz,
                Dx = x, Dy = y1, Dz = z1,
            };
        }

        private static ShellMesh Level(out int covered)
        {
            ShellMesh m = new ShellMesh();
            covered = TunnelShell.Segment(Line(0, 0, 0, 0, 20), Line(10, 0, 0, 0, 20), 6f, true, 4f, 0.5f, 1f, 8f, m);
            return m;
        }

        private static void AssertWellFormed(ShellMesh m)
        {
            Assert.Equal(0, m.Indices.Count % 3);
            Assert.All(m.Indices, i => Assert.InRange(i, 0, m.VertexCount - 1));
            Assert.Equal(m.Positions.Count, m.Normals.Count);
            Assert.Equal(m.Positions.Count / 3 * 2, m.Uvs.Count);
        }

        private static void AssertWinding(ShellMesh m)
        {
            Assert.True(m.Indices.Count > 0);
            float[] p = m.Positions.ToArray();
            for (int t = 0; t < m.Indices.Count; t += 3)
            {
                int a = m.Indices[t], b = m.Indices[t + 1], c = m.Indices[t + 2];
                float ux = p[b * 3] - p[a * 3], uy = p[b * 3 + 1] - p[a * 3 + 1], uz = p[b * 3 + 2] - p[a * 3 + 2];
                float vx = p[c * 3] - p[a * 3], vy = p[c * 3 + 1] - p[a * 3 + 1], vz = p[c * 3 + 2] - p[a * 3 + 2];
                float cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
                float dot = cx * m.Normals[a * 3] + cy * m.Normals[a * 3 + 1] + cz * m.Normals[a * 3 + 2];
                Assert.True(dot > 0, "triangle " + (t / 3) + " winds against its normal");
            }
        }

        private static bool NormalIs(ShellMesh m, int v, float x, float y, float z)
        {
            return Math.Abs(m.Normals[v * 3] - x) < 1e-4f && Math.Abs(m.Normals[v * 3 + 1] - y) < 1e-4f && Math.Abs(m.Normals[v * 3 + 2] - z) < 1e-4f;
        }

        [Fact]
        public void StraightLevelTunnelCoversEveryInterval()
        {
            int covered;
            ShellMesh m = Level(out covered);
            Assert.Equal(5, covered);
            Assert.True(m.VertexCount > 0);
            AssertWellFormed(m);
        }

        [Fact]
        public void WallsAreInsetAndSpanFootToCeiling()
        {
            int covered;
            ShellMesh m = Level(out covered);
            Assert.True(m.VertexCount > 0);
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int v = 0; v < m.VertexCount; v++)
            {
                float x = m.Positions[v * 3];
                Assert.True(Math.Abs(x - 0.5f) < 1e-4f || Math.Abs(x - 9.5f) < 1e-4f, "x=" + x);
                minY = Math.Min(minY, m.Positions[v * 3 + 1]);
                maxY = Math.Max(maxY, m.Positions[v * 3 + 1]);
            }
            Assert.Equal(-0.5f, minY, 3);
            Assert.Equal(5f, maxY, 3);
        }

        [Fact]
        public void NormalsFaceInside()
        {
            int covered;
            ShellMesh m = Level(out covered);
            int left = 0, right = 0, ceiling = 0;
            for (int v = 0; v < m.VertexCount; v++)
            {
                if (NormalIs(m, v, 1, 0, 0)) { left++; Assert.Equal(0.5f, m.Positions[v * 3], 3); }
                else if (NormalIs(m, v, -1, 0, 0)) { right++; Assert.Equal(9.5f, m.Positions[v * 3], 3); }
                else if (NormalIs(m, v, 0, -1, 0)) { ceiling++; Assert.Equal(5f, m.Positions[v * 3 + 1], 3); }
                else Assert.Fail("unexpected normal at vertex " + v);
            }
            Assert.True(left > 0 && right > 0 && ceiling > 0);
        }

        [Fact]
        public void EveryTriangleWindsTowardsItsNormal()
        {
            int covered;
            AssertWinding(Level(out covered));
        }

        [Fact]
        public void EdgesTooCloseGiveNothing()
        {
            ShellMesh m = new ShellMesh();
            int n = TunnelShell.Segment(Line(0, 0, 0, 0, 20), Line(0.8f, 0, 0, 0, 20), 6f, true, 4f, 0.5f, 1f, 8f, m);
            Assert.Equal(0, n);
            Assert.Equal(0, m.VertexCount);
            Assert.Empty(m.Indices);
        }

        [Fact]
        public void SlopeCoversOnlyIntervalsBelowGroundByClearance()
        {
            const float clearance = 5.5f;
            ShellMesh m = new ShellMesh();
            int n = TunnelShell.Segment(Line(0, -10, 0, 0, 40), Line(10, -10, 0, 0, 40), clearance, false, 4f, 0.5f, 1f, 8f, m);
            int expected = 0;
            for (int k = 0; k < 10; k++)
            {
                float floorTop = -10f + (k + 1); // the higher end of interval k; ground (portal level) is 0
                if (floorTop + clearance <= 0f) expected++;
            }
            Assert.Equal(4, expected);
            Assert.Equal(expected, n);
            AssertWellFormed(m);
            AssertWinding(m);
        }

        [Fact]
        public void LevelSlopeCoversNothing()
        {
            ShellMesh m = new ShellMesh();
            int n = TunnelShell.Segment(Line(0, 0, 0, 0, 20), Line(10, 0, 0, 0, 20), 6f, false, 4f, 0.5f, 1f, 8f, m);
            Assert.Equal(0, n);
            Assert.Equal(0, m.VertexCount);
        }

        [Fact]
        public void AppendingKeepsEarlierVerticesAndOffsetsIndices()
        {
            ShellMesh m = new ShellMesh();
            m.Add(1, 2, 3, 0, 1, 0, 0, 0);
            m.Add(4, 5, 6, 0, 1, 0, 0, 0);
            int before = m.VertexCount;
            TunnelShell.Segment(Line(0, 0, 0, 0, 20), Line(10, 0, 0, 0, 20), 6f, true, 4f, 0.5f, 1f, 8f, m);
            Assert.True(m.VertexCount > before);
            Assert.Equal(1f, m.Positions[0]);
            Assert.Equal(6f, m.Positions[5]);
            Assert.NotEmpty(m.Indices);
            Assert.All(m.Indices, i => Assert.InRange(i, before, m.VertexCount - 1));
            AssertWellFormed(m);
        }

        private static float[] ScrambledRing()
        {
            // square corners at angles 270, 0, 180, 90 degrees (radius 5), x y z each
            return new float[] { 0, 0, -5, 5, 0, 0, -5, 0, 0, 0, 0, 5 };
        }

        [Fact]
        public void JunctionReturnsTwoTrianglesPerRingPoint()
        {
            ShellMesh m = new ShellMesh();
            int n = TunnelShell.Junction(0, 0, 0, ScrambledRing(), 4, 6f, 1f, 8f, m);
            Assert.Equal(8, n);
            Assert.Equal(24, m.Indices.Count);
            AssertWellFormed(m);
        }

        [Fact]
        public void JunctionFacesAndHeightsAreCorrectDespiteRingOrder()
        {
            ShellMesh m = new ShellMesh();
            TunnelShell.Junction(0, 0, 0, ScrambledRing(), 4, 6f, 1f, 8f, m);
            int up = 0, down = 0;
            for (int v = 0; v < m.VertexCount; v++)
            {
                if (NormalIs(m, v, 0, 1, 0)) { up++; Assert.Equal(0f, m.Positions[v * 3 + 1], 3); }
                else if (NormalIs(m, v, 0, -1, 0)) { down++; Assert.Equal(5f, m.Positions[v * 3 + 1], 3); }
                else Assert.Fail("unexpected normal at vertex " + v);
            }
            Assert.True(up > 0 && down > 0);
            AssertWinding(m);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        public void JunctionWithFewerThanThreePointsGivesNothing(int count)
        {
            ShellMesh m = new ShellMesh();
            Assert.Equal(0, TunnelShell.Junction(0, 0, 0, ScrambledRing(), count, 6f, 1f, 8f, m));
            Assert.Equal(0, m.VertexCount);
        }
    }
}
