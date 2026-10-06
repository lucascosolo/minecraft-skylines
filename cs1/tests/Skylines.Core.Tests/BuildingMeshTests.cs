using System;
using System.Collections.Generic;
using System.Linq;
using Skylines.Core.Geometry;
using Xunit;
using static Skylines.Core.Tests.TriHelpers;

namespace Skylines.Core.Tests
{
    internal static class MeshAsserts
    {
        public static float Area3D(Tri t)
        {
            var n = t.Normal();
            return 0.5f * (float)Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
        }

        public static float AreaXz(Tri t)
        {
            return 0.5f * Math.Abs(t.Normal()[1]);
        }

        public static float Dot(float[] a, float[] b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }

        public static void AssertInside(List<Tri> tris, float minX, float minZ, float maxX, float maxZ)
        {
            const float e = 1e-4f;
            foreach (var t in tris)
                foreach (var v in t.Verts())
                {
                    Assert.InRange(v[0], minX - e, maxX + e);
                    Assert.InRange(v[2], minZ - e, maxZ + e);
                }
        }
    }

    public class RectClipTests
    {
        private static TriangleBuffer Clip(float[] a, float[] b, float[] c, float minX, float minZ, float maxX, float maxZ, out int n, ushort flags = 7)
        {
            var buf = new TriangleBuffer();
            n = RectClip.Triangle(a[0], a[1], a[2], b[0], b[1], b[2], c[0], c[1], c[2], flags, minX, minZ, maxX, maxZ, buf);
            return buf;
        }

        [Fact]
        public void WhollyInsideIsCopiedVerbatim()
        {
            var buf = Clip(new float[] { 1, 2, 1 }, new float[] { 3, 5, 1 }, new float[] { 1, 3, 3 }, 0, 0, 4, 4, out int n, 9);
            Assert.Equal(1, n);
            Assert.Equal(1, buf.Count);
            Assert.Equal(new float[] { 1, 2, 1, 3, 5, 1, 1, 3, 3 }, buf.Positions.Take(9).ToArray());
            Assert.Equal(9, buf.Flags[0]);
        }

        [Fact]
        public void TouchingBoundaryFromInsideIsStillWhollyInside()
        {
            var buf = Clip(new float[] { 0, 0, 0 }, new float[] { 4, 0, 0 }, new float[] { 0, 0, 4 }, 0, 0, 4, 4, out int n);
            Assert.Equal(1, n);
            Assert.Equal(new float[] { 0, 0, 0, 4, 0, 0, 0, 0, 4 }, buf.Positions.Take(9).ToArray());
        }

        [Fact]
        public void WhollyOutsideAppendsNothing()
        {
            var buf = Clip(new float[] { 10, 0, 10 }, new float[] { 12, 0, 10 }, new float[] { 10, 0, 12 }, 0, 0, 4, 4, out int n);
            Assert.Equal(0, n);
            Assert.Equal(0, buf.Count);
        }

        [Fact]
        public void SharingOnlyAnEdgeAppendsNothing()
        {
            var buf = Clip(new float[] { 4, 0, 0 }, new float[] { 8, 0, 0 }, new float[] { 4, 0, 4 }, 0, 0, 4, 4, out int n);
            Assert.Equal(0, n);
            Assert.Equal(0, buf.Count);
        }

        [Fact]
        public void SharingOnlyACornerPointAppendsNothing()
        {
            var buf = Clip(new float[] { 4, 0, 4 }, new float[] { 8, 0, 4 }, new float[] { 4, 0, 8 }, 0, 0, 4, 4, out int n);
            Assert.Equal(0, n);
            Assert.Equal(0, buf.Count);
        }

