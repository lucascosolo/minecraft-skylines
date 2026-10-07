package dev.mcskylines.shadow;

import static org.junit.jupiter.api.Assertions.*;

import java.util.*;
import org.junit.jupiter.api.Test;

class TreeLayoutTest {
    private static Map<String, String> run(int kind, double h, double r) {
        Map<String, String> m = new HashMap<>();
        TreeLayout.blocks(kind, 10.5, 20.0, -4.5, h, r,
                (x, y, z, b) -> assertNull(m.put(x + "," + y + "," + z, b), "duplicate cell"));
        return m;
    }

    @Test
    void trunkHeightClamps() {
        assertEquals(2, TreeLayout.trunkHeight(1));
        assertEquals(6, TreeLayout.trunkHeight(10));
        assertEquals(24, TreeLayout.trunkHeight(500));
    }

    @Test
    void oakHasLogsFromBaseCell() {
        Map<String, String> m = run(0, 10, 3);
        int y0 = ShadowColumn.solidTop(20.0) + 1;
        for (int i = 0; i < 6; i++) assertEquals("minecraft:oak_log[axis=y]", m.get("10," + (y0 + i) + ",-5"));
        assertFalse(m.getOrDefault("10," + (y0 + 6) + ",-5", "").contains("_log"));
    }

    @Test
    void woodPerKind() {
        String[] wood = {"oak", "spruce", "birch", "jungle", "acacia", "dark_oak"};
        int y0 = ShadowColumn.solidTop(20.0) + 1;
        for (int k = 0; k < 6; k++) {
            Map<String, String> m = run(k, 10, 3);
            assertEquals("minecraft:" + wood[k] + "_log[axis=y]", m.get("10," + y0 + ",-5"));
            assertEquals("minecraft:" + wood[k] + "_leaves[distance=1,persistent=false]", TreeLayout.leaves(k, 10));
        }
    }

    @Test
    void unknownKindIsOak() {
        assertEquals(run(0, 10, 3), run(42, 10, 3));
    }

    @Test
    void noLeafBlocksOnlyATrunkColumn() {
        for (Map<String, String> m : List.of(run(0, 10, 4), run(3, 20, 6))) {
            for (var e : m.entrySet()) {
                assertTrue(e.getValue().endsWith("_log[axis=y]"), e.getValue());
                String[] p = e.getKey().split(",");
                assertEquals("10", p[0]);
                assertEquals("-5", p[2]);
            }
        }
    }

    @Test
    void leafLootGrowsWithCanopy() {
        int narrow = TreeLayout.leafCount(0, 10, 1), wide = TreeLayout.leafCount(0, 10, 4);
        assertTrue(narrow > 0 && wide > narrow, narrow + " " + wide);
        assertTrue(TreeLayout.leafCount(6, 2, 1) > 0);
    }

    @Test
    void bushAndShortTreesAreOneOakLog() {
        int y0 = ShadowColumn.solidTop(20.0) + 1;
        for (Map<String, String> m : List.of(run(6, 10, 3), run(0, 2.0, 1))) {
            assertEquals(Map.of("10," + y0 + ",-5", "minecraft:oak_log[axis=y]"), m);
        }
    }
}
