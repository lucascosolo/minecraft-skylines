package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.Test;

class CityHazardsTest {
    @Test
    void cellMappingClampsToGrid() {
        assertEquals(256, CityHazards.cellX(0));
        assertEquals(256, CityHazards.cellZ(0));
        assertEquals(257, CityHazards.cellX(33.75));
        assertEquals(255, CityHazards.cellX(-0.01));
        assertEquals(255, CityHazards.cellZ(33.75));
        assertEquals(257, CityHazards.cellZ(-33.75));
        assertEquals(0, CityHazards.cellX(-1e9));
        assertEquals(511, CityHazards.cellX(1e9));
        assertEquals(511, CityHazards.cellZ(-1e9));
        assertEquals(0, CityHazards.cellZ(1e9));
    }

    @Test
    void poisonAmplifierThresholds() {
        assertEquals(-1, CityHazards.poisonAmplifier(0));
        assertEquals(-1, CityHazards.poisonAmplifier(127));
        assertEquals(0, CityHazards.poisonAmplifier(128));
        assertEquals(0, CityHazards.poisonAmplifier(223));
        assertEquals(1, CityHazards.poisonAmplifier(224));
        assertEquals(1, CityHazards.poisonAmplifier(255));
    }

    @Test
    void growthFactorCurve() {
        assertEquals(1.0, CityHazards.growthFactor(0, 0), 1e-9);
        assertEquals(2.0, CityHazards.growthFactor(0, 255), 1e-9);
        assertEquals(1.0, CityHazards.growthFactor(63, 0), 1e-9);
        assertEquals(0.625, CityHazards.growthFactor(128, 0), 1e-9);
        assertTrue(CityHazards.growthFactor(191, 255) > 0);
        assertEquals(0.0, CityHazards.growthFactor(192, 255), 0.0);
        assertEquals(0.0, CityHazards.growthFactor(255, 0), 0.0);
    }

    @Test
    void growthTicksIsStochasticRound() {
        assertEquals(1, CityHazards.growthTicks(1.0, 0.99));
        assertEquals(2, CityHazards.growthTicks(1.5, 0.49));
        assertEquals(1, CityHazards.growthTicks(1.5, 0.5));
        assertEquals(0, CityHazards.growthTicks(0.0, 0.0));
        assertEquals(1, CityHazards.growthTicks(0.625, 0.6));
        assertEquals(0, CityHazards.growthTicks(0.625, 0.7));
    }

    @Test
    void saplingChance() {
        assertEquals(0.0, CityHazards.saplingChance(0), 1e-12);
        assertEquals(0.08, CityHazards.saplingChance(255), 1e-12);
    }

    @Test
    void crimeAttempts() {
        assertEquals(0, CityHazards.crimeAttempts(0));
        assertEquals(0, CityHazards.crimeAttempts(29));
        assertEquals(1, CityHazards.crimeAttempts(30));
        assertEquals(1, CityHazards.crimeAttempts(49));
        assertEquals(2, CityHazards.crimeAttempts(50));
        assertEquals(4, CityHazards.crimeAttempts(90));
        assertEquals(4, CityHazards.crimeAttempts(100));
        assertEquals(4, CityHazards.crimeAttempts(255));
    }

    @Test
    void zombieAttempts() {
        assertEquals(0, CityHazards.zombieAttempts(0));
        assertEquals(0, CityHazards.zombieAttempts(-3));
        assertEquals(3, CityHazards.zombieAttempts(3));
        assertEquals(4, CityHazards.zombieAttempts(255));
    }

    @Test
    void mobChoice() {
        assertEquals("minecraft:pillager", CityHazards.crimeMob(0.0));
        assertEquals("minecraft:pillager", CityHazards.crimeMob(0.59));
        assertEquals("minecraft:zombie", CityHazards.crimeMob(0.6));
        assertEquals("minecraft:zombie_villager", CityHazards.deadMob(0.0));
        assertEquals("minecraft:zombie_villager", CityHazards.deadMob(0.09));
        assertEquals("minecraft:zombie", CityHazards.deadMob(0.1));
    }

    @Test
    void firesPerSecond() {
        assertEquals(0, CityHazards.firesPerSecond(0));
        assertEquals(0, CityHazards.firesPerSecond(-5));
        assertEquals(1, CityHazards.firesPerSecond(1));
        assertEquals(1, CityHazards.firesPerSecond(64));
        assertEquals(2, CityHazards.firesPerSecond(65));
        assertEquals(4, CityHazards.firesPerSecond(255));
    }

    @Test
    void fireReach() {
        assertTrue(CityHazards.inFireReach(0, 0, 0));
        assertTrue(CityHazards.inFireReach(12, 0, 10));
        assertFalse(CityHazards.inFireReach(12.1, 0, 10));
        assertTrue(CityHazards.inFireReach(6, 8, 8));
        assertFalse(CityHazards.inFireReach(8, 8, 8));
    }
}
