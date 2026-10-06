using System;
using System.Collections.Generic;
using Skylines.Core.Geometry;
using Xunit;

namespace Skylines.Core.Tests
{
    public class TunnelProfileTests
    {
        private const float Inf = float.PositiveInfinity;

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

        private static List<TunnelSection> Build(Bezier3D l, Bezier3D r, float clearance, bool tunnel, Func<float, float, float> ground, out int n)
        {
            List<TunnelSection> s = new List<TunnelSection> { new TunnelSection() }; // must be cleared by Build
            n = TunnelProfile.Build(l, r, clearance, tunnel, 4f, 0.5f, ground, s);
            return s;
        }

        private static float Dist(float ax, float ay, float az, float bx, float by, float bz)
        {
            return (float)Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by) + (az - bz) * (az - bz));
        }

        [Fact]
        public void TopWithClearanceBelowHeadroomIsFloorPlusHeadroom()
        {
            Assert.Equal(2f + 8f, TunnelProfile.Top(2f, 6f, Inf), 3);
        }

        [Fact]
        public void TopWithClearanceAboveHeadroomIsFloorPlusClearance()
        {
            Assert.Equal(2f + 10f, TunnelProfile.Top(2f, 10f, Inf), 3);
        }

        [Fact]
        public void TopNeverDropsBelowFloorPlusClearanceWhateverTheCap()
        {
            Assert.Equal(0f + 6f, TunnelProfile.Top(0f, 6f, 3f), 3);
            Assert.Equal(0f + 6f, TunnelProfile.Top(0f, 6f, -20f), 3);
        }

        [Fact]
        public void TopBetweenClearanceAndHeadroomFollowsTheCap()
        {
            Assert.Equal(7f, TunnelProfile.Top(0f, 6f, 7f), 3);
            Assert.Equal(8f, TunnelProfile.Top(0f, 6f, 50f), 3);
        }

        [Fact]
        public void BuildGivesNPlusOneSectionsAndClearsTheList()
        {
            int n;
            var s = Build(Seg(0, 0, 0, 0, 0, 20), Seg(10, 0, 0, 10, 0, 20), 6f, true, null, out n);
            Assert.Equal(6, n); // 20 m / 4 m = 5 intervals
            Assert.Equal(6, s.Count);
            Assert.True(s[0].Covered && s[4].Covered);
            Assert.False(s[5].Covered, "the last section is never covered");
            Assert.Equal(0f, s[0].Lz, 3);
            Assert.Equal(20f, s[5].Lz, 3);
        }

        [Fact]
        public void InsetPointsAreExactlyInsetFromTheirEdgeAndOnTheSegmentBetween()
        {
            int n;
            Bezier3D l = Seg(0, 0, 0, 0, 0, 20), r = Seg(10, 3, 0, 10, 3, 20);
            var s = Build(l, r, 6f, true, null, out n);
            Assert.Equal(6, n);
            for (int k = 0; k < n; k++)
            {
                float t = k / (float)(n - 1);
                float[] p = l.At(t), q = r.At(t);
                float dl = Dist(s[k].Lx, s[k].Ly, s[k].Lz, p[0], p[1], p[2]);
                float dr = Dist(s[k].Rx, s[k].Ry, s[k].Rz, q[0], q[1], q[2]);
                float full = Dist(p[0], p[1], p[2], q[0], q[1], q[2]);
                Assert.Equal(0.5f, dl, 3);
                Assert.Equal(0.5f, dr, 3);
                Assert.Equal(full, dl + Dist(s[k].Lx, s[k].Ly, s[k].Lz, q[0], q[1], q[2]), 3);
                Assert.Equal(full, dr + Dist(s[k].Rx, s[k].Ry, s[k].Rz, p[0], p[1], p[2]), 3);
            }
        }

        [Fact]
        public void TunnelCapsInteriorSectionsByGroundMinusCoverButNotTheEnds()
        {
            int n;
            Func<float, float, float> ground = (x, z) => Math.Abs(x - 5f) < 1e-3f ? 8f : 100f; // asked at the edges' midpoint
            var s = Build(Seg(0, 0, 0, 0, 0, 20), Seg(10, 0, 0, 10, 0, 20), 6f, true, ground, out n);
            Assert.Equal(8f, s[0].Top, 3);
            Assert.Equal(8f, s[5].Top, 3);
            for (int k = 1; k < 5; k++) Assert.Equal(7f, s[k].Top, 3); // cap = 8 - Cover
        }

        [Fact]
        public void NullGroundMeansEveryTopIsFloorPlusHeadroom()
        {
            int n;
            var s = Build(Seg(0, 0, 0, 0, 0, 20), Seg(10, 1, 0, 10, 1, 20), 6f, true, null, out n);
            Assert.All(s, x => Assert.Equal(1f + 8f, x.Top, 3));
        }

        [Fact]
        public void DescendingSlopeCoversOnlyDeepIntervalsAndNeverRoofsAbovePortal()
        {
            int n;
            var s = Build(Seg(0, 0, 0, 0, -10, 40), Seg(10, 0, 0, 10, -10, 40), 6f, false, null, out n);
            int chords = n - 1;
            Assert.Equal(11, chords); // 3D chord 41.2 m / 4
            int covered = 0;
            for (int k = 0; k < chords; k++)
            {
                float mk = -10f * k / chords, mk1 = -10f * (k + 1) / chords;
                bool expected = Math.Max(mk, mk1) + 6f <= 0f;
                Assert.Equal(expected, s[k].Covered);
                if (expected)
                {
                    covered++;
                    Assert.True(s[k].Top <= 1e-3f, "roof of interval " + k + " pokes above the portal");
                    Assert.True(s[k + 1].Top <= 1e-3f);
                }
            }
            Assert.Equal(4, covered);
            Assert.False(s[chords].Covered);
        }

        [Fact]
        public void EdgesCloserThanTwiceTheInsetGiveZeroSections()
        {
            int n;
            var s = Build(Seg(0, 0, 0, 0, 0, 20), Seg(0.9f, 0, 0, 0.9f, 0, 20), 6f, true, null, out n);
            Assert.Equal(0, n);
            Assert.Empty(s);
        }
    }
}
