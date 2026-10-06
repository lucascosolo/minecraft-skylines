using System;
using System.Collections.Generic;
using Skylines.Core.Geometry;
using Xunit;

namespace Skylines.Core.Tests
{
    public class TunnelProfileTests
    {
        internal static Bezier3D Seg(float ax, float ay, float az, float dx, float dy, float dz)
        {
            return new Bezier3D
            {
                Ax = ax, Ay = ay, Az = az,
                Bx = ax + (dx - ax) / 3f, By = ay + (dy - ay) / 3f, Bz = az + (dz - az) / 3f,
                Cx = ax + 2 * (dx - ax) / 3f, Cy = ay + 2 * (dy - ay) / 3f, Cz = az + 2 * (dz - az) / 3f,
                Dx = dx, Dy = dy, Dz = dz,
            };
        }

        internal static TunnelDims Dims(float ratio, float lintel, float cover)
        {
            return new TunnelDims { InnerRatio = ratio, Lintel = lintel, CoverFraction = cover };
        }

        private static List<TunnelSection> Build(Bezier3D l, Bezier3D r, TunnelDims d, bool tunnel, out int n)
        {
            List<TunnelSection> s = new List<TunnelSection> { new TunnelSection() }; // must be cleared by Build
            n = TunnelProfile.Build(l, r, d, tunnel, 4f, s);
            return s;
        }

        // ---- Fallback ----

        [Fact]
        public void FallbackInsetsTwoMetresAndUsesDefaultLintelWithUnknownCover()
        {
            TunnelDims d = TunnelProfile.Fallback(8f);
            Assert.Equal(0.75f, d.InnerRatio, 4);
            Assert.Equal(6f, d.Lintel, 4);
            Assert.True(float.IsNaN(d.CoverFraction));
        }

        [Theory]
        [InlineData(2f)]
        [InlineData(1f)]
        public void FallbackRatioIsOneForRoadsNoWiderThanTheInset(float halfWidth)
        {
            Assert.Equal(1f, TunnelProfile.Fallback(halfWidth).InnerRatio, 4);
        }

        // ---- FromPortal ----

        private static void Quad(List<float> p, List<int> idx, float[] a, float[] b, float[] c, float[] d)
        {
            int o = p.Count / 3;
            foreach (float[] v in new[] { a, b, c, d }) p.AddRange(v);
            idx.AddRange(new[] { o, o + 1, o + 2, o, o + 2, o + 3 });
        }

        // Mimics CS1 small-tunnel-slope: x +-8, z +-32. zSign = 1 puts the roof at z -32..-8, -1 mirrors it.
        private static void PortalMesh(float zSign, out float[] pos, out int[] idx)
        {
            List<float> p = new List<float>(); List<int> i = new List<int>();
            float za = -32f * zSign, zb = -8f * zSign;
            // low geometry out to |x| 8, plus a sidewalk nearer the centre line
            Quad(p, i, new[] { -8f, 0f, -32f }, new[] { 8f, 0f, -32f }, new[] { 8f, 0f, 32f }, new[] { -8f, 0f, 32f });
            Quad(p, i, new[] { -3f, 1f, -32f }, new[] { 3f, 1f, -32f }, new[] { 3f, 1f, 32f }, new[] { -3f, 1f, 32f });
            // walls above 2 m at |x| 6..8
            Quad(p, i, new[] { -6f, 2.5f, -32f }, new[] { -6f, 2.5f, 32f }, new[] { -6f, 6f, 32f }, new[] { -6f, 6f, -32f });
            Quad(p, i, new[] { 6f, 2.5f, -32f }, new[] { 6f, 2.5f, 32f }, new[] { 6f, 6f, 32f }, new[] { 6f, 6f, -32f });
            Quad(p, i, new[] { -8f, 2.5f, -32f }, new[] { -8f, 2.5f, 32f }, new[] { -8f, 4f, 32f }, new[] { -8f, 4f, -32f });
            Quad(p, i, new[] { 8f, 2.5f, -32f }, new[] { 8f, 2.5f, 32f }, new[] { 8f, 4f, 32f }, new[] { 8f, 4f, -32f });
            // roof underside at y 6 spanning x -6..6, and a higher roof/facade up to y 12 spanning wider
            Quad(p, i, new[] { -6f, 6f, za }, new[] { 6f, 6f, za }, new[] { 6f, 6f, zb }, new[] { -6f, 6f, zb });
            Quad(p, i, new[] { -8f, 12f, za }, new[] { 8f, 12f, za }, new[] { 8f, 12f, zb }, new[] { -8f, 12f, zb });
            pos = p.ToArray(); idx = i.ToArray();
        }

