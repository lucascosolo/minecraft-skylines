package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import dev.mcskylines.collision.SkyTri;
import java.util.List;
import org.junit.jupiter.api.Test;

class TreeRoomTest {
    // Tree at origin: height 10, radius 4. Trunk x,z in [-0.5,0.5], y in [0.25,10]; crown x,z in [-2,2], y in [4,10].
    private static final double H = 10, R = 4;

    private static SkyTri tri(int flags, float... v) {
        return new SkyTri(v, 0, flags);
    }

    private static boolean fits(SkyTri t) {
        return TreeRoom.fits(List.of(t), 0, 0, 0, H, R);
    }

    /** Vertical triangle in the z = 0 plane around x = cx, spanning y0..y1. */
    private static float[] vertical(float cx, float y0, float y1) {
        return new float[] {cx - 0.3f, y0, 0, cx + 0.3f, y0, 0, cx, y1, 0};
    }

    @Test
    void blockingMask() {
        assertEquals(0x1FE, TreeRoom.BLOCKING);
    }

    @Test
    void emptyListFits() {
        assertTrue(TreeRoom.fits(List.of(), 0, 0, 0, H, R));
    }

    @Test
    void terrainThroughTrunkFits() {
        assertTrue(fits(tri(SkyTri.TERRAIN, vertical(0, 0, 5))));
    }

    @Test
    void buildingThroughTrunkBlocks() {
        assertFalse(fits(tri(SkyTri.BUILDING, vertical(0, 0, 5))));
    }

    @Test
    void propCrossingOnlyCrownBlocks() {
        assertFalse(fits(tri(128, vertical(1.5f, 5, 8))));
    }

    @Test
    void buildingOutsideBothBoxesFits() {
        assertTrue(fits(tri(SkyTri.BUILDING, vertical(5, 0, 5))));
    }

    @Test
    void buildingBesideTrunkBelowCrownFits() {
        // x = 1.5 is outside the trunk; y 0..3 is below the crown (y >= 4).
        assertTrue(fits(tri(SkyTri.BUILDING, vertical(1.5f, 0, 3))));
    }

    @Test
    void dugSurfaceFits() {
        assertTrue(fits(tri(SkyTri.DUG_SURFACE, vertical(0, 0, 5))));
    }

    @Test
    void queryBoxIsUnionOfTrunkAndCrown() {
        assertArrayEquals(new double[] {-2, 0.25, -2, 2, 10, 2}, TreeRoom.queryBox(0, 0, 0, H, R), 1e-9);
        assertArrayEquals(new double[] {8, 100.25, 18, 12, 110, 22}, TreeRoom.queryBox(10, 100, 20, H, R), 1e-9);
        assertArrayEquals(new double[] {-0.5, 0.25, -0.5, 0.5, 10, 0.5}, TreeRoom.queryBox(0, 0, 0, H, 1), 1e-9);
    }
}
