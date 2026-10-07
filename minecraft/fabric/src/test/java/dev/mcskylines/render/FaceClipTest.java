package dev.mcskylines.render;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.Test;

class FaceClipTest {
    private static final float N = Float.NaN;
    private static final float E = 1e-4f;

    /** Quad along x from 0 to 1 at z=0, bottom y=0, top y=2; vertex order bottom-first. */
    private static float[][] quad(boolean vDown) {
        float[] x = {0, 1, 1, 0};
        float[] y = {0, 0, 2, 2};
        float[] z = {0, 0, 0, 0};
        float[] u = {0, 1, 1, 0};
        float[] v = vDown ? new float[] {1, 1, 0, 0} : new float[] {0, 0, 1, 1};
        return new float[][] {x, y, z, u, v};
    }

    private static boolean clip(float[][] q, float... surf) {
        return FaceClip.clipTop(q[0], q[1], q[2], q[3], q[4], surf);
    }

    @Test
    void halfHeightClipCropsTexture() {
        float[][] q = quad(true);
        assertTrue(clip(q, N, N, 1f, 1f));
        assertEquals(1f, q[1][2], E);
        assertEquals(1f, q[1][3], E);
        assertEquals(0.5f, q[4][2], E);
        assertEquals(0.5f, q[4][3], E);
        assertEquals(1f, q[3][2], E);
        assertEquals(0f, q[3][3], E);
        assertEquals(0f, q[1][0], E);
        assertEquals(0f, q[1][1], E);
        assertEquals(1f, q[4][0], E);
        assertEquals(1f, q[4][1], E);
    }

    @Test
    void vIncreasingUpwardAlsoCrops() {
        float[][] q = quad(false);
        assertTrue(clip(q, N, N, 0.5f, 0.5f));
        assertEquals(0.5f, q[1][2], E);
        assertEquals(0.25f, q[4][2], E);
        assertEquals(0.25f, q[4][3], E);
    }

    @Test
    void slopedClip() {
        float[][] q = quad(true);
        assertTrue(clip(q, N, N, 0.25f, 0.75f));
        assertEquals(0.25f, q[1][2], E);
        assertEquals(0.75f, q[1][3], E);
        assertEquals(1f - 0.125f, q[4][2], E);
        assertEquals(1f - 0.375f, q[4][3], E);
    }

    @Test
    void textureCoordinateUInterpolatesToo() {
        float[][] q = quad(true);
        q[3][2] = 3f; // top-right u differs from bottom-right u (1)
        assertTrue(clip(q, N, N, 1f, N));
        assertEquals(2f, q[3][2], E);
        assertEquals(1f, q[1][2], E);
    }

    @Test
    void oneCornerBelowBottomClampsAndKeepsFace() {
        float[][] q = quad(true);
        assertTrue(clip(q, N, N, -1f, 1f));
        assertEquals(0f, q[1][2], E);
        assertEquals(1f, q[4][2], E);
        assertEquals(1f, q[1][3], E);
        assertEquals(0.5f, q[4][3], E);
    }

    @Test
    void bothAtOrBelowBottomIsClippedAway() {
        assertFalse(clip(quad(true), N, N, -1f, -5f));
        assertFalse(clip(quad(true), N, N, 0f, 0f));
    }

    @Test
    void surfaceAboveTopLeavesFaceUnchanged() {
        float[][] q = quad(true);
        float[][] ref = quad(true);
        assertTrue(clip(q, N, N, 5f, 2f));
        for (int k = 0; k < 5; k++) {
            assertArrayEquals(ref[k], q[k], E);
        }
    }

    @Test
    void nanSurfaceLeavesVertexUnchanged() {
        float[][] q = quad(true);
        float[][] ref = quad(true);
        assertTrue(clip(q, N, N, N, N));
        for (int k = 0; k < 5; k++) {
            assertArrayEquals(ref[k], q[k], E);
        }
        q = quad(true);
        assertTrue(clip(q, N, N, 1f, N));
        assertEquals(2f, q[1][3], E);
        assertEquals(0f, q[4][3], E);
    }

    @Test
    void nanOnOneCornerAndOtherBelowBottomStaysVisible() {
        assertTrue(clip(quad(true), N, N, -1f, N));
    }

    @Test
    void interleavedVertexOrder() {
        float[] x = {0, 0, 1, 1};
        float[] y = {2, 0, 2, 0};
        float[] z = {0, 0, 0, 0};
        float[] u = {0, 0, 1, 1};
        float[] v = {0, 1, 0, 1};
        assertTrue(FaceClip.clipTop(x, y, z, u, v, new float[] {1f, N, 0.5f, N}));
        assertEquals(1f, y[0], E);
        assertEquals(0.5f, v[0], E);
        assertEquals(0.5f, y[2], E);
        assertEquals(0.75f, v[2], E);
        assertEquals(0f, y[1], E);
        assertEquals(0f, y[3], E);
        assertEquals(1f, v[1], E);
    }

    @Test
    void nonZeroBottomIsRespected() {
        float[][] q = quad(true);
        for (int i = 0; i < 4; i++) {
            q[1][i] += 10f;
        }
        assertTrue(clip(q, N, N, 11f, 11f));
        assertEquals(11f, q[1][2], E);
        assertEquals(0.5f, q[4][2], E);
    }

    @Test
    void horizontalQuadUnchanged() {
        float[] x = {0, 1, 1, 0};
        float[] y = {5, 5, 5, 5};
        float[] z = {0, 0, 1, 1};
        float[] u = {0, 1, 1, 0};
        float[] v = {0, 0, 1, 1};
        assertTrue(FaceClip.clipTop(x, y, z, u, v, new float[] {1f, 1f, 1f, 1f}));
        assertArrayEquals(new float[] {5, 5, 5, 5}, y, E);
        assertArrayEquals(new float[] {0, 0, 1, 1}, v, E);
    }
}
