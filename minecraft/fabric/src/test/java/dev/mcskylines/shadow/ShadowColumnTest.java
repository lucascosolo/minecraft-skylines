package dev.mcskylines.shadow;

import static org.junit.jupiter.api.Assertions.*;

import dev.mcskylines.collision.SkyTri;
import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.Test;

class ShadowColumnTest {
    static List<SkyTri> quad(float y, int flags) {
        float[] v = {0, y, 0, 100, y, 0, 0, y, 100, 100, y, 0, 100, y, 100, 0, y, 100};
        return new ArrayList<>(List.of(new SkyTri(v, 0, flags), new SkyTri(v, 9, flags)));
    }

    @Test
    void emptyIsAllNaN() {
        ShadowColumn.Sample s = ShadowColumn.sample(List.of(), 5, 5);
        assertTrue(Double.isNaN(s.terrain()) && Double.isNaN(s.road()) && Double.isNaN(s.building()) && Double.isNaN(s.terrainNy()));
    }

    @Test
    void separatesSurfacesAndIgnoresOtherFlags() {
        List<SkyTri> t = quad(10, SkyTri.TERRAIN);
        t.addAll(quad(11, SkyTri.ROAD_SURFACE));
        t.addAll(quad(30, SkyTri.BUILDING));
        t.addAll(quad(99, 16));
        ShadowColumn.Sample s = ShadowColumn.sample(t, 20, 30);
        assertEquals(10, s.terrain(), 1e-6);
        assertEquals(11, s.road(), 1e-6);
        assertEquals(30, s.building(), 1e-6);
        assertEquals(1.0, s.terrainNy(), 1e-6);
    }

    @Test
    void takesHighestAndBridgeDeckCountsAsRoad() {
        List<SkyTri> t = quad(10, SkyTri.TERRAIN);
        t.addAll(quad(14, SkyTri.TERRAIN));
        t.addAll(quad(20, SkyTri.ROAD_SURFACE));
        t.addAll(quad(25, SkyTri.BRIDGE_DECK));
        ShadowColumn.Sample s = ShadowColumn.sample(t, 1, 1);
        assertEquals(14, s.terrain(), 1e-6);
        assertEquals(25, s.road(), 1e-6);
    }

    @Test
    void outsideFootprintIsNaN() {
        assertTrue(Double.isNaN(ShadowColumn.sample(quad(10, SkyTri.TERRAIN), 500, 500).terrain()));
    }

    @Test
    void steepTriangleHasSmallNy() {
        float[] v = {0, 0, 0, 10, 30, 0, 0, 0, 10, 10, 30, 0, 10, 30, 10, 0, 0, 10};
        List<SkyTri> t = List.of(new SkyTri(v, 0, SkyTri.TERRAIN), new SkyTri(v, 9, SkyTri.TERRAIN));
        assertEquals(1 / Math.sqrt(10), ShadowColumn.sample(t, 2, 2).terrainNy(), 1e-4);
    }

    @Test
    void solidTop() {
        assertEquals(9, ShadowColumn.solidTop(10.4));
        assertEquals(9, ShadowColumn.solidTop(10.5));
        assertEquals(10, ShadowColumn.solidTop(10.6));
        assertEquals(-1, ShadowColumn.solidTop(0.2));
    }

    @Test
    void dugSurfaceCountsAsTerrainWithoutTerrainBit() {
        ShadowColumn.Sample s = ShadowColumn.sample(quad(12, SkyTri.DUG_SURFACE), 20, 30);
        assertEquals(12, s.terrain(), 1e-6);
        assertEquals(1.0, s.terrainNy(), 1e-6);
        assertTrue(Double.isNaN(s.road()) && Double.isNaN(s.building()));
    }

    @Test
    void highestOfTerrainAndDugSurfaceWins() {
        List<SkyTri> t = quad(10, SkyTri.TERRAIN);
        t.addAll(quad(15, SkyTri.DUG_SURFACE));
        assertEquals(15, ShadowColumn.sample(t, 5, 5).terrain(), 1e-6);
        List<SkyTri> u = quad(20, SkyTri.TERRAIN);
        u.addAll(quad(15, SkyTri.DUG_SURFACE));
        assertEquals(20, ShadowColumn.sample(u, 5, 5).terrain(), 1e-6);
    }
}
