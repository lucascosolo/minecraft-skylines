package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import java.util.HashSet;
import java.util.Set;
import org.junit.jupiter.api.Test;

class GroundUseTest {
    /** Fake world: anything not listed is a non-ground solid. */
    private static final class World implements GroundUse.Cells {
        final Set<Long> plants = new HashSet<>(), air = new HashSet<>(), ground = new HashSet<>();

        World plant(int x, int y, int z) { plants.add(BlockKey.pack(x, y, z)); return this; }
        World air(int x, int y, int z) { air.add(BlockKey.pack(x, y, z)); return this; }
        World ground(int x, int y, int z) { ground.add(BlockKey.pack(x, y, z)); return this; }

        @Override public boolean shadowPlant(long key) { return plants.contains(key); }
        @Override public boolean air(long key) { return air.contains(key); }
        @Override public boolean ground(long key) { return ground.contains(key); }
    }

    private static long k(int x, int y, int z) { return BlockKey.pack(x, y, z); }

    @Test
    void clickedPlantOnGrass() {
        World w = new World().ground(0, 63, 0).plant(0, 64, 0);
        assertEquals(k(0, 63, 0), GroundUse.groundFor(k(0, 64, 0), w));
    }

    @Test
    void clickedUpperTallGrassHalf() {
        World w = new World().ground(0, 63, 0).plant(0, 64, 0).plant(0, 65, 0);
        assertEquals(k(0, 63, 0), GroundUse.groundFor(k(0, 65, 0), w));
    }

    @Test
    void clickedAirAbovePlantAboveGrass() {
        World w = new World().ground(0, 63, 0).plant(0, 64, 0).air(0, 65, 0);
        assertEquals(k(0, 63, 0), GroundUse.groundFor(k(0, 65, 0), w));
    }

    @Test
    void clickedAirAboveBareGround() {
        World w = new World().ground(0, 63, 0).air(0, 64, 0);
        assertEquals(k(0, 63, 0), GroundUse.groundFor(k(0, 64, 0), w));
    }

    @Test
    void clickedGroundDirectly() {
        World w = new World().ground(0, 63, 0);
        assertEquals(k(0, 63, 0), GroundUse.groundFor(k(0, 63, 0), w));
    }

    @Test
    void clickedNonGroundSolidIsNone() {
        assertEquals(Long.MIN_VALUE, GroundUse.NONE);
        assertEquals(GroundUse.NONE, GroundUse.groundFor(k(0, 63, 0), new World()));
    }

    @Test
    void airAboveAirIsNone() {
        World w = new World().ground(0, 62, 0).air(0, 63, 0).air(0, 64, 0);
        assertEquals(GroundUse.NONE, GroundUse.groundFor(k(0, 64, 0), w));
    }

    @Test
    void plantsAboveOrderedBottomToTop() {
        World w = new World().ground(0, 63, 0).plant(0, 64, 0).plant(0, 65, 0);
        assertArrayEquals(new long[] {k(0, 64, 0), k(0, 65, 0)}, GroundUse.plantsAbove(k(0, 63, 0), w));
    }

    @Test
    void plantsAboveCapsAtTwoAndHandlesNone() {
        World w = new World().ground(0, 63, 0).plant(0, 64, 0).plant(0, 65, 0).plant(0, 66, 0);
        assertArrayEquals(new long[] {k(0, 64, 0), k(0, 65, 0)}, GroundUse.plantsAbove(k(0, 63, 0), w));
        assertEquals(0, GroundUse.plantsAbove(k(0, 63, 0), new World().ground(0, 63, 0)).length);
    }

    @Test
    void isGroundTool() {
        assertTrue(GroundUse.isGroundTool("minecraft:bone_meal"));
        assertTrue(GroundUse.isGroundTool("minecraft:iron_hoe"));
        assertTrue(GroundUse.isGroundTool("minecraft:netherite_shovel"));
        assertFalse(GroundUse.isGroundTool("minecraft:wheat_seeds"));
        assertFalse(GroundUse.isGroundTool("other:thing_hoe"));
        assertFalse(GroundUse.isGroundTool("minecraft:stick"));
    }
}
