using System.Collections.Generic;
using System.Linq;
using Skylines.Core.Geometry;
using Xunit;

namespace Skylines.Core.Tests
{
    public class PortalCapTests
    {
        private sealed class Mesh
        {
            public readonly List<float> Pos = new List<float>();
            public readonly List<int> Idx = new List<int>();

            // Two triangles (a,b,c) and (a,c,d); returns the index of the first triangle.
            public int Quad(float[] a, float[] b, float[] c, float[] d)
            {
                int first = Idx.Count / 3;
                Tri(a, b, c);
                Tri(a, c, d);
                return first;
            }

            private void Tri(float[] a, float[] b, float[] c)
            {
                foreach (float[] v in new[] { a, b, c })
                {
                    Idx.Add(Pos.Count / 3);
                    Pos.AddRange(v);
                }
            }

            // Quad in a constant-z plane: x0..x1, y0..y1. Diagonal runs (x0,y0)-(x1,y1).
            public int ZQuad(float z, float x0, float x1, float y0, float y1)
            {
                return Quad(new[] { x0, y0, z }, new[] { x1, y0, z }, new[] { x1, y1, z }, new[] { x0, y1, z });
            }

            public int[] Expected(params int[] removedTris)
            {
                var keep = new List<int>();
                for (int t = 0; t < Idx.Count / 3; t++)
                {
                    if (removedTris.Contains(t)) continue;
                    keep.Add(Idx[3 * t]); keep.Add(Idx[3 * t + 1]); keep.Add(Idx[3 * t + 2]);
                }
                return keep.ToArray();
            }
        }

        // Horizontal-ish quad spanning z0..z1 (z scaled by sign).
        private static void Floor(Mesh m, float sign, float y, float z0, float z1)
        {
            m.Quad(new[] { -8f, y, z0 * sign }, new[] { 8f, y, z0 * sign }, new[] { 8f, y, z1 * sign }, new[] { -8f, y, z1 * sign });
        }

        // CS1 small-tunnel-slope like mesh; sign = -1 puts the deep end at +z.
        private static Mesh Slope(float sign, out int[] capTris)
        {
            var m = new Mesh();
            Floor(m, sign, 0f, -32f, 32f);
            Floor(m, sign, 11.8f, -32f, -16f); // roof block
            int cap = m.ZQuad(-30f * sign, -7, 7, -1, 7);
            m.ZQuad(-8f * sign, -6, 6, 6, 9.9f); // lintel, kept
            int back = m.ZQuad(-32f * sign, -8, 8, -3, 7.5f);
            m.ZQuad(32f * sign, 6, 8, 0, 2);
            m.ZQuad(32f * sign, -8, -6, 0, 2);
            m.ZQuad(32f * sign, -1, 1, 0, 2); // median, covers probe but at the shallow end
            capTris = new[] { cap, cap + 1, back, back + 1 };
            return m;
        }

        [Fact]
        public void ConstantsMatchContract()
        {
            Assert.Equal(1.5f, PortalCap.ProbeHeight);
            Assert.Equal(0.01f, PortalCap.PlaneTolerance);
            Assert.Equal(4f, PortalCap.EndReach);
            Assert.Equal(1f, PortalCap.MinRise);
        }

        [Fact]
        public void SlopeMeshRemovesExactlyTheFourCapTrianglesInOrder()
        {
            int[] cap;
            Mesh m = Slope(1f, out cap);
            int removed;
            int[] result = PortalCap.Remove(m.Pos.ToArray(), m.Idx.ToArray(), 0f, out removed);
            Assert.Equal(4, removed);
            Assert.Equal(m.Expected(cap), result);
        }

        [Fact]
        public void MirroredMeshRemovesCapsAtPositiveZ()
        {
            int[] cap;
            Mesh m = Slope(-1f, out cap);
            int removed;
            int[] result = PortalCap.Remove(m.Pos.ToArray(), m.Idx.ToArray(), 0f, out removed);
            Assert.Equal(4, removed);
            Assert.Equal(m.Expected(cap), result);
        }

        [Fact]
        public void NoRiseDifferenceRemovesNothing()
        {
            var m = new Mesh();
            Floor(m, 1f, 0.5f, -32f, 32f);
            m.ZQuad(-32f, -8, 8, -3, 0.5f);
            m.ZQuad(32f, -8, 8, -3, 0.5f);
            int removed;
            int[] result = PortalCap.Remove(m.Pos.ToArray(), m.Idx.ToArray(), 0f, out removed);
            Assert.Equal(0, removed);
            Assert.Equal(m.Idx.ToArray(), result);
        }

        [Fact]
        public void OffCentreProbeKeepsGroupThatDoesNotCoverIt()
        {
            int[] cap;
            Mesh m = Slope(1f, out cap);
            int removed;
            int[] result = PortalCap.Remove(m.Pos.ToArray(), m.Idx.ToArray(), 7.5f, out removed);
            // z=-30 quad spans x +-7 and misses 7.5; the z=-32 back quad (x +-8) covers it.
            Assert.Equal(2, removed);
            Assert.Equal(m.Expected(cap[2], cap[3]), result);
        }

        [Fact]
        public void GroupRemovedWhenOnlyOneTriangleContainsProbe()
        {
            var m = new Mesh();
            Floor(m, 1f, 0f, -32f, 32f);
            Floor(m, 1f, 6f, -32f, -16f);
            // Diagonal (-7,-1)-(7,7) passes x=0 at y=3, so (0,1.5) is inside only the first triangle.
            int cap = m.ZQuad(-30f, -7, 7, -1, 7);
            int removed;
            int[] result = PortalCap.Remove(m.Pos.ToArray(), m.Idx.ToArray(), 0f, out removed);
            Assert.Equal(2, removed);
            Assert.Equal(m.Expected(cap, cap + 1), result);
        }
    }
}
