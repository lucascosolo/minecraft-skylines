package dev.mcskylines.shadow;

import static org.junit.jupiter.api.Assertions.*;

import java.util.*;
import org.junit.jupiter.api.Test;

class ShadowPlannerObstacleTest {
    private static final double NaN = Double.NaN;

    private static ShadowColumn.Sample smp(double t, double r, double b) {
        return new ShadowColumn.Sample(t, r, b, 1.0);
    }

    private static Map<Integer, String> plan(long seed, int x, ShadowColumn.Sample s, double obstacle) {
        Map<Integer, String> m = new HashMap<>();
        ShadowPlanner.column(seed, s, x, 4, false, 40, obstacle, (cx, y, cz, b) -> assertNull(m.put(y, b), "twice y=" + y));
        return m;
    }

    private static boolean hasPlant(Map<Integer, String> m) {
        return m.values().stream().anyMatch(b -> b.contains("grass") && !b.contains("grass_block"));
    }

    /** First (seed, x) at which the 7-arg overload emits a plant on terrain 50.3. */
    private static long[] plantSpot() {
        for (long seed = 1; seed < 40; seed++) {
            for (int x = 0; x < 40; x++) {
                Map<Integer, String> m = new HashMap<>();
                ShadowPlanner.column(seed, smp(50.3, NaN, NaN), x, 4, false, 40, (cx, y, cz, b) -> m.put(y, b));
                if (hasPlant(m)) {
                    return new long[] {seed, x};
                }
            }
        }
        throw new AssertionError("no plant spot found");
    }

    @Test
    void oldOverloadEqualsNanObstacle() {
        long[] p = plantSpot();
        Map<Integer, String> old = new HashMap<>();
        ShadowPlanner.column(p[0], smp(50.3, NaN, NaN), (int) p[1], 4, false, 40, (cx, y, cz, b) -> old.put(y, b));
        assertEquals(old, plan(p[0], (int) p[1], smp(50.3, NaN, NaN), NaN));
    }

    @Test
    void tallObstacleEmitsTwoBarriersAndNoPlant() {
        long[] p = plantSpot();
        ShadowColumn.Sample s = smp(50.3, NaN, NaN);
        Map<Integer, String> base = plan(p[0], (int) p[1], s, NaN);
        Map<Integer, String> m = plan(p[0], (int) p[1], s, 53.0);
        int t = ShadowColumn.solidTop(50.3);
        assertEquals(ShadowPlanner.SOLID, m.get(t + 1));
        assertEquals(ShadowPlanner.SOLID, m.get(t + 2));
        assertFalse(m.containsKey(t + 3));
        assertFalse(hasPlant(m));
        for (int y = 40; y <= t; y++) {
            assertEquals(base.get(y), m.get(y), "ground y=" + y);
        }
        assertEquals(t - 40 + 1 + 2, m.size());
    }

    @Test
    void jumpableObstacleEmitsOneBarrier() {
        long[] p = plantSpot();
        Map<Integer, String> m = plan(p[0], (int) p[1], smp(50.3, NaN, NaN), 50.3 + 1.0);
        int t = ShadowColumn.solidTop(50.3);
        assertEquals(ShadowPlanner.SOLID, m.get(t + 1));
        assertFalse(m.containsKey(t + 2));
        assertFalse(hasPlant(m));
    }

    @Test
    void lowObstacleEmitsNoBarrier() {
        Map<Integer, String> m = plan(1L, 3, smp(50.3, NaN, NaN), 50.3 + 0.5);
        assertFalse(m.containsValue(ShadowPlanner.SOLID));
        assertEquals(plan(1L, 3, smp(50.3, NaN, NaN), NaN), m);
    }

    @Test
    void slopedGroundUsesSolidTopOfTerrain() {
        assertEquals(63, ShadowColumn.solidTop(64.4));
        Map<Integer, String> one = plan(1L, 3, smp(64.4, NaN, NaN), 65.6);
        assertEquals(ShadowPlanner.SOLID, one.get(64));
        assertFalse(one.containsKey(65));
        Map<Integer, String> two = plan(1L, 3, smp(64.4, NaN, NaN), 66.0);
        assertEquals(ShadowPlanner.SOLID, two.get(64));
        assertEquals(ShadowPlanner.SOLID, two.get(65));
        assertFalse(two.containsKey(66));
    }

    @Test
    void roadColumnGetsNoBarrier() {
        ShadowColumn.Sample s = smp(50.3, 50.4, NaN);
        Map<Integer, String> m = plan(1L, 3, s, 55.0);
        assertEquals(plan(1L, 3, s, NaN), m);
        assertFalse(m.containsValue(ShadowPlanner.SOLID));
    }

    @Test
    void noTerrainStillEmitsNothing() {
        assertTrue(plan(1L, 3, smp(NaN, NaN, NaN), 55.0).isEmpty());
    }
}
