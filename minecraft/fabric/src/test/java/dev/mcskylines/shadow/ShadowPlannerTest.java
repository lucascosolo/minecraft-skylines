package dev.mcskylines.shadow;

import static org.junit.jupiter.api.Assertions.*;

import java.util.*;
import org.junit.jupiter.api.Test;

class ShadowPlannerTest {
    private static final double NaN = Double.NaN;

    private static Map<Integer, String> plan(long seed, ShadowColumn.Sample s, int x, boolean water, int floor) {
        Map<Integer, String> m = new HashMap<>();
        ShadowPlanner.column(seed, s, x, 4, water, floor, (cx, y, cz, b) -> {
            assertEquals(x, cx);
            assertEquals(4, cz);
            assertNull(m.put(y, b), "cell emitted twice at y=" + y);
        });
        return m;
    }

    private static Map<Integer, String> plan(ShadowColumn.Sample s, boolean water, int floor) {
        return plan(1L, s, 3, water, floor);
    }

    private static ShadowColumn.Sample smp(double t, double r, double b, double ny) {
        return new ShadowColumn.Sample(t, r, b, ny);
    }

    private static boolean isPlant(String b) {
        return b.startsWith("minecraft:short_grass") || b.startsWith("minecraft:tall_grass");
    }

    @Test
    void defaultFloor() {
        assertEquals(ShadowColumn.solidTop(50.3) - 6 + 1, ShadowPlanner.defaultFloor(smp(50.3, NaN, NaN, 1)));
        assertEquals(ShadowMaterials.BOTTOM_Y, ShadowPlanner.defaultFloor(smp(-62.0, NaN, NaN, 1)));
        assertEquals(Integer.MAX_VALUE, ShadowPlanner.defaultFloor(smp(NaN, NaN, NaN, NaN)));
    }

    @Test
    void noTerrainEmitsNothing() {
        assertTrue(plan(smp(NaN, 10, 20, NaN), false, 0).isEmpty());
    }

    @Test
    void groundColumnFromFloorToTop() {
        int tTop = ShadowColumn.solidTop(50.3);
        Map<Integer, String> m = plan(smp(50.3, NaN, NaN, 1.0), false, 40);
        for (int y = 40; y <= tTop; y++) {
            assertEquals(ShadowMaterials.ground(1L, 3, y, 4, tTop, ShadowMaterials.Top.GRASS), m.get(y));
        }
        assertTrue(Collections.min(m.keySet()) >= 40);
    }

    @Test
    void floorClampedToBottom() {
        Map<Integer, String> m = plan(smp(-60.0, NaN, NaN, 1.0), false, -500);
        assertEquals("minecraft:bedrock", m.get(ShadowMaterials.BOTTOM_Y));
        assertEquals(ShadowMaterials.BOTTOM_Y, Collections.min(m.keySet()));
    }

    @Test
    void topSelection() {
        int tTop = ShadowColumn.solidTop(50.3);
        assertEquals("minecraft:stone", plan(smp(50.3, NaN, NaN, 0.5), true, 40).get(tTop));
        assertEquals("minecraft:sand", plan(smp(50.3, NaN, NaN, 1.0), true, 40).get(tTop));
        assertEquals("minecraft:grass_block", plan(smp(50.3, NaN, NaN, 1.0), false, 40).get(tTop));
    }

    @Test
    void lowRoadReplacesTopGround() {
        int tTop = ShadowColumn.solidTop(50.3), rTop = ShadowColumn.solidTop(51.6);
        Map<Integer, String> m = plan(smp(50.3, 51.6, NaN, 1.0), false, 40);
        for (int y = tTop; y <= rTop; y++) assertEquals(ShadowPlanner.PAVED, m.get(y));
        assertEquals(rTop, Collections.max(m.keySet()));
    }

    @Test
    void roadBelowTerrainIsIgnored() {
        int tTop = ShadowColumn.solidTop(50.3);
        Map<Integer, String> m = plan(smp(50.3, 49.0, NaN, 1.0), false, 40);
        assertFalse(m.containsValue(ShadowPlanner.PAVED));
        assertEquals("minecraft:grass_block", m.get(tTop));
    }

    @Test
    void bridgeOnlyPavesDeckCell() {
        int tTop = ShadowColumn.solidTop(50.3), rTop = ShadowColumn.solidTop(60.0);
        Map<Integer, String> m = plan(smp(50.3, 60.0, NaN, 1.0), false, 40);
        assertEquals(ShadowPlanner.PAVED, m.get(rTop));
        assertEquals(1, m.values().stream().filter(ShadowPlanner.PAVED::equals).count());
        assertNotEquals(ShadowPlanner.PAVED, m.get(tTop));
        assertNull(m.get(tTop + 1));
    }