        [Theory]
        [InlineData(1f)]
        [InlineData(-1f)]
        public void FromPortalReadsRatioLintelAndCoverFromTheRoofedHalf(float zSign)
        {
            float[] pos; int[] idx; TunnelDims d;
            PortalMesh(zSign, out pos, out idx);
            Assert.True(TunnelProfile.FromPortal(pos, idx, 8f, out d));
            Assert.Equal(0.75f, d.InnerRatio, 4);
            Assert.Equal(6f, d.Lintel, 4);
            Assert.Equal(0.375f, d.CoverFraction, 4);
        }

        [Fact]
        public void FromPortalIgnoresLowGeometryNearTheCentreLine()
        {
            float[] pos; int[] idx; TunnelDims d;
            PortalMesh(1f, out pos, out idx);
            Assert.True(TunnelProfile.FromPortal(pos, idx, 8f, out d));
            Assert.Equal(6f / 8f, d.InnerRatio, 4); // the y=0 and y=1 quads reach x=0 and 3 but are not "high"
        }

        [Fact]
        public void FromPortalFailsWithNoGeometryAboveTwoMetres()
        {
            List<float> p = new List<float>(); List<int> i = new List<int>();
            Quad(p, i, new[] { -8f, 0f, -32f }, new[] { 8f, 0f, -32f }, new[] { 8f, 2f, 32f }, new[] { -8f, 2f, 32f });
            TunnelDims d;
            Assert.False(TunnelProfile.FromPortal(p.ToArray(), i.ToArray(), 8f, out d));
        }

        [Fact]
        public void FromPortalFailsWhenNoHighTriangleSpansTheCentreLine()
        {
            List<float> p = new List<float>(); List<int> i = new List<int>();
            Quad(p, i, new[] { 6f, 3f, -32f }, new[] { 8f, 3f, -32f }, new[] { 8f, 6f, 32f }, new[] { 6f, 6f, 32f });
            TunnelDims d;
            Assert.False(TunnelProfile.FromPortal(p.ToArray(), i.ToArray(), 8f, out d));
        }

        // ---- Inset ----

        [Fact]
        public void InsetIsTheLerpTowardsTheOtherEdgeAtEveryT()
        {
            Bezier3D l = new Bezier3D { Ax = 0, Ay = 0, Az = 0, Bx = 1, By = 1, Bz = 7, Cx = 3, Cy = 2, Cz = 12, Dx = 2, Dy = 4, Dz = 20 };
            Bezier3D r = new Bezier3D { Ax = 10, Ay = 1, Az = 0, Bx = 12, By = 0, Bz = 8, Cx = 11, Cy = 3, Cz = 13, Dx = 13, Dy = 5, Dz = 21 };
            Bezier3D inset = TunnelProfile.Inset(l, r, 0.75f);
            foreach (float t in new[] { 0f, 0.2f, 0.5f, 0.9f, 1f })
            {
                float[] a = inset.At(t), p = l.At(t), q = r.At(t);
                for (int k = 0; k < 3; k++) Assert.Equal(p[k] + (q[k] - p[k]) * 0.125f, a[k], 3);
            }
        }

        [Fact]
        public void InsetWithRatioOneIsTheEdgeItself()
        {
            Bezier3D l = Seg(0, 0, 0, 1, 2, 20), r = Seg(10, 0, 0, 11, 2, 20);
            float[] a = TunnelProfile.Inset(l, r, 1f).At(0.4f), p = l.At(0.4f);
            for (int k = 0; k < 3; k++) Assert.Equal(p[k], a[k], 4);
        }

        [Fact]
        public void InsetMatchesTheBuiltSectionEdgesAtSeveralT()
        {
            Bezier3D l = Seg(0, 0, 0, 0, 3, 20), r = Seg(10, 1, 0, 10, 4, 20);
            TunnelDims d = Dims(0.8f, 6f, float.NaN);
            int n;
            var s = Build(l, r, d, true, out n);
            Bezier3D il = TunnelProfile.Inset(l, r, 0.8f), ir = TunnelProfile.Inset(r, l, 0.8f);
            for (int k = 0; k < n; k++)
            {
                float t = k / (float)(n - 1);
                float[] a = il.At(t), b = ir.At(t);
                Assert.Equal(a[0], s[k].Lx, 3); Assert.Equal(a[1], s[k].Ly, 3); Assert.Equal(a[2], s[k].Lz, 3);
                Assert.Equal(b[0], s[k].Rx, 3); Assert.Equal(b[1], s[k].Ry, 3); Assert.Equal(b[2], s[k].Rz, 3);
            }
        }

