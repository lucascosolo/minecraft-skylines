package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.Test;

class GrowthMathTest {
    @Test
    void skyDarkenReferenceValues() {
        long[][] ref = {{0, 0}, {6000, 0}, {12000, 0}, {12768, 5}, {13000, 6}, {13670, 11}, {18000, 11}, {23000, 6}, {42000, 11}, {-6000, 11}};
        for (long[] r : ref) assertEquals((int) r[1], GrowthMath.skyDarken(r[0]), "tick " + r[0]);
    }

    @Test
    void skyLightMultiplierKeyframes() {
        assertEquals(1f, GrowthMath.skyLightMultiplier(6000), 0f);
        assertEquals(0.26666668f, GrowthMath.skyLightMultiplier(18000), 0f);
        assertEquals(GrowthMath.skyLightMultiplier(100), GrowthMath.skyLightMultiplier(24100), 0f);
    }

    @Test
    void eventTicksEmptyCases() {
        assertEquals(0, GrowthMath.eventTicks(100, 100, 3, 1L, 10).length);
        assertEquals(0, GrowthMath.eventTicks(100, 50, 3, 1L, 10).length);
        assertEquals(0, GrowthMath.eventTicks(0, 1000, 0, 1L, 10).length);
        assertEquals(0, GrowthMath.eventTicks(0, 1000, -1, 1L, 10).length);
        assertEquals(0, GrowthMath.eventTicks(0, 1000, 3, 1L, 0).length);
    }

    @Test
    void eventTicksAscendingWithinRange() {
        long[] t = GrowthMath.eventTicks(500, 20000, 3, 42L, 256);
        assertTrue(t.length > 0);
        for (int i = 0; i < t.length; i++) {
            assertTrue(t[i] > 500 && t[i] <= 20000, "in range: " + t[i]);
            if (i > 0) assertTrue(t[i] > t[i - 1], "strictly ascending at " + i);
        }
    }

    @Test
    void eventTicksCapKeepsEarliest() {
        assertEquals(256, GrowthMath.EVENT_CAP);
        long[] all = GrowthMath.eventTicks(0, 24000, 3, 7L, 100000);
        assertTrue(all.length > 5);
        long[] capped = GrowthMath.eventTicks(0, 24000, 3, 7L, 5);
        assertEquals(5, capped.length);
        for (int i = 0; i < 5; i++) assertEquals(all[i], capped[i]);
        assertTrue(GrowthMath.eventTicks(0, 24000, 4096, 7L, GrowthMath.EVENT_CAP).length <= GrowthMath.EVENT_CAP);
    }

    @Test
    void probabilityOneFiresEveryTick() {
        assertArrayEquals(new long[] {11, 12, 13, 14, 15}, GrowthMath.eventTicks(10, 15, 4096, 9L, 100));
        assertArrayEquals(new long[] {11, 12, 13}, GrowthMath.eventTicks(10, 1000, 5000, 9L, 3));
    }

    @Test
    void eventTicksDeterministicForSeed() {
        assertArrayEquals(GrowthMath.eventTicks(0, 24000, 3, 99L, 256), GrowthMath.eventTicks(0, 24000, 3, 99L, 256));
        assertFalse(java.util.Arrays.equals(GrowthMath.eventTicks(0, 24000, 3, 99L, 256), GrowthMath.eventTicks(0, 24000, 3, 100L, 256)));
    }

    @Test
    void eventTicksMeanMatchesProbability() {
        long total = 0;
        for (long seed = 0; seed < 2000; seed++) total += GrowthMath.eventTicks(0, 24000, 3, seed, 24000).length;
        assertEquals(17.578, total / 2000.0, 0.5);
    }

    @Test
    void mixDeterministicAndSensitive() {
        long m = GrowthMath.mix(1L, 2L, 3L);
        assertEquals(m, GrowthMath.mix(1L, 2L, 3L));
        assertNotEquals(m, GrowthMath.mix(2L, 2L, 3L));
        assertNotEquals(m, GrowthMath.mix(1L, 3L, 3L));
        assertNotEquals(m, GrowthMath.mix(1L, 2L, 4L));
    }
}
