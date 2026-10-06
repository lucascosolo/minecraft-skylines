using System;
using System.Collections.Generic;
using Skylines.Core.Geometry;
using Xunit;

namespace Skylines.Core.Tests
{
    public class TunnelShellTests
    {
        private static Bezier3D Line(float x, float y0, float y1, float z0, float z1)
        {
            return TunnelProfileTests.Seg(x, y0, z0, x, y1, z1);
        }

        private static TunnelDims D(float ratio, float cover = float.NaN)
        {
            return TunnelProfileTests.Dims(ratio, 6f, cover);
        }

        private static List<TunnelSection> Sections(Bezier3D l, Bezier3D r, bool tunnel, float cover = float.NaN)
        {
            List<TunnelSection> s = new List<TunnelSection>();
            TunnelProfile.Build(l, r, D(0.9f, cover), tunnel, 4f, s);
            return s;
        }

        private static ShellMesh LevelMesh(out int covered, out List<TunnelSection> s)
        {
            s = Sections(Line(0, 0, 0, 0, 20), Line(10, 0, 0, 0, 20), true);
            ShellMesh m = new ShellMesh();
            covered = TunnelShell.Segment(s, 8f, m);
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

        private static bool HasVertex(ShellMesh m, float x, float y, float z)
        {
            for (int v = 0; v < m.VertexCount; v++)
                if (Math.Abs(m.Positions[v * 3] - x) < 1e-3f && Math.Abs(m.Positions[v * 3 + 1] - y) < 1e-3f && Math.Abs(m.Positions[v * 3 + 2] - z) < 1e-3f) return true;
            return false;
        }

        private static List<float[]> VerticesAtZ(ShellMesh m, float z)
        {
            List<float[]> r = new List<float[]>();
            for (int v = 0; v < m.VertexCount; v++)
                if (Math.Abs(m.Positions[v * 3 + 2] - z) < 1e-3f) r.Add(new[] { m.Positions[v * 3], m.Positions[v * 3 + 1], m.Positions[v * 3 + 2] });
            return r;
        }

        private static void AssertSameSection(TunnelSection a, TunnelSection b)
        {
            Assert.Equal(a.Lx, b.Lx, 4); Assert.Equal(a.Ly, b.Ly, 4); Assert.Equal(a.Lz, b.Lz, 4);
            Assert.Equal(a.Rx, b.Rx, 4); Assert.Equal(a.Ry, b.Ry, 4); Assert.Equal(a.Rz, b.Rz, 4);
            Assert.Equal(a.Ceiling, b.Ceiling, 4);
        }

        private static TunnelSection Mouth(float lx, float rx, float lz, float rz, float y, float top)
        {
            return new TunnelSection { Lx = lx, Ly = y, Lz = lz, Rx = rx, Ry = y, Rz = rz, Ceiling = top, Covered = true };
        }

        // ---- Segment ----

        [Fact]
        public void SegmentCoversEveryIntervalOfALevelTunnelAndIsWellFormed()
        {
            int covered; List<TunnelSection> s;
            ShellMesh m = LevelMesh(out covered, out s);
            Assert.Equal(5, covered);
            Assert.True(m.VertexCount > 0);
            AssertWellFormed(m);
        }

        [Fact]
        public void WallVerticesSitOnInsetPointsFromFootToCeilingUnderside()
        {
            int covered; List<TunnelSection> s;
            ShellMesh m = LevelMesh(out covered, out s);
            for (int k = 0; k < s.Count; k++)
            {
                Assert.True(HasVertex(m, s[k].Lx, s[k].Ly - TunnelShell.WallFoot, s[k].Lz), "left foot " + k);
                Assert.True(HasVertex(m, s[k].Lx, s[k].Ceiling, s[k].Lz), "left top " + k);
                Assert.True(HasVertex(m, s[k].Rx, s[k].Ry - TunnelShell.WallFoot, s[k].Rz), "right foot " + k);
                Assert.True(HasVertex(m, s[k].Rx, s[k].Ceiling, s[k].Rz), "right top " + k);
            }
        }

        [Fact]
        public void SegmentNormalsFaceTheOtherSideAndDown()
        {
            int covered; List<TunnelSection> s;
            ShellMesh m = LevelMesh(out covered, out s);
            int left = 0, right = 0, ceiling = 0;
            for (int v = 0; v < m.VertexCount; v++)
            {
                if (NormalIs(m, v, 1, 0, 0)) { left++; Assert.Equal(0.5f, m.Positions[v * 3], 3); }
                else if (NormalIs(m, v, -1, 0, 0)) { right++; Assert.Equal(9.5f, m.Positions[v * 3], 3); }
                else if (NormalIs(m, v, 0, -1, 0)) { ceiling++; Assert.Equal(6f, m.Positions[v * 3 + 1], 3); }
                else Assert.Fail("unexpected normal at vertex " + v);
            }
            Assert.True(left > 0 && right > 0 && ceiling > 0);
        }

        [Fact]
        public void EverySegmentTriangleWindsTowardsItsNormal()
        {
            int covered; List<TunnelSection> s;
            AssertWinding(LevelMesh(out covered, out s));
        }

        [Fact]
        public void SegmentOnASlopeReturnsOnlyTheCoveredIntervalsAndWindsCorrectly()
        {
            var s = Sections(Line(0, 0, -10, 0, 40), Line(10, 0, -10, 0, 40), false);
            int expected = 0;
            foreach (TunnelSection t in s) if (t.Covered) expected++;
            Assert.Equal(4, expected);
            ShellMesh m = new ShellMesh();
            Assert.Equal(expected, TunnelShell.Segment(s, 8f, m));
            AssertWellFormed(m);
            AssertWinding(m);
        }

        [Fact]
        public void SegmentWithNoCoveredIntervalAppendsNothing()
        {
            var s = Sections(Line(0, 0, 0, 0, 20), Line(10, 0, 0, 0, 20), false);
            ShellMesh m = new ShellMesh();
            Assert.Equal(0, TunnelShell.Segment(s, 8f, m));
            Assert.Equal(0, m.VertexCount);
        }

        [Fact]
        public void SegmentAppendingKeepsEarlierVerticesAndOffsetsIndices()
        {
            ShellMesh m = new ShellMesh();
            m.Add(1, 2, 3, 0, 1, 0, 0, 0);
            m.Add(4, 5, 6, 0, 1, 0, 0, 0);
            int before = m.VertexCount;
            var s = Sections(Line(0, 0, 0, 0, 20), Line(10, 0, 0, 0, 20), true);
            TunnelShell.Segment(s, 8f, m);
            Assert.True(m.VertexCount > before);
            Assert.Equal(1f, m.Positions[0]);
            Assert.Equal(6f, m.Positions[5]);
            Assert.All(m.Indices, i => Assert.InRange(i, before, m.VertexCount - 1));
            AssertWellFormed(m);
        }

        [Fact]
        public void CeilingIsLevelAcrossTheWidthEvenWhenTheEdgesDifferInHeight()
        {
            var s = Sections(Line(0, 0, 0, 0, 20), Line(10, 1, 1, 0, 20), true);
            ShellMesh m = new ShellMesh();
            TunnelShell.Segment(s, 8f, m);
            int ceiling = 0;
            for (int v = 0; v < m.VertexCount; v++)
            {
                if (!NormalIs(m, v, 0, -1, 0)) continue;
                ceiling++;
                Assert.Equal(0.5f + 6f, m.Positions[v * 3 + 1], 3);
            }
            Assert.True(ceiling > 0);
            AssertWinding(m);
        }

        [Fact]
        public void CeilingUndersideIsExactlyLintelAboveALevelRoad()
        {
            var s = Sections(Line(0, 0, 0, 0, 20), Line(10, 0, 0, 0, 20), true);
            ShellMesh m = new ShellMesh();
            TunnelShell.Segment(s, 8f, m);
            int ceiling = 0;
            for (int v = 0; v < m.VertexCount; v++)
                if (NormalIs(m, v, 0, -1, 0)) { ceiling++; Assert.Equal(6f, m.Positions[v * 3 + 1], 3); }
            Assert.True(ceiling > 0);
        }

        // ---- Joints between consecutive pieces ----

        private static void BuildThreePieces(bool firstIsSlope, out List<TunnelSection> a, out List<TunnelSection> j, out List<TunnelSection> b,
            out ShellMesh ma, out ShellMesh mj, out ShellMesh mb)
        {
            float y = firstIsSlope ? -10f : 0f;
            a = firstIsSlope
                ? Sections(Line(0, 0, -10, 0, 40), Line(10, 0, -10, 0, 40), false, 0.375f)
                : Sections(Line(0, 0, 0, 0, 20), Line(10, 0, 0, 0, 20), true);
            float z0 = firstIsSlope ? 40f : 20f;
            Bezier3D jl = new Bezier3D { Ax = 0, Ay = y, Az = z0, Bx = 0, By = y, Bz = z0 + 5, Cx = 1, Cy = y, Cz = z0 + 8, Dx = 2, Dy = y, Dz = z0 + 10 };
            Bezier3D jr = new Bezier3D { Ax = 10, Ay = y, Az = z0, Bx = 10, By = y, Bz = z0 + 5, Cx = 11, Cy = y, Cz = z0 + 8, Dx = 12, Dy = y, Dz = z0 + 10 };
            j = Sections(jl, jr, true);
            b = Sections(TunnelProfileTests.Seg(2, y, z0 + 10, 4, y, z0 + 30), TunnelProfileTests.Seg(12, y, z0 + 10, 14, y, z0 + 30), true);
            ma = new ShellMesh(); mj = new ShellMesh(); mb = new ShellMesh();
            TunnelShell.Segment(a, 8f, ma);
            TunnelShell.Segment(j, 8f, mj);
            TunnelShell.Segment(b, 8f, mb);
        }

        private static void AssertWallsMeet(ShellMesh from, ShellMesh to, float z)
        {
            List<float[]> edge = VerticesAtZ(from, z);
            Assert.NotEmpty(edge);
            foreach (float[] p in edge) Assert.True(HasVertex(to, p[0], p[1], p[2]), "gap: no vertex at " + p[0] + "," + p[1] + "," + p[2]);
        }

        [Fact]
        public void ConsecutiveTunnelPiecesShareCornerSectionsAndWallVertices()
        {
            List<TunnelSection> a, j, b; ShellMesh ma, mj, mb;
            BuildThreePieces(false, out a, out j, out b, out ma, out mj, out mb);
            AssertSameSection(a[a.Count - 1], j[0]);
            AssertSameSection(j[j.Count - 1], b[0]);
            AssertWallsMeet(ma, mj, 20f);
            AssertWallsMeet(mj, mb, 30f);
        }

        [Fact]
        public void SlopeToTunnelJointMatchesAndSlopeEndsCovered()
        {
            List<TunnelSection> a, j, b; ShellMesh ma, mj, mb;
            BuildThreePieces(true, out a, out j, out b, out ma, out mj, out mb);
            AssertSameSection(a[a.Count - 1], j[0]);
            AssertSameSection(j[j.Count - 1], b[0]);
            Assert.True(a[a.Count - 2].Covered, "the slope's last interval is roofed");
            Assert.Equal(-4f, j[0].Ceiling, 3);
            foreach (TunnelSection t in j) Assert.Equal(t.Ly + 6f, t.Ceiling, 3);
            AssertWallsMeet(ma, mj, 40f);
            AssertWallsMeet(mj, mb, 50f);
        }

        // Owner bug 2: a bend. A segment and a joint curving away (also with sides swapped) share corner vertices.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BendJointSharesCeilingCornersWithTheSegmentBeforeIt(bool swapped)
        {
            var seg = Sections(Line(0, 0, 2, 0, 20), Line(10, 0, 2, 0, 20), true);
            Bezier3D jl = new Bezier3D { Ax = 0, Ay = 2, Az = 20, Bx = 0, By = 2, Bz = 26, Cx = 2, Cy = 2, Cz = 30, Dx = 6, Dy = 2, Dz = 33 };
            Bezier3D jr = new Bezier3D { Ax = 10, Ay = 2, Az = 20, Bx = 10, By = 2, Bz = 28, Cx = 12, Cy = 2, Cz = 34, Dx = 14, Dy = 2, Dz = 40 };
            var joint = swapped ? Sections(jr, jl, true) : Sections(jl, jr, true);
            TunnelSection a = seg[seg.Count - 1], b = joint[0];
            Assert.Equal(a.Ceiling, b.Ceiling, 4);
            ShellMesh ms = new ShellMesh(), mj = new ShellMesh();
            TunnelShell.Segment(seg, 8f, ms);
            TunnelShell.Segment(joint, 8f, mj);
            foreach (float[] c in new[] { new[] { a.Lx, a.Lz }, new[] { a.Rx, a.Rz } })
            {
                Assert.True(HasVertex(ms, c[0], a.Ceiling, c[1]), "segment ceiling corner missing");
                Assert.True(HasVertex(mj, c[0], a.Ceiling, c[1]), "joint ceiling corner missing");
            }
        }

        // ---- Junction ----

        // First section of an arm built by Build, 16 m wide, leaving (x0,z0) towards (x1,z1).
        private static TunnelSection Arm(float x0, float z0, float x1, float z1, float y, bool flip)
        {
            float dx = z1 - z0, dz = -(x1 - x0), len = (float)Math.Sqrt(dx * dx + dz * dz);
            dx = dx / len * 8f; dz = dz / len * 8f; // 8 m to one side of travel
            Bezier3D l = TunnelProfileTests.Seg(x0 + dx, y, z0 + dz, x1 + dx, y, z1 + dz);
            Bezier3D r = TunnelProfileTests.Seg(x0 - dx, y, z0 - dz, x1 - dx, y, z1 - dz);
            List<TunnelSection> s = new List<TunnelSection>();
            TunnelDims d = TunnelProfileTests.Dims(0.75f, 6f, float.NaN);
            if (flip) TunnelProfile.Build(r, l, d, true, 4f, s);
            else TunnelProfile.Build(l, r, d, true, 4f, s);
            return s[0];
        }

        // T-junction round the origin: south, east and west mouths (south given first, out of angular order); south is one metre higher.
        private static List<TunnelSection> TMouths()
        {
            return new List<TunnelSection>
            {
                Arm(0, 10, 0, 30, 1f, false),
                Arm(10, 0, 30, 0, 0f, false),
                Arm(-10, 0, -30, 0, 0f, true),
            };
        }

        private static List<float[]> Ring(IList<TunnelSection> mouths, out List<int> owner)
        {
            List<float[]> pts = new List<float[]>();
            owner = new List<int>();
            for (int i = 0; i < mouths.Count; i++)
            {
                pts.Add(new[] { mouths[i].Lx, mouths[i].Ly, mouths[i].Lz, mouths[i].Ceiling });
                pts.Add(new[] { mouths[i].Rx, mouths[i].Ry, mouths[i].Rz, mouths[i].Ceiling });
                owner.Add(i); owner.Add(i);
            }
            return pts;
        }

        private static int HorizontalTriangles(ShellMesh m)
        {
            int c = 0;
            for (int t = 0; t < m.Indices.Count; t += 3) if (Math.Abs(m.Normals[m.Indices[t] * 3 + 1]) < 1e-4f) c++;
            return c;
        }

        [Fact]
        public void JunctionOfThreeMouthsHasExactlyThreeWalls()
        {
            ShellMesh m = new ShellMesh();
            int n = TunnelShell.Junction(0, 0, TMouths(), 8f, m);
            Assert.Equal(2 * 6 + 2 * 3, n);
            Assert.Equal(n * 3, m.Indices.Count);
            Assert.Equal(6, HorizontalTriangles(m));
            AssertWellFormed(m);
            AssertWinding(m);
        }

        [Fact]
        public void JunctionWallsBridgeTheGapsBetweenMouthsNeverAMouth()
        {
            var mouths = TMouths();
            ShellMesh m = new ShellMesh();
            TunnelShell.Junction(0, 0, mouths, 8f, m);
            List<int> owner;
            List<float[]> ring = Ring(mouths, out owner);
            List<int> order = new List<int>();
            for (int i = 0; i < ring.Count; i++) order.Add(i);
            order.Sort((p, q) => Math.Atan2(ring[p][2], ring[p][0]).CompareTo(Math.Atan2(ring[q][2], ring[q][0])));
            List<float[]> wallVerts = new List<float[]>();
            for (int v = 0; v < m.VertexCount; v++)
                if (Math.Abs(m.Normals[v * 3 + 1]) < 1e-4f) wallVerts.Add(new[] { m.Positions[v * 3], m.Positions[v * 3 + 1], m.Positions[v * 3 + 2] });
            Func<float, float, float, bool> hasWall = (x, y, z) => wallVerts.Exists(w => Math.Abs(w[0] - x) < 1e-3f && Math.Abs(w[1] - y) < 1e-3f && Math.Abs(w[2] - z) < 1e-3f);
            int walls = 0;
            for (int i = 0; i < order.Count; i++)
            {
                int p = order[i], q = order[(i + 1) % order.Count];
                if (owner[p] == owner[q]) continue;
                walls++;
                float[] a = ring[p], b = ring[q];
                Assert.True(hasWall(a[0], a[1] - TunnelShell.WallFoot, a[2]) && hasWall(a[0], a[3], a[2])
                         && hasWall(b[0], b[1] - TunnelShell.WallFoot, b[2]) && hasWall(b[0], b[3], b[2]),
                    "missing wall between ring points " + p + " and " + q);
            }
            Assert.Equal(3, walls);
            Assert.All(wallVerts, w => Assert.True(
                ring.Exists(r => Math.Abs(r[0] - w[0]) < 1e-3f && Math.Abs(r[2] - w[2]) < 1e-3f), "wall vertex off the ring"));
        }

        [Fact]
        public void JunctionWallNormalsAreHorizontalPerpendicularToTheWallAndPointAtTheCentre()
        {
            ShellMesh m = new ShellMesh();
            TunnelShell.Junction(0, 0, TMouths(), 8f, m);
            int walls = 0;
            for (int t = 0; t < m.Indices.Count; t += 3)
            {
                int a = m.Indices[t], b = m.Indices[t + 1], c = m.Indices[t + 2];
                if (Math.Abs(m.Normals[a * 3 + 1]) > 1e-4f) continue;
                walls++;
                float nx = m.Normals[a * 3], nz = m.Normals[a * 3 + 2];
                Assert.Equal(1f, nx * nx + nz * nz, 3);
                foreach (int v in new[] { b, c })
                {
                    float ex = m.Positions[v * 3] - m.Positions[a * 3], ez = m.Positions[v * 3 + 2] - m.Positions[a * 3 + 2];
                    Assert.True(Math.Abs(ex * nx + ez * nz) < 1e-3f, "normal not perpendicular to wall");
                }
                float mx = (m.Positions[a * 3] + m.Positions[b * 3] + m.Positions[c * 3]) / 3f;
                float mz = (m.Positions[a * 3 + 2] + m.Positions[b * 3 + 2] + m.Positions[c * 3 + 2]) / 3f;
                Assert.True(-mx * nx - mz * nz > 0, "wall normal points away from the centre");
            }
            Assert.Equal(6, walls);
        }

        [Fact]
        public void JunctionFloorAndCeilingFollowTheirMouthHeights()
        {
            var mouths = TMouths();
            ShellMesh m = new ShellMesh();
            TunnelShell.Junction(0, 0, mouths, 8f, m);
            List<int> owner;
            List<float[]> ring = Ring(mouths, out owner);
            float floorMean = 0, ceilMean = 0;
            foreach (float[] r in ring) { floorMean += r[1] / 6f; ceilMean += (r[3]) / 6f; }
            int floors = 0, ceils = 0;
            for (int v = 0; v < m.VertexCount; v++)
            {
                bool up = NormalIs(m, v, 0, 1, 0), down = NormalIs(m, v, 0, -1, 0);
                if (!up && !down) continue;
                float x = m.Positions[v * 3], y = m.Positions[v * 3 + 1], z = m.Positions[v * 3 + 2];
                float[] hit = ring.Find(r => Math.Abs(r[0] - x) < 1e-3f && Math.Abs(r[2] - z) < 1e-3f);
                float expected = hit != null ? (up ? hit[1] : hit[3]) : (up ? floorMean : ceilMean);
                if (hit == null) Assert.True(Math.Abs(x) < 1e-3f && Math.Abs(z) < 1e-3f, "vertex is neither ring point nor centre");
                Assert.Equal(expected, y, 3);
                if (up) floors++; else ceils++;
            }
            Assert.True(floors > 0 && ceils > 0);
        }

        [Fact]
        public void JunctionOfFourMouthsHasFourWalls()
        {
            var mouths = new List<TunnelSection>
            {
                Arm(10, 0, 30, 0, 0f, false), Arm(0, 10, 0, 30, 0f, false), Arm(-10, 0, -30, 0, 0f, false), Arm(0, -10, 0, -30, 0f, false),
            };
            ShellMesh m = new ShellMesh();
            Assert.Equal(2 * 8 + 2 * 4, TunnelShell.Junction(0, 0, mouths, 8f, m));
            AssertWinding(m);
        }

        // Owner bug 5: the ceiling ring sits at each mouth's own Ceiling.
        [Fact]
        public void JunctionCeilingRingAndWallTopsAreAtEachMouthsCeiling()
        {
            var mouths = TMouths();
            Assert.Equal(7f, mouths[0].Ceiling, 3);
            Assert.Equal(6f, mouths[1].Ceiling, 3);
            ShellMesh m = new ShellMesh();
            TunnelShell.Junction(0, 0, mouths, 8f, m);
            foreach (TunnelSection t in mouths)
            {
                Assert.True(HasVertex(m, t.Lx, t.Ceiling, t.Lz), "no ceiling-level vertex at left mouth point");
                Assert.True(HasVertex(m, t.Rx, t.Ceiling, t.Rz), "no ceiling-level vertex at right mouth point");
            }
            for (int v = 0; v < m.VertexCount; v++)
            {
                if (!NormalIs(m, v, 0, -1, 0)) continue;
                float x = m.Positions[v * 3], z = m.Positions[v * 3 + 2];
                int hit = mouths.FindIndex(t => (Math.Abs(t.Lx - x) < 1e-3f && Math.Abs(t.Lz - z) < 1e-3f) || (Math.Abs(t.Rx - x) < 1e-3f && Math.Abs(t.Rz - z) < 1e-3f));
                if (hit >= 0) Assert.Equal(mouths[hit].Ceiling, m.Positions[v * 3 + 1], 3);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void JunctionWithFewerThanThreeMouthsGivesNothing(int count)
        {
            ShellMesh m = new ShellMesh();
            var mouths = TMouths().GetRange(0, count);
            Assert.Equal(0, TunnelShell.Junction(0, 0, mouths, 8f, m));
            Assert.Equal(0, m.VertexCount);
            Assert.Empty(m.Indices);
        }
    }
}