        [Fact]
        public void PartialRightTriangleKeepsAnalyticAreaAndWinding()
        {
            // Triangle x>=0, z>=0, x+z<=8 clipped to x<=4, z<=8: area under z=8-x for x in [0,4] = 24.
            var a = new float[] { 0, 0, 0 }; var b = new float[] { 8, 0, 0 }; var c = new float[] { 0, 0, 8 };
            var buf = Clip(a, b, c, 0, 0, 4, 8, out int n);
            var tris = All(buf);
            Assert.True(n >= 1);
            Assert.Equal(n, tris.Count);
            MeshAsserts.AssertInside(tris, 0, 0, 4, 8);
            Assert.Equal(24f, tris.Sum(t => MeshAsserts.AreaXz(t)), 3);
            var src = new Tri { A = a, B = b, C = c }.Normal();
            foreach (var t in tris) Assert.True(MeshAsserts.Dot(t.Normal(), src) > 0);
        }

        [Fact]
        public void PiecesCarryTheGivenFlags()
        {
            var buf = Clip(new float[] { -2, 0, 0 }, new float[] { 6, 0, 0 }, new float[] { 0, 0, 6 }, 0, 0, 4, 4, out int n, 33);
            Assert.True(n >= 1);
            for (int i = 0; i < buf.Count; i++) Assert.Equal(33, buf.Flags[i]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TriangleCoveringTheRectYieldsTheRectAndAtMostFivePieces(bool reversed)
        {
            var a = new float[] { -100, 0, -100 }; var b = new float[] { 100, 0, -100 }; var c = new float[] { 0, 0, 100 };
            var buf = reversed ? Clip(a, c, b, 0, 0, 4, 4, out int n) : Clip(a, b, c, 0, 0, 4, 4, out n);
            var tris = All(buf);
            Assert.InRange(n, 1, 5);
            Assert.Equal(n, tris.Count);
            MeshAsserts.AssertInside(tris, 0, 0, 4, 4);
            Assert.Equal(16f, tris.Sum(t => MeshAsserts.AreaXz(t)), 3);
            var src = new Tri { A = a, B = reversed ? c : b, C = reversed ? b : c }.Normal();
            foreach (var t in tris) Assert.True(MeshAsserts.Dot(t.Normal(), src) > 0);
        }

        [Fact]
        public void SlopedTriangleStaysOnItsPlane()
        {
            // Plane y = 0.5x + 2.
            float Y(float x) { return 0.5f * x + 2; }
            var a = new float[] { -2, Y(-2), -2 }; var b = new float[] { 6, Y(6), -2 }; var c = new float[] { -2, Y(-2), 6 };
            var buf = Clip(a, b, c, 0, 0, 4, 4, out int n);
            var tris = All(buf);
            Assert.True(n >= 1);
            MeshAsserts.AssertInside(tris, 0, 0, 4, 4);
            foreach (var t in tris)
                foreach (var v in t.Verts())
                    Assert.True(Near(v[1], Y(v[0])), "vertex off plane: " + v[0] + "," + v[1]);
        }

        [Fact]
        public void VerticalWallIsClippedAlongX()
        {
            // Wall in z=5: triangle (-10,0)-(10,0)-(10,10) in (x,y); upper edge y=(x+10)/2.
            // Part with x in [0,4]: integral of (x+10)/2 = 24.
            var a = new float[] { -10, 0, 5 }; var b = new float[] { 10, 0, 5 }; var c = new float[] { 10, 10, 5 };
            var buf = Clip(a, b, c, 0, 0, 4, 10, out int n);
            var tris = All(buf);
            Assert.True(n >= 1);
            Assert.Equal(n, tris.Count);
            MeshAsserts.AssertInside(tris, 0, 0, 4, 10);
            Assert.Equal(24f, tris.Sum(t => MeshAsserts.Area3D(t)), 3);
            var src = new Tri { A = a, B = b, C = c }.Normal();
            foreach (var t in tris)
            {
                Assert.True(MeshAsserts.Dot(t.Normal(), src) > 0);
                foreach (var v in t.Verts()) Assert.True(Near(v[2], 5));
            }
        }

        [Fact]
        public void VerticalWallOutsideTheRectAppendsNothing()
        {
            var buf = Clip(new float[] { -10, 0, 50 }, new float[] { 10, 0, 50 }, new float[] { 10, 10, 50 }, 0, 0, 4, 10, out int n);
            Assert.Equal(0, n);
        }

        [Fact]
        public void NoPieceIsDegenerateWhenTheTriangleTouchesRectCorners()
        {
            var cases = new[]
            {
                new[] { new float[] { 0, 0, 0 }, new float[] { 8, 0, 2 }, new float[] { 2, 0, 8 } },
                new[] { new float[] { -4, 0, 4 }, new float[] { 4, 0, 12 }, new float[] { 4, 0, -4 } },
                new[] { new float[] { 4, 1, 4 }, new float[] { -4, 2, 4 }, new float[] { 0, 3, -4 } },
            };
            foreach (var cs in cases)
            {
                var buf = Clip(cs[0], cs[1], cs[2], 0, 0, 4, 4, out int n);
                var tris = All(buf);
                Assert.Equal(n, tris.Count);
                Assert.InRange(n, 0, 5);
                MeshAsserts.AssertInside(tris, 0, 0, 4, 4);
                foreach (var t in tris) Assert.True(MeshAsserts.Area3D(t) > 1e-6f);
            }
        }

        [Fact]
        public void AppendsAfterExistingContent()
        {
            var buf = new TriangleBuffer();
            buf.Add(0, 0, 0, 1, 0, 0, 0, 0, 1, 1);
            int n = RectClip.Triangle(1, 0, 1, 3, 0, 1, 1, 0, 3, 5, 0, 0, 4, 4, buf);
            Assert.Equal(1, n);
            Assert.Equal(2, buf.Count);
            Assert.Equal(5, buf.Flags[1]);
        }
    }

    public class TriangleBudgetTests
    {
        // Right triangle in xz with legs k: area k*k/2, flags = id.
        private static void Add(TriangleBuffer b, float k, ushort id)
        {
            b.Add(0, 0, 0, k, 0, 0, 0, 0, k, id);
        }

        private static ushort[] FlagList(TriangleBuffer b) { return b.Flags.Take(b.Count).ToArray(); }

        [Fact]
        public void UnderOrAtTheLimitChangesNothing()
        {
            var b = new TriangleBuffer();
            Add(b, 1, 1); Add(b, 2, 2); Add(b, 3, 3);
            Assert.Equal(0, TriangleBudget.KeepLargest(b, 0, 3));
            Assert.Equal(0, TriangleBudget.KeepLargest(b, 0, 10));
            Assert.Equal(new ushort[] { 1, 2, 3 }, FlagList(b));
        }

        [Fact]
        public void KeepsLargestAreasPreservingOrderAndFlags()
        {
            var b = new TriangleBuffer();
            Add(b, 1, 10); Add(b, 5, 11); Add(b, 2, 12); Add(b, 4, 13); Add(b, 3, 14);
            int dropped = TriangleBudget.KeepLargest(b, 0, 2);
            Assert.Equal(3, dropped);
            Assert.Equal(2, b.Count);
            Assert.Equal(new ushort[] { 11, 13 }, FlagList(b));
            Assert.Equal(new float[] { 0, 0, 0, 5, 0, 0, 0, 0, 5 }, b.Positions.Take(9).ToArray());
            Assert.Equal(new float[] { 0, 0, 0, 4, 0, 0, 0, 0, 4 }, b.Positions.Skip(9).Take(9).ToArray());
        }

        [Fact]
        public void TiesKeepTheEarlierTriangle()
        {
            var b = new TriangleBuffer();
            Add(b, 2, 1); Add(b, 2, 2); Add(b, 2, 3); Add(b, 2, 4);
            Assert.Equal(2, TriangleBudget.KeepLargest(b, 0, 2));
            Assert.Equal(new ushort[] { 1, 2 }, FlagList(b));
        }

        [Fact]
        public void AreaIsThreeDimensional()
        {
            var b = new TriangleBuffer();
            Add(b, 2, 1);                               // xz area 2
            b.Add(0, 0, 0, 10, 0, 0, 0, 10, 0, 2);      // vertical, xz area 0, 3D area 50
            Assert.Equal(1, TriangleBudget.KeepLargest(b, 0, 1));
            Assert.Equal(new ushort[] { 2 }, FlagList(b));
        }

        [Fact]
        public void TrianglesBeforeStartAreUntouched()
        {
            var b = new TriangleBuffer();
            Add(b, 1, 1); Add(b, 1, 2);                 // tiny, before start
            Add(b, 3, 3); Add(b, 9, 4); Add(b, 5, 5);
            Assert.Equal(1, TriangleBudget.KeepLargest(b, 2, 2));
            Assert.Equal(4, b.Count);
            Assert.Equal(new ushort[] { 1, 2, 4, 5 }, FlagList(b));
        }

        [Fact]
        public void ZeroMaxDropsTheWholeRange()
        {
            var b = new TriangleBuffer();
            Add(b, 1, 1); Add(b, 2, 2); Add(b, 3, 3);
            Assert.Equal(2, TriangleBudget.KeepLargest(b, 1, 0));
            Assert.Equal(1, b.Count);
            Assert.Equal(new ushort[] { 1 }, FlagList(b));
        }

        [Fact]
        public void EmptyRangeAtCountIsFine()
        {
            var b = new TriangleBuffer();
            Add(b, 1, 1);
            Assert.Equal(0, TriangleBudget.KeepLargest(b, 1, 0));
            Assert.Equal(1, b.Count);
        }

        [Theory]
        [InlineData(-1, 1)]
        [InlineData(3, 1)]
        [InlineData(0, -1)]
        public void OutOfRangeArgumentsThrow(int start, int max)
        {
            var b = new TriangleBuffer();
            Add(b, 1, 1); Add(b, 2, 2);
            Assert.Throws<ArgumentOutOfRangeException>(() => TriangleBudget.KeepLargest(b, start, max));
        }
    }

    public class SkirtTests
    {
        private static float[] Horizontal(float[] n) { return new[] { n[0], 0f, n[2] }; }

        [Fact]
        public void ToleranceConstantIsFourCentimetres()
        {
            Assert.Equal(0.04f, Skirt.GroundTolerance);
        }

        [Fact]
        public void WallWithGroundEdgeGetsAQuadBelowFacingTheSameWay()
        {
            var b = new TriangleBuffer();
            b.Add(0, 0, 0, 4, 0, 0, 2, 3, 0, 1);   // wall in z=0, normal +z
            int n = Skirt.Append(b, 0, 0f, 2f, 77);
            Assert.Equal(2, n);
            Assert.Equal(3, b.Count);
            var added = All(b).Skip(1).ToList();
            Assert.Equal(77, b.Flags[1]);
            Assert.Equal(77, b.Flags[2]);
            foreach (var t in added)
            {
                Assert.True(MeshAsserts.Dot(t.Normal(), new float[] { 0, 0, 1 }) > 0);
                foreach (var v in t.Verts())
                {
                    Assert.True(Near(v[1], 0) || Near(v[1], -2));
                    Assert.True(Near(v[0], 0) || Near(v[0], 4));
                    Assert.True(Near(v[2], 0));
                }
            }
            Assert.Equal(8f, added.Sum(t => MeshAsserts.Area3D(t)), 3);
        }

        [Fact]
        public void OppositeWindingFacesTheOtherWay()
        {
            var b = new TriangleBuffer();
            b.Add(0, 0, 0, 2, 3, 0, 4, 0, 0, 1);   // normal -z
            Assert.Equal(2, Skirt.Append(b, 0, 0f, 1f, 1));
            foreach (var t in All(b).Skip(1))
                Assert.True(MeshAsserts.Dot(t.Normal(), new float[] { 0, 0, -1 }) > 0);
        }

        [Fact]
        public void GroundVerticesAreSnappedToGroundY()
        {
            var b = new TriangleBuffer();
            b.Add(0, 10.03f, 0, 4, 9.97f, 0, 2, 13, 0, 1);
            Assert.Equal(2, Skirt.Append(b, 0, 10f, 3f, 1));
            foreach (var t in All(b).Skip(1))
                foreach (var v in t.Verts())
                    Assert.True(v[1] == 10f || Near(v[1], 7f), "y=" + v[1]);
        }

        [Fact]
        public void VertexJustOutsideToleranceDoesNotCount()
        {
            var b = new TriangleBuffer();
            b.Add(0, 0, 0, 4, 0.05f, 0, 2, 3, 0, 1);
            Assert.Equal(0, Skirt.Append(b, 0, 0f, 2f, 1));
            Assert.Equal(1, b.Count);
        }

        [Fact]
        public void DiagonalEdgeAreaIsLengthTimesDepth()
        {
            var b = new TriangleBuffer();
            b.Add(0, 0, 0, 3, 0, 4, 1.5f, 3, 2, 1);   // ground edge length 5
            Assert.Equal(2, Skirt.Append(b, 0, 0f, 2f, 1));
            Assert.Equal(10f, All(b).Skip(1).Sum(t => MeshAsserts.Area3D(t)), 3);
        }

        [Fact]
        public void SlopedTriangleSkirtHorizontalNormalAgreesWithSource()
        {
            var b = new TriangleBuffer();
            b.Add(0, 0, 0, 4, 0, 0, 2, 3, -3, 1);   // normal (0,12,12)
            var src = Horizontal(All(b)[0].Normal());
            Assert.Equal(2, Skirt.Append(b, 0, 0f, 1f, 1));
            foreach (var t in All(b).Skip(1))
                Assert.True(MeshAsserts.Dot(Horizontal(t.Normal()), src) > 0);
        }

        [Fact]
        public void FlatLotAtGroundProducesNothing()
        {
            var b = new TriangleBuffer();
            b.Add(0, 0, 0, 4, 0, 0, 0, 0, 4, 1);
            Assert.Equal(0, Skirt.Append(b, 0, 0f, 2f, 1));
            Assert.Equal(1, b.Count);
        }

        [Fact]
        public void OneGroundVertexProducesNothing()
        {
            var b = new TriangleBuffer();
            b.Add(0, 0, 0, 4, 2, 0, 2, 3, 0, 1);
            Assert.Equal(0, Skirt.Append(b, 0, 0f, 2f, 1));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        public void NonPositiveDepthAppendsNothing(float depth)
        {
            var b = new TriangleBuffer();
            b.Add(0, 0, 0, 4, 0, 0, 2, 3, 0, 1);
            Assert.Equal(0, Skirt.Append(b, 0, 0f, depth, 1));
            Assert.Equal(1, b.Count);
        }

        [Fact]
        public void OnlyTrianglesFromStartAreProcessedAndAppendedOnesAreNot()
        {
            var b = new TriangleBuffer();
            b.Add(0, 0, 0, 4, 0, 0, 2, 3, 0, 1);       // before start: ignored
            b.Add(0, 0, 5, 4, 0, 5, 2, 3, 5, 2);       // qualifies
            b.Add(0, 0, 8, 4, 0, 8, 2, 3, 8, 3);       // qualifies
            int n = Skirt.Append(b, 1, 0f, 2f, 9);
            Assert.Equal(4, n);
            Assert.Equal(7, b.Count);
            var added = All(b).Skip(3).ToList();
            Assert.Equal(2, added.Count(t => t.Verts().All(v => Near(v[2], 5))));
            Assert.Equal(2, added.Count(t => t.Verts().All(v => Near(v[2], 8))));
        }
    }
}