        // ---- Build ----

        [Fact]
        public void BuildGivesNPlusOneSectionsAndClearsTheList()
        {
            int n;
            var s = Build(Seg(0, 0, 0, 0, 0, 20), Seg(10, 0, 0, 10, 0, 20), Dims(0.9f, 6f, float.NaN), true, out n);
            Assert.Equal(6, n); // 20 m / 4 m = 5 intervals
            Assert.Equal(6, s.Count);
            Assert.True(s[0].Covered && s[4].Covered);
            Assert.False(s[5].Covered, "the last section is never covered");
            Assert.Equal(0f, s[0].Lz, 3);
            Assert.Equal(20f, s[5].Lz, 3);
        }

        [Fact]
        public void ZeroInnerRatioGivesZeroSections()
        {
            int n;
            var s = Build(Seg(0, 0, 0, 0, 0, 20), Seg(10, 0, 0, 10, 0, 20), Dims(0f, 6f, float.NaN), true, out n);
            Assert.Equal(0, n);
            Assert.Empty(s);
        }

        // Owner bug 1: walls exactly +-6 from the centre line, ceiling exactly mean edge y + 6, following a grade.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BasicRoadWallsAreAtSixMetresAndCeilingFollowsTheGrade(bool useFallback)
        {
            TunnelDims d = useFallback ? TunnelProfile.Fallback(8f) : Dims(0.75f, 6f, float.NaN);
            int n;
            var s = Build(Seg(-8, 0, 0, -8, 5, 40), Seg(8, 0, 0, 8, 5, 40), d, true, out n);
            Assert.Equal(12, n); // 3D chord 40.3 m / 4 m = 11 intervals
            for (int k = 0; k < n; k++)
            {
                float y = 5f * s[k].Lz / 40f;
                Assert.Equal(-6f, s[k].Lx, 4);
                Assert.Equal(6f, s[k].Rx, 4);
                Assert.Equal(y, s[k].Ly, 3);
                Assert.Equal(y + 6f, s[k].Ceiling, 3);
            }
        }

        [Fact]
        public void CeilingIsLevelAcrossTheWidthAtMeanEdgeHeightPlusLintel()
        {
            int n;
            var s = Build(Seg(0, 0, 0, 0, 0, 20), Seg(10, 2, 0, 10, 2, 20), Dims(0.9f, 5f, float.NaN), true, out n);
            Assert.All(s, x => Assert.Equal(1f + 5f, x.Ceiling, 4));
        }

        // Owner bug 4: the cover boundary.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SlopeInsertsASectionAtTheCoverBoundaryAndCoversOnlyTheLowerSide(bool lowerEndIsD)
        {
            int n;
            Bezier3D l = lowerEndIsD ? Seg(0, 0, 0, 0, -10, 40) : Seg(0, -10, 0, 0, 0, 40);
            Bezier3D r = lowerEndIsD ? Seg(10, 0, 0, 10, -10, 40) : Seg(10, -10, 0, 10, 0, 40);
            var s = Build(l, r, Dims(0.9f, 6f, 0.375f), false, out n);
            Assert.Equal(13, n); // 11 regular intervals plus the boundary section
            float tcZ = lowerEndIsD ? 25f : 15f; // z = 40 t on a straight edge
            Assert.Contains(s, x => Math.Abs(x.Lz - tcZ) < 1e-3f);
            for (int k = 0; k < n - 1; k++)
            {
                float mid = (s[k].Lz + s[k + 1].Lz) / 2f;
                bool expected = lowerEndIsD ? mid > tcZ : mid < tcZ;
                Assert.Equal(expected, s[k].Covered);
            }
            Assert.False(s[n - 1].Covered);
        }

        [Fact]
        public void CoverFractionOfOneInsertsNoExtraSection()
        {
            int n;
            Build(Seg(0, 0, 0, 0, -10, 40), Seg(10, 0, 0, 10, -10, 40), Dims(0.9f, 6f, 1f), false, out n);
            Assert.Equal(12, n);
        }

