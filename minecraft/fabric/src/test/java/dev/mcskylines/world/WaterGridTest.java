package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import dev.mcskylines.protocol.WaterSurface;
import org.junit.jupiter.api.Test;

class WaterGridTest {
    private static WaterGrid grid(int ox, int oz, int size, float[] surface, float[] bottom) {
        return new WaterGrid(new WaterSurface(ox, oz, size, surface, bottom));
    }

    /** 1x1 grid at (0,0). */
    private static WaterGrid one(float surface, float bottom) {
        return grid(0, 0, 1, new float[] {surface}, new float[] {bottom});
    }

    /** 2x2 grid at (-1,-1): index = (z+1)*2 + (x+1). Column (0,-1) has no water. */
    private static WaterGrid negative() {
        return grid(-1, -1, 2, new float[] {10.5f, 20f, 5f, 7.5f}, new float[] {8f, 20f, 4f, 7f});
    }

    @Test
    void surfaceInsideUsesRowMajorIndexAndNegativeOrigin() {
        WaterGrid g = negative();
        assertEquals(10.5, g.surfaceAt(-1, -1), 1e-6);
        assertEquals(5.0, g.surfaceAt(-1, 0), 1e-6);
        assertEquals(7.5, g.surfaceAt(0, 0), 1e-6);
    }

    @Test
    void surfaceOutsideIsNaN() {
        WaterGrid g = negative();
        assertTrue(Double.isNaN(g.surfaceAt(-2, -1)));
        assertTrue(Double.isNaN(g.surfaceAt(1, 0)));
        assertTrue(Double.isNaN(g.surfaceAt(0, -2)));
        assertTrue(Double.isNaN(g.surfaceAt(0, 1)));
    }

    @Test
    void surfaceAtEdgesIsInclusiveLowExclusiveHigh() {
        WaterGrid g = grid(10, 20, 2, new float[] {1, 2, 3, 4}, new float[] {0, 0, 0, 0});
        assertEquals(1.0, g.surfaceAt(10, 20), 1e-6);
        assertEquals(4.0, g.surfaceAt(11, 21), 1e-6);
        assertTrue(Double.isNaN(g.surfaceAt(12, 20)));
        assertTrue(Double.isNaN(g.surfaceAt(10, 22)));
        assertTrue(Double.isNaN(g.surfaceAt(9, 20)));
    }

    @Test
    void columnWithoutWaterHasNaNSurface() {
        WaterGrid g = negative();
        assertTrue(Double.isNaN(g.surfaceAt(0, -1)), "surface == bottom");
        assertTrue(Double.isNaN(one(3f, 4f).surfaceAt(0, 0)), "surface < bottom");
    }

    @Test
    void bottomInsideAndOutside() {
        WaterGrid g = negative();
        assertEquals(8.0, g.bottomAt(-1, -1), 1e-6);
        assertEquals(20.0, g.bottomAt(0, -1), 1e-6, "also where there is no water");
        assertEquals(7.0, g.bottomAt(0, 0), 1e-6);
        assertTrue(Double.isNaN(g.bottomAt(-2, 0)));
        assertTrue(Double.isNaN(g.bottomAt(0, 1)));
    }

    @Test
    void deepCellIsFull() {
        assertEquals(1f, one(10.5f, 8f).depthIn(0, 5 + 3, 0), 1e-6f);
        assertEquals(1f, one(10.5f, 8f).depthIn(0, 9, 0), 1e-6f, "surface - y = 1.5 clamps to 1");
    }

    @Test
    void partialTopCellIsTheFraction() {
        assertEquals(0.5f, one(10.5f, 8f).depthIn(0, 10, 0), 1e-6f);
    }

    @Test
    void thinFilmBelowThreshold() {
        assertEquals(0f, one(10.01f, 8f).depthIn(0, 10, 0));
        assertEquals(0f, one(10.5f, 8f).depthIn(0, 11, 0), "cell above the surface");
        assertEquals(0.03f, one(10.03f, 8f).depthIn(0, 10, 0), 1e-6f);
    }

    @Test
    void outsideOrDryColumnIsZero() {
        WaterGrid g = negative();
        assertEquals(0f, g.depthIn(5, 6, 5));
        assertEquals(0f, g.depthIn(0, 19, -1), "no-water column");
    }

    @Test
    void cellWhollyUnderBottomIsDry() {
        WaterGrid g = one(10f, 8f);
        assertEquals(0f, g.depthIn(0, 7, 0), "tunnel under the river");
        assertEquals(0f, g.depthIn(0, 0, 0));
        assertEquals(1f, g.depthIn(0, 8, 0), 1e-6f, "cell starting at the bottom holds water");
    }

    @Test
    void cellStraddlingBottomCounts() {
        assertEquals(1f, one(10f, 7.5f).depthIn(0, 7, 0), 1e-6f);
    }

    @Test
    void anyInFindsWaterAnywhereInTheBox() {
        WaterGrid g = negative();
        assertTrue(g.anyIn(-1, 9, -1, -1, 9, -1));
        assertTrue(g.anyIn(-5, 0, -5, 5, 30, 5), "box larger than the grid");
        assertTrue(g.anyIn(0, 7, 0, 3, 7, 3), "only the corner cell (0,0) is wet at y=7");
        assertTrue(g.anyIn(-1, 10, -1, -1, 10, -1), "partial top cell");
    }

    @Test
    void anyInFalseWhenDry() {
        WaterGrid g = negative();
        assertFalse(g.anyIn(-1, 11, -1, 0, 30, 0), "above every surface");
        assertFalse(g.anyIn(0, 0, -1, 0, 30, -1), "no-water column");
        assertFalse(g.anyIn(5, 0, 5, 9, 30, 9), "outside the grid");
        assertFalse(g.anyIn(-1, 0, -1, -1, 6, -1), "wholly under the bottom");
    }
}
