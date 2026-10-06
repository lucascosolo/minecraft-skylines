using System;
using Skylines.Core.Models;
using Xunit;

namespace Skylines.Core.Tests
{
    public class BoxModelMathTests
    {
        private const float Tol = 1e-5f;
        private static readonly float Pi = (float)Math.PI;

        private static void AssertMat(float[] expected, float[] actual)
        {
            Assert.Equal(16, actual.Length);
            for (int i = 0; i < 16; i++) Assert.True(Math.Abs(expected[i] - actual[i]) < Tol, "m[" + i + "] expected " + expected[i] + " got " + actual[i]);
        }

        private static float[] Apply(float[] m, float x, float y, float z)
        {
            return new[]
            {
                m[0] * x + m[1] * y + m[2] * z + m[3],
                m[4] * x + m[5] * y + m[6] * z + m[7],
                m[8] * x + m[9] * y + m[10] * z + m[11],
            };
        }

        private static void AssertPoint(float[] m, float[] p, float ex, float ey, float ez)
        {
            float[] r = Apply(m, p[0], p[1], p[2]);
            Assert.True(Math.Abs(r[0] - ex) < Tol && Math.Abs(r[1] - ey) < Tol && Math.Abs(r[2] - ez) < Tol,
                "expected (" + ex + "," + ey + "," + ez + ") got (" + r[0] + "," + r[1] + "," + r[2] + ")");
        }

        private static float[] Local(float px, float py, float pz, float rx, float ry, float rz, float sx, float sy, float sz)
        {
            return BoxModelMath.PartLocal(px, py, pz, rx, ry, rz, sx, sy, sz);
        }

        private static float[] Flip() { return new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1 }; }

        private static float[] Sample()
        {
            return Local(8, -16, 4, 0.3f, -0.7f, 1.1f, 1f, 2f, 0.5f);
        }

