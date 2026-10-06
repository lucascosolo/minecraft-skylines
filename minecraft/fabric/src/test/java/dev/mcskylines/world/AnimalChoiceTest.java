package dev.mcskylines.world;

import static dev.mcskylines.world.AnimalChoice.Context.*;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;

import org.junit.jupiter.api.Test;

class AnimalChoiceTest {
    // classify(water, grassTop, treeNear, builtSamples)

    @Test
    void waterWinsOverEverything() {
        assertEquals(WATER, AnimalChoice.classify(true, false, false, 0));
        assertEquals(WATER, AnimalChoice.classify(true, true, true, 5));
        assertEquals(WATER, AnimalChoice.classify(true, true, false, 0));
    }

    @Test
    void noGrassTopIsNone() {
        assertEquals(NONE, AnimalChoice.classify(false, false, false, 0));
        assertEquals(NONE, AnimalChoice.classify(false, false, true, 0));
        assertEquals(NONE, AnimalChoice.classify(false, false, true, 3));
    }

    @Test
    void treeNearOnGrassIsForestRegardlessOfBuildings() {
        assertEquals(FOREST, AnimalChoice.classify(false, true, true, 0));
        assertEquals(FOREST, AnimalChoice.classify(false, true, true, 7));
    }

    @Test
    void noTreesNoBuildingsIsOpenCountry() {
        assertEquals(OPEN_COUNTRY, AnimalChoice.classify(false, true, false, 0));
    }

    @Test
    void noTreesWithBuildingsIsGrass() {
        assertEquals(GRASS, AnimalChoice.classify(false, true, false, 1));
        assertEquals(GRASS, AnimalChoice.classify(false, true, false, 9));
    }

    @Test
    void noneYieldsNull() {
        assertNull(AnimalChoice.choose(NONE, 0.0));
        assertNull(AnimalChoice.choose(NONE, 0.99));
    }

    @Test
    void waterBoundaries() {
        assertEquals("minecraft:cod", AnimalChoice.choose(WATER, 0.0));
        assertEquals("minecraft:cod", AnimalChoice.choose(WATER, 0.39));
        assertEquals("minecraft:salmon", AnimalChoice.choose(WATER, 0.4));
        assertEquals("minecraft:salmon", AnimalChoice.choose(WATER, 0.69));
        assertEquals("minecraft:squid", AnimalChoice.choose(WATER, 0.7));
        assertEquals("minecraft:squid", AnimalChoice.choose(WATER, 0.99));
    }

    @Test
    void forestBoundaries() {
        assertEquals("minecraft:rabbit", AnimalChoice.choose(FOREST, 0.0));
        assertEquals("minecraft:rabbit", AnimalChoice.choose(FOREST, 0.39));
        assertEquals("minecraft:fox", AnimalChoice.choose(FOREST, 0.4));
        assertEquals("minecraft:fox", AnimalChoice.choose(FOREST, 0.74));
        assertEquals("minecraft:wolf", AnimalChoice.choose(FOREST, 0.75));
        assertEquals("minecraft:wolf", AnimalChoice.choose(FOREST, 0.99));
    }

    @Test
    void openCountryBoundaries() {
        assertEquals("minecraft:horse", AnimalChoice.choose(OPEN_COUNTRY, 0.0));
        assertEquals("minecraft:horse", AnimalChoice.choose(OPEN_COUNTRY, 0.39));
        assertEquals("minecraft:llama", AnimalChoice.choose(OPEN_COUNTRY, 0.45));
        assertEquals("minecraft:cow", AnimalChoice.choose(OPEN_COUNTRY, 0.65));
        assertEquals("minecraft:sheep", AnimalChoice.choose(OPEN_COUNTRY, 0.85));
        assertEquals("minecraft:sheep", AnimalChoice.choose(OPEN_COUNTRY, 0.99));
    }

    @Test
    void grassBoundaries() {
        assertEquals("minecraft:cow", AnimalChoice.choose(GRASS, 0.0));
        assertEquals("minecraft:cow", AnimalChoice.choose(GRASS, 0.24));
        assertEquals("minecraft:pig", AnimalChoice.choose(GRASS, 0.25));
        assertEquals("minecraft:pig", AnimalChoice.choose(GRASS, 0.49));
        assertEquals("minecraft:sheep", AnimalChoice.choose(GRASS, 0.5));
        assertEquals("minecraft:sheep", AnimalChoice.choose(GRASS, 0.74));
        assertEquals("minecraft:chicken", AnimalChoice.choose(GRASS, 0.75));
        assertEquals("minecraft:chicken", AnimalChoice.choose(GRASS, 0.99));
    }
}