        [Fact]
        public void BoundaryCoincidingWithAnExistingSectionIsNotDuplicated()
        {
            int n;
            // 15 m edge, 4 m step: n = 4, so t = 0.5 is already a section; lower end is A.
            Build(Seg(0, -0.001f, 0, 0, 0, 15), Seg(10, -0.001f, 0, 10, 0, 15), Dims(0.9f, 6f, 0.5f), false, out n);
            Assert.Equal(5, n);
        }

        [Fact]
        public void SlopeWithUnknownCoverUsesTheHeightRule()
        {
            int n;
            var s = Build(Seg(0, 0, 0, 0, -10, 40), Seg(10, 0, 0, 10, -10, 40), Dims(0.9f, 6f, float.NaN), false, out n);
            int chords = n - 1;
            Assert.Equal(11, chords);
            int covered = 0;
            for (int k = 0; k < chords; k++)
            {
                float mk = -10f * k / chords, mk1 = -10f * (k + 1) / chords;
                bool expected = Math.Max(mk, mk1) + 6f <= 0f;
                Assert.Equal(expected, s[k].Covered);
                if (expected) covered++;
            }
            Assert.Equal(4, covered);
            Assert.False(s[chords].Covered);
        }

        // Owner bug 2: a bend leaves no seam between a segment and the joint piece that follows it.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SegmentAndFollowingJointShareTheirBoundarySection(bool swapped)
        {
            TunnelDims d = Dims(0.75f, 6f, float.NaN);
            Bezier3D sl = Seg(0, 1, 0, 0, 3, 20), sr = Seg(10, 1, 0, 10, 3, 20);
            Bezier3D jl = new Bezier3D { Ax = 0, Ay = 3, Az = 20, Bx = 0, By = 3, Bz = 26, Cx = 2, Cy = 3, Cz = 30, Dx = 6, Dy = 4, Dz = 33 };
            Bezier3D jr = new Bezier3D { Ax = 10, Ay = 3, Az = 20, Bx = 10, By = 3, Bz = 28, Cx = 12, Cy = 3, Cz = 34, Dx = 14, Dy = 4, Dz = 40 };
            int n, m;
            var seg = Build(sl, sr, d, true, out n);
            var joint = swapped ? Build(jr, jl, d, true, out m) : Build(jl, jr, d, true, out m);
            TunnelSection a = seg[n - 1], b = joint[0];
            Assert.Equal(a.Ceiling, b.Ceiling, 4);
            float[] la = { a.Lx, a.Ly, a.Lz }, ra = { a.Rx, a.Ry, a.Rz }, lb = { b.Lx, b.Ly, b.Lz }, rb = { b.Rx, b.Ry, b.Rz };
            bool same = Close(la, lb) && Close(ra, rb), crossed = Close(la, rb) && Close(ra, lb);
            Assert.True(same || crossed, "boundary sections differ");
        }

        // Owner bug 3: slope to tunnel joint.
        [Fact]
        public void SlopeEndAndFollowingTunnelPieceShareSectionAndSlopeEndsCovered()
        {
            Bezier3D sl = Seg(0, 0, 0, 0, -10, 40), sr = Seg(10, 0, 0, 10, -10, 40);
            Bezier3D jl = new Bezier3D { Ax = 0, Ay = -10, Az = 40, Bx = 0, By = -10, Bz = 46, Cx = 2, Cy = -10, Cz = 50, Dx = 6, Dy = -10, Dz = 53 };
            Bezier3D jr = Seg(10, -10, 40, 14, -10, 60);
            int n, m;
            var slope = Build(sl, sr, Dims(0.75f, 6f, 0.375f), false, out n);
            var tunnel = Build(jl, jr, Dims(0.75f, 6f, 0.375f), true, out m);
            TunnelSection a = slope[n - 1], b = tunnel[0];
            Assert.Equal(a.Lx, b.Lx, 4); Assert.Equal(a.Ly, b.Ly, 4); Assert.Equal(a.Lz, b.Lz, 4);
            Assert.Equal(a.Rx, b.Rx, 4); Assert.Equal(a.Ry, b.Ry, 4); Assert.Equal(a.Rz, b.Rz, 4);
            Assert.Equal(a.Ceiling, b.Ceiling, 4);
            Assert.Equal(-4f, a.Ceiling, 3);
            Assert.True(slope[n - 2].Covered, "the slope's last interval must be roofed");
        }

        private static bool Close(float[] a, float[] b)
        {
            return Math.Abs(a[0] - b[0]) < 1e-4f && Math.Abs(a[1] - b[1]) < 1e-4f && Math.Abs(a[2] - b[2]) < 1e-4f;
        }
    }
}
