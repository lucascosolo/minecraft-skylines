package dev.mcskylines.shadow;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.Test;

class ShadowMaterialsResourcesTest {
    private static final ShadowMaterials.Top GRASS = ShadowMaterials.Top.GRASS;
    private static final long SEED = 12345L;

    private static int count(ShadowMaterials.Resources r, String suffix) {
        int n = 0;
        for (int x = 0; x < 48; x++)
            for (int z = 0; z < 48; z++)
                for (int y = -60; y <= 60; y++) {
                    String id = ShadowMaterials.ground(SEED, x, y, z, 1000, GRASS, r);
                    if (id.endsWith(suffix)) n++;
                }
        return n;
    }

    @Test
    void noneIsZeroed() {
        assertEquals(new ShadowMaterials.Resources(0, 0, 0, false), ShadowMaterials.Resources.NONE);
    }

    @Test
    void depletionAndScales() {
        assertEquals(1.0, ShadowMaterials.depletion(ShadowMaterials.Resources.NONE), 0);
        assertEquals(0.5, ShadowMaterials.depletion(new ShadowMaterials.Resources(15, 15, 0, true)), 0);
        assertEquals(1.0, ShadowMaterials.depletion(new ShadowMaterials.Resources(16, 0, 0, true)), 0);
        assertEquals(1.0, ShadowMaterials.depletion(new ShadowMaterials.Resources(0, 16, 0, true)), 0);
        assertEquals(3.0, ShadowMaterials.oreScale(new ShadowMaterials.Resources(255, 0, 0, false)), 1e-9);
        assertEquals(4.0, ShadowMaterials.coalScale(new ShadowMaterials.Resources(0, 255, 0, false)), 1e-9);
        assertEquals(0.5, ShadowMaterials.oreScale(new ShadowMaterials.Resources(0, 0, 0, true)), 1e-9);
        assertEquals(0.5, ShadowMaterials.coalScale(new ShadowMaterials.Resources(0, 0, 0, true)), 1e-9);
    }

    @Test
    void dirtDepthFromFertility() {
        assertEquals(3, ShadowMaterials.dirtDepth(new ShadowMaterials.Resources(0, 0, 0, false)));
        assertEquals(3, ShadowMaterials.dirtDepth(new ShadowMaterials.Resources(0, 0, 84, false)));
        assertEquals(4, ShadowMaterials.dirtDepth(new ShadowMaterials.Resources(0, 0, 85, false)));
        assertEquals(6, ShadowMaterials.dirtDepth(new ShadowMaterials.Resources(0, 0, 255, false)));
    }

    @Test
    void sixArgGroundEqualsNone() {
        for (int x = 0; x < 48; x++)
            for (int z = 0; z < 48; z++)
                for (int y = -64; y <= 70; y++)
                    assertEquals(
                        ShadowMaterials.ground(SEED, x, y, z, 64, GRASS),
                        ShadowMaterials.ground(SEED, x, y, z, 64, GRASS, ShadowMaterials.Resources.NONE));
    }

    @Test
    void fertileGrassHasDeeperDirt() {
        var rich = new ShadowMaterials.Resources(0, 0, 255, false);
        assertEquals("minecraft:grass_block", ShadowMaterials.ground(SEED, 3, 60, 3, 60, GRASS, rich));
        for (int d = 1; d <= 6; d++)
            assertEquals("minecraft:dirt", ShadowMaterials.ground(SEED, 3, 60 - d, 3, 60, GRASS, rich), "depth " + d);
        var poor = new ShadowMaterials.Resources(0, 0, 0, false);
        for (int d = 1; d <= 3; d++)
            assertEquals("minecraft:dirt", ShadowMaterials.ground(SEED, 3, 60 - d, 3, 60, GRASS, poor), "depth " + d);
    }

    @Test
    void bedrockUnchanged() {
        var r = new ShadowMaterials.Resources(255, 255, 255, false);
        assertEquals("minecraft:bedrock",
            ShadowMaterials.ground(SEED, 1, ShadowMaterials.BOTTOM_Y, 1, 60, GRASS, r));
    }

    @Test
    void oreRichCellTriplesIron() {
        double ratio = (double) count(new ShadowMaterials.Resources(255, 0, 0, false), "iron_ore")
            / count(ShadowMaterials.Resources.NONE, "iron_ore");
        assertTrue(ratio >= 2.5 && ratio <= 3.5, "ratio " + ratio);
    }

    @Test
    void oilRichCellQuadruplesCoal() {
        double ratio = (double) count(new ShadowMaterials.Resources(0, 255, 0, false), "coal_ore")
            / count(ShadowMaterials.Resources.NONE, "coal_ore");
        assertTrue(ratio >= 3.3 && ratio <= 4.7, "ratio " + ratio);
    }

    @Test
    void depletedWorkedCellHalvesOre() {
        String[] ores = {"coal_ore", "copper_ore", "iron_ore", "gold_ore", "redstone_ore", "lapis_ore", "diamond_ore"};
        int none = 0, dep = 0;
        for (String o : ores) {
            none += count(ShadowMaterials.Resources.NONE, o);
            dep += count(new ShadowMaterials.Resources(0, 0, 0, true), o);
        }
        double ratio = (double) dep / none;
        assertTrue(ratio >= 0.4 && ratio <= 0.6, "ratio " + ratio);
    }
}
