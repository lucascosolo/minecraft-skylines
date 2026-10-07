package dev.mcskylines.shadow;

import static org.junit.jupiter.api.Assertions.*;

import dev.mcskylines.collision.SkyTri;
import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.Test;

class ShadowObstaclesTest {
    private static final double G = 64.0;

    private static void tri(List<SkyTri> out, int flags, double... p) {
        float[] v = new float[9];
        for (int i = 0; i < 9; i++) {
            v[i] = (float) p[i];
        }
        out.add(new SkyTri(v, 0, flags));
    }

    /** 12 triangles of a prism over the quad (px,pz) corners in order, from y0 to y1. */
    private static List<SkyTri> prism(int flags, double[] px, double[] pz, double y0, double y1) {
        List<SkyTri> t = new ArrayList<>();
        tri(t, flags, px[0], y1, pz[0], px[1], y1, pz[1], px[2], y1, pz[2]);
        tri(t, flags, px[0], y1, pz[0], px[2], y1, pz[2], px[3], y1, pz[3]);
        tri(t, flags, px[0], y0, pz[0], px[1], y0, pz[1], px[2], y0, pz[2]);
        tri(t, flags, px[0], y0, pz[0], px[2], y0, pz[2], px[3], y0, pz[3]);
        for (int i = 0; i < 4; i++) {
            int j = (i + 1) % 4;
            tri(t, flags, px[i], y0, pz[i], px[j], y0, pz[j], px[j], y1, pz[j]);
            tri(t, flags, px[i], y0, pz[i], px[j], y1, pz[j], px[i], y1, pz[i]);
        }
        return t;
    }

    private static List<SkyTri> box(int flags, double x0, double z0, double x1, double z1, double y0, double y1) {
        return prism(flags, new double[] {x0, x1, x1, x0}, new double[] {z0, z0, z1, z1}, y0, y1);
    }

    private static List<SkyTri> centred(int flags, double half, double top) {
        return box(flags, 10.5 - half, 20.5 - half, 10.5 + half, 20.5 + half, G - 0.3, top);
    }

    @Test
    void constants() {
        assertEquals(64, SkyTri.VEGETATION);
        assertEquals(128, SkyTri.PROP);
        assertEquals(0.6, ShadowObstacles.MIN_HEIGHT);
        assertEquals(1.2, ShadowObstacles.JUMP_HEIGHT);
        assertEquals(0.2, ShadowObstacles.INSET);
        assertEquals(0.5, ShadowObstacles.GROUNDED);
    }

    @Test
    void nanGroundGivesNan() {
        assertTrue(Double.isNaN(ShadowObstacles.top(centred(SkyTri.PROP, 0.3, G + 2), 10, 20, Double.NaN)));
    }

    @Test
    void emptyListGivesNan() {
        assertTrue(Double.isNaN(ShadowObstacles.top(List.of(), 10, 20, G)));
    }

    @Test
    void tallPropReturnsItsTop() {
        assertEquals(G + 2.0, ShadowObstacles.top(centred(SkyTri.PROP, 0.3, G + 2.0), 10, 20, G), 1e-4);
        assertEquals(G + 5.0, ShadowObstacles.top(centred(SkyTri.VEGETATION, 0.3, G + 5.0), 10, 20, G), 1e-4);
    }

    @Test
    void tooLowGivesNan() {
        assertTrue(Double.isNaN(ShadowObstacles.top(centred(SkyTri.PROP, 0.3, G + 0.5), 10, 20, G)));
    }

    @Test
    void exactlyMinHeightCounts() {
        assertEquals(G + 0.6, ShadowObstacles.top(centred(SkyTri.PROP, 0.3, G + 0.6), 10, 20, G), 1e-4);
    }

    @Test
    void nonObstacleFlagsIgnored() {
        for (int f : new int[] {SkyTri.TERRAIN, SkyTri.ROAD_SURFACE, SkyTri.BRIDGE_DECK, SkyTri.BUILDING}) {
            assertTrue(Double.isNaN(ShadowObstacles.top(centred(f, 0.3, G + 3), 10, 20, G)), "flag " + f);
        }
    }

    @Test
    void overheadSignIsNotGrounded() {
        List<SkyTri> sign = box(SkyTri.PROP, 10.2, 20.2, 10.8, 20.8, G + 3.0, G + 4.0);
        assertTrue(Double.isNaN(ShadowObstacles.top(sign, 10, 20, G)));
    }

    @Test
    void groundedBoxPlusOverheadUsesMaxTop() {
        List<SkyTri> all = new ArrayList<>(centred(SkyTri.PROP, 0.3, G + 1.0));
        all.addAll(box(SkyTri.PROP, 10.3, 20.3, 10.7, 20.7, G + 3.0, G + 4.0));
        assertEquals(G + 4.0, ShadowObstacles.top(all, 10, 20, G), 1e-4);
    }

    @Test
    void thinFenceCrossingColumnCounts() {
        List<SkyTri> fence = box(SkyTri.PROP, 10.0, 20.45, 11.0, 20.55, G - 0.3, G + 1.0);
        assertEquals(G + 1.0, ShadowObstacles.top(fence, 10, 20, G), 1e-4);
    }

    @Test
    void boxOnlyOnOuterEdgeDoesNotCount() {
        List<SkyTri> edge = box(SkyTri.PROP, 10.0, 20.0, 10.1, 21.0, G - 0.3, G + 2.0);
        assertTrue(Double.isNaN(ShadowObstacles.top(edge, 10, 20, G)));
    }

    @Test
    void boxInNeighbourColumnDoesNotCount() {
        assertTrue(Double.isNaN(ShadowObstacles.top(centred(SkyTri.PROP, 0.3, G + 2), 11, 20, G)));
    }

    @Test
    void rotatedBoxWorks() {
        double cx = 10.5, cz = 20.5, r = 0.3;
        double[] px = {cx - r, cx, cx + r, cx};
        double[] pz = {cz, cz - r, cz, cz + r};
        List<SkyTri> d = prism(SkyTri.VEGETATION, px, pz, G - 0.3, G + 2.5);
        assertEquals(G + 2.5, ShadowObstacles.top(d, 10, 20, G), 1e-4);
    }

    @Test
    void rotatedBoxBoundingBoxOverlapOnlyDoesNotCount() {
        // diamond at the column corner: its bounding box overlaps the inset square but its body does not
        double cx = 10.0, cz = 20.0, r = 0.35;
        double[] px = {cx - r, cx, cx + r, cx};
        double[] pz = {cz, cz - r, cz, cz + r};
        List<SkyTri> d = prism(SkyTri.PROP, px, pz, G - 0.3, G + 2.5);
        assertTrue(Double.isNaN(ShadowObstacles.top(d, 10, 20, G)));
    }

    @Test
    void cellsCounts() {
        assertEquals(0, ShadowObstacles.cells(G, Double.NaN));
        assertEquals(0, ShadowObstacles.cells(Double.NaN, G + 2));
        assertEquals(0, ShadowObstacles.cells(G, G + 0.5));
        assertEquals(1, ShadowObstacles.cells(G, G + 0.6));
        assertEquals(1, ShadowObstacles.cells(G, G + 1.2));
        assertEquals(2, ShadowObstacles.cells(G, G + 1.25));
        assertEquals(2, ShadowObstacles.cells(G, G + 8));
    }
}