        [Fact]
        public void IdentityAndTranslation()
        {
            AssertMat(new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 }, BoxModelMath.Identity());
            AssertMat(new float[] { 1, 0, 0, 1, 0, 1, 0, 2, 0, 0, 1, 3, 0, 0, 0, 1 }, BoxModelMath.Translation(1, 2, 3));
        }

        [Fact]
        public void FromAffineAddsBottomRowAndRejectsWrongLength()
        {
            float[] m = BoxModelMath.FromAffine(new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 });
            AssertMat(new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 0, 0, 0, 1 }, m);
            Assert.Throws<ArgumentException>(() => BoxModelMath.FromAffine(new float[11]));
            Assert.Throws<ArgumentException>(() => BoxModelMath.FromAffine(new float[16]));
        }

        [Fact]
        public void MultiplyIsRowMajorProductAndIdentityIsNeutral()
        {
            float[] a = Sample();
            AssertMat(a, BoxModelMath.Multiply(a, BoxModelMath.Identity()));
            AssertMat(a, BoxModelMath.Multiply(BoxModelMath.Identity(), a));
            // (T * S) p = S p + t
            float[] ts = BoxModelMath.Multiply(BoxModelMath.Translation(1, 0, 0), Local(0, 0, 0, 0, 0, 0, 2, 2, 2));
            AssertPoint(ts, new float[] { 1, 1, 1 }, 3, 2, 2);
            // (S * T) p = S (p + t)
            float[] st = BoxModelMath.Multiply(Local(0, 0, 0, 0, 0, 0, 2, 2, 2), BoxModelMath.Translation(1, 0, 0));
            AssertPoint(st, new float[] { 1, 1, 1 }, 4, 2, 2);
        }

        [Fact]
        public void PartLocalPureTranslationIsDividedBySixteen()
        {
            AssertMat(new float[] { 1, 0, 0, 1, 0, 1, 0, -2, 0, 0, 1, 0.5f, 0, 0, 0, 1 }, Local(16, -32, 8, 0, 0, 0, 1, 1, 1));
        }

        [Fact]
        public void PartLocalQuarterTurnAboutX()
        {
            AssertPoint(Local(0, 0, 0, Pi / 2, 0, 0, 1, 1, 1), new float[] { 0, 1, 0 }, 0, 0, 1);
            AssertPoint(Local(0, 0, 0, Pi / 2, 0, 0, 1, 1, 1), new float[] { 0, 0, 1 }, 0, -1, 0);
        }

        [Fact]
        public void PartLocalQuarterTurnAboutY()
        {
            AssertPoint(Local(0, 0, 0, 0, Pi / 2, 0, 1, 1, 1), new float[] { 0, 0, 1 }, 1, 0, 0);
            AssertPoint(Local(0, 0, 0, 0, Pi / 2, 0, 1, 1, 1), new float[] { 1, 0, 0 }, 0, 0, -1);
        }

        [Fact]
        public void PartLocalQuarterTurnAboutZ()
        {
            AssertPoint(Local(0, 0, 0, 0, 0, Pi / 2, 1, 1, 1), new float[] { 1, 0, 0 }, 0, 1, 0);
            AssertPoint(Local(0, 0, 0, 0, 0, Pi / 2, 1, 1, 1), new float[] { 0, 1, 0 }, -1, 0, 0);
        }

        [Fact]
        public void PartLocalAppliesXThenYThenZ()
        {
            AssertPoint(Local(0, 0, 0, Pi / 2, 0, Pi / 2, 1, 1, 1), new float[] { 0, 1, 0 }, 0, 0, 1);
            AssertPoint(Local(0, 0, 0, Pi / 2, 0, Pi / 2, 1, 1, 1), new float[] { 1, 0, 0 }, 0, 1, 0);
            AssertPoint(Local(0, 0, 0, Pi / 2, Pi / 2, 0, 1, 1, 1), new float[] { 0, 1, 0 }, 1, 0, 0);
            AssertPoint(Local(0, 0, 0, 0, Pi / 2, Pi / 2, 1, 1, 1), new float[] { 0, 0, 1 }, 0, 1, 0);
        }

        [Fact]
        public void PartLocalScalesBeforeRotationAndTranslatesLast()
        {
            // S: (1,1,1) -> (2,3,4); Rz(90): (-3,2,4); T(1,0,-1): (-2,2,3)
            AssertPoint(Local(16, 0, -16, 0, 0, Pi / 2, 2, 3, 4), new float[] { 1, 1, 1 }, -2, 2, 3);
        }

        [Fact]
        public void MirrorZEqualsFlipSandwich()
        {
            float[] m = Sample();
            AssertMat(BoxModelMath.Multiply(Flip(), BoxModelMath.Multiply(m, Flip())), BoxModelMath.MirrorZ(m));
        }

        [Fact]
        public void MirrorZTwiceIsOriginal()
        {
            float[] m = Sample();
            AssertMat(m, BoxModelMath.MirrorZ(BoxModelMath.MirrorZ(m)));
        }

        [Fact]
        public void MirrorZTransformsMirroredPointsLikeTheOriginal()
        {
            float[] m = Sample();
            float[] r = Apply(m, 1f, 2f, 3f);
            AssertPoint(BoxModelMath.MirrorZ(m), new float[] { 1f, 2f, -3f }, r[0], r[1], -r[2]);
        }

        [Fact]
        public void LerpIsElementWise()
        {
            float[] a = BoxModelMath.Identity();
            float[] b = BoxModelMath.Translation(2, 4, 6);
            AssertMat(a, BoxModelMath.Lerp(a, b, 0f));
            AssertMat(b, BoxModelMath.Lerp(a, b, 1f));
            AssertMat(BoxModelMath.Translation(1, 2, 3), BoxModelMath.Lerp(a, b, 0.5f));
        }

        [Fact]
        public void LerpAngleTakesTheShortWayAcrossThePiSeam()
        {
            float r = BoxModelMath.LerpAngle(3.0f, -3.0f, 0.5f);
            Assert.True(Math.Abs(Math.Abs(r) - Pi) < 1e-4f, "got " + r);
            Assert.Equal(1f, BoxModelMath.LerpAngle(0f, 2f, 0.5f), 5);
            Assert.Equal(0f, BoxModelMath.LerpAngle(-0.5f, 0.5f, 0.5f), 5);
            Assert.Equal(3.0f, BoxModelMath.LerpAngle(3.0f, -3.0f, 0f), 5);
        }

        [Fact]
        public void ChainMultipliesParentByLocalAndLeavesRoots()
        {
            float[] root = BoxModelMath.Translation(1, 0, 0);
            float[] child = Local(0, 16, 0, 0, 0, Pi / 2, 1, 1, 1);
            float[] grand = BoxModelMath.Translation(0, 0, 5);
            float[][] w = BoxModelMath.Chain(new[] { -1, 0, 1 }, new[] { root, child, grand });
            Assert.Equal(3, w.Length);
            AssertMat(root, w[0]);
            float[] c = BoxModelMath.Multiply(root, child);
            AssertMat(c, w[1]);
            AssertMat(BoxModelMath.Multiply(c, grand), w[2]);
        }

        [Fact]
        public void ChainTreatsEveryRootIndependently()
        {
            float[] a = BoxModelMath.Translation(1, 0, 0), b = BoxModelMath.Translation(0, 2, 0);
            float[][] w = BoxModelMath.Chain(new[] { -1, -1 }, new[] { a, b });
            AssertMat(a, w[0]);
            AssertMat(b, w[1]);
        }

        [Fact]
        public void DrawnPropagatesHiddenToDescendantsButNotSkip()
        {
            int[] parents = { -1, 0, 1, -1 };
            Assert.Equal(new[] { true, false, false, true }, BoxModelMath.Drawn(parents, new byte[] { 0, 1, 0, 0 }));
            Assert.Equal(new[] { false, true, true, true }, BoxModelMath.Drawn(parents, new byte[] { 2, 0, 0, 0 }));
            Assert.Equal(new[] { false, false, false, true }, BoxModelMath.Drawn(parents, new byte[] { 1, 0, 0, 0 }));
            Assert.Equal(new[] { true, false, false, false }, BoxModelMath.Drawn(parents, new byte[] { 0, 3, 0, 2 }));
        }

        private static float[] Quad(float[][] verts, float nx, float ny, float nz)
        {
            var q = new float[23];
            for (int i = 0; i < 4; i++) for (int k = 0; k < 5; k++) q[i * 5 + k] = verts[i][k];
            q[20] = nx; q[21] = ny; q[22] = nz;
            return q;
        }

        private static float[][] TopVerts()
        {
            return new[]
            {
                new float[] { 0, 16, 0, 0.25f, 0.25f },
                new float[] { 16, 16, 0, 0.5f, 0.25f },
                new float[] { 16, 16, 16, 0.5f, 0.75f },
                new float[] { 0, 16, 16, 0.25f, 0.75f },
            };
        }

        [Fact]
        public void BuildMeshEmptyInputGivesEmptyArrays()
        {
            BoxMesh m = BoxModelMath.BuildMesh(new float[0]);
            Assert.Empty(m.Positions);
            Assert.Empty(m.Uvs);
            Assert.Empty(m.Normals);
            Assert.Empty(m.Indices);
        }

        [Fact]
        public void BuildMeshMirrorsZAndFlipsV()
        {
            float[] q = Quad(TopVerts(), 0, 1, 0);
            q[2] = 32f;  // first vertex z
            q[22] = -1f; // nz
            BoxMesh m = BoxModelMath.BuildMesh(q);
            Assert.Equal(12, m.Positions.Length);
            Assert.Equal(8, m.Uvs.Length);
            Assert.Equal(12, m.Normals.Length);
            Assert.Equal(6, m.Indices.Length);
            Assert.Equal(new[] { 0f, 1f, -2f }, new[] { m.Positions[0], m.Positions[1], m.Positions[2] });
            Assert.Equal(new[] { 1f, 1f, 0f }, new[] { m.Positions[3], m.Positions[4], m.Positions[5] });
            Assert.Equal(new[] { 1f, 1f, -1f }, new[] { m.Positions[6], m.Positions[7], m.Positions[8] });
            Assert.Equal(0.25f, m.Uvs[0]);
            Assert.Equal(0.75f, m.Uvs[1]);
            Assert.Equal(0.5f, m.Uvs[4]);
            Assert.Equal(0.25f, m.Uvs[5]);
            for (int v = 0; v < 4; v++)
            {
                Assert.Equal(0f, m.Normals[v * 3]);
                Assert.Equal(1f, m.Normals[v * 3 + 1]);
                Assert.Equal(1f, m.Normals[v * 3 + 2]);
            }
        }

        private static float[] P(BoxMesh m, int i) { return new[] { m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2] }; }

        private static void AssertFrontFacing(BoxMesh m, int quadCount)
        {
            Assert.Equal(6 * quadCount, m.Indices.Length);
            for (int t = 0; t < m.Indices.Length / 3; t++)
            {
                int i0 = m.Indices[t * 3], i1 = m.Indices[t * 3 + 1], i2 = m.Indices[t * 3 + 2];
                float[] a = P(m, i0), b = P(m, i1), c = P(m, i2);
                float[] u = { b[0] - a[0], b[1] - a[1], b[2] - a[2] };
                float[] w = { c[0] - a[0], c[1] - a[1], c[2] - a[2] };
                float[] x = { u[1] * w[2] - u[2] * w[1], u[2] * w[0] - u[0] * w[2], u[0] * w[1] - u[1] * w[0] };
                float dot = x[0] * m.Normals[i0 * 3] + x[1] * m.Normals[i0 * 3 + 1] + x[2] * m.Normals[i0 * 3 + 2];
                Assert.True(dot > 0f, "triangle " + t + " faces away: " + dot);
            }
        }

        [Fact]
        public void BuildMeshWindingHoldsForBothVertexOrders()
        {
            float[][] v = TopVerts();
            float[][] rev = { v[3], v[2], v[1], v[0] };
            AssertFrontFacing(BoxModelMath.BuildMesh(Quad(v, 0, 1, 0)), 1);
            BoxMesh b = BoxModelMath.BuildMesh(Quad(rev, 0, 1, 0));
            AssertFrontFacing(b, 1);
            // vertices stay in the given quad order
            Assert.Equal(0f, b.Positions[0]);
            Assert.Equal(-1f, b.Positions[2]);
        }

        [Fact]
        public void BuildMeshEmitsFourVerticesAndSixIndicesPerQuadWithOffsetIndices()
        {
            float[] q1 = Quad(TopVerts(), 0, 1, 0);
            var both = new float[46];
            Array.Copy(q1, 0, both, 0, 23);
            Array.Copy(q1, 0, both, 23, 23);
            BoxMesh m = BoxModelMath.BuildMesh(both);
            Assert.Equal(24, m.Positions.Length);
            Assert.Equal(16, m.Uvs.Length);
            Assert.Equal(24, m.Normals.Length);
            Assert.Equal(12, m.Indices.Length);
            for (int i = 6; i < 12; i++) Assert.InRange(m.Indices[i], 4, 7);
            AssertFrontFacing(m, 2);
        }
    }
}