    @Test
    void buildingFillIsCappedByFillAndByRoof() {
        int tTop = ShadowColumn.solidTop(50.3);
        Map<Integer, String> tall = plan(smp(50.3, NaN, 90.0, 1.0), false, 40);
        for (int y = tTop + 1; y <= tTop + ShadowPlanner.BUILDING_FILL; y++) assertEquals(ShadowPlanner.SOLID, tall.get(y));
        assertNull(tall.get(tTop + ShadowPlanner.BUILDING_FILL + 1));
        Map<Integer, String> low = plan(smp(50.3, NaN, 51.6, 1.0), false, 40);
        int bTop = ShadowColumn.solidTop(51.6);
        assertEquals(bTop, Collections.max(low.keySet()));
        assertEquals(ShadowPlanner.SOLID, low.get(bTop));
    }

    @Test
    void buildingFillStartsAboveRoad() {
        int rTop = ShadowColumn.solidTop(52.0);
        Map<Integer, String> m = plan(smp(50.3, 52.0, 90.0, 1.0), false, 40);
        assertEquals(ShadowPlanner.PAVED, m.get(rTop));
        for (int y = rTop + 1; y <= rTop + ShadowPlanner.BUILDING_FILL; y++) assertEquals(ShadowPlanner.SOLID, m.get(y));
    }

    @Test
    void plantsOnlyOnBareGrass() {
        int tTop = ShadowColumn.solidTop(50.3);
        boolean sawShort = false, sawTall = false;
        for (int x = 0; x < 3000; x++) {
            String expected = ShadowMaterials.plant(1L, x, 4);
            Map<Integer, String> bare = plan(1L, smp(50.3, NaN, NaN, 1.0), x, false, 40);
            if ("minecraft:short_grass".equals(expected)) {
                assertEquals("minecraft:short_grass", bare.get(tTop + 1));
                sawShort = true;
            } else if ("minecraft:tall_grass".equals(expected)) {
                assertEquals("minecraft:tall_grass[half=lower]", bare.get(tTop + 1));
                assertEquals("minecraft:tall_grass[half=upper]", bare.get(tTop + 2));
                sawTall = true;
            } else {
                assertNull(bare.get(tTop + 1));
            }
            assertTrue(plan(1L, smp(50.3, NaN, 90.0, 1.0), x, false, 40).values().stream().noneMatch(ShadowPlannerTest::isPlant));
            assertTrue(plan(1L, smp(50.3, 51.0, NaN, 1.0), x, false, 40).values().stream().noneMatch(ShadowPlannerTest::isPlant));
            assertTrue(plan(1L, smp(50.3, NaN, NaN, 1.0), x, true, 40).values().stream().noneMatch(ShadowPlannerTest::isPlant));
            assertTrue(plan(1L, smp(50.3, NaN, NaN, 0.3), x, false, 40).values().stream().noneMatch(ShadowPlannerTest::isPlant));
        }
        assertTrue(sawShort && sawTall);
    }

    @Test
    void protectsIsFalseWithoutTerrain() {
        assertFalse(ShadowPlanner.protects(smp(NaN, 10, 20, NaN)));
    }

    @Test
    void bareGroundIsNotProtected() {
        assertFalse(ShadowPlanner.protects(smp(50.3, NaN, NaN, 1)));
    }

    @Test
    void roadAtGroundLevelProtects() {
        assertTrue(ShadowPlanner.protects(smp(50.3, 51.0, NaN, 1)));
        assertTrue(ShadowPlanner.protects(smp(50.3, 50.1, NaN, 1)), "within 0.25 below terrain still a road");
        assertTrue(ShadowPlanner.protects(smp(50.3, 53.3, NaN, 1)), "rise of exactly 3 is not a bridge");
    }

    @Test
    void roadWellBelowTerrainDoesNotProtect() {
        assertFalse(ShadowPlanner.protects(smp(50.3, 49.0, NaN, 1)));
    }

    @Test
    void bridgeAloneDoesNotProtect() {
        assertFalse(ShadowPlanner.protects(smp(50.3, 60.0, NaN, 1)));
    }

    @Test
    void buildingProtects() {
        assertTrue(ShadowPlanner.protects(smp(50.3, NaN, 60.0, 1)));
        assertFalse(ShadowPlanner.protects(smp(50.3, NaN, 50.0, 1)), "building not above ground");
    }

    @Test
    void buildingAboveABridgeCountsAboveTheDeck() {
        assertTrue(ShadowPlanner.protects(smp(50.3, 60.0, 70.0, 1)));
        assertFalse(ShadowPlanner.protects(smp(50.3, 60.0, 55.0, 1)), "ground is max(terrain, road)");
    }
}
