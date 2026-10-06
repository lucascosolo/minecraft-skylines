package dev.mcskylines.shadow;

import static org.junit.jupiter.api.Assertions.*;

import java.util.*;
import org.junit.jupiter.api.Test;

class TreeLayoutTest {
    private static final String LEAVES = "minecraft:oak_leaves[distance=1,persistent=false]";

    private static Map<String, String> run(int kind, double h, double r) {
        Map<String, String> m = new HashMap<>();
        TreeLayout.blocks(kind, 10.5, 20.0, -4.5, h, r,
                (x, y, z, b) -> assertNull(m.put(x + "," + y + "," + z, b), "duplicate cell"));
        return m;
    }

    private static int maxLeafReach(Map<String, String> m) {
        int reach = 0;
        for (var e : m.entrySet()) {
            if (!e.getValue().contains("leaves")) continue;
            String[] p = e.getKey().split(",");
            reach = Math.max(reach, Math.max(Math.abs(Integer.parseInt(p[0]) - 10), Math.abs(Integer.parseInt(p[2]) + 5)));
        }
        return reach;
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
            assertTrue(m.containsValue("minecraft:" + wood[k] + "_leaves[distance=1,persistent=false]"));
        }
    }

    @Test
    void unknownKindIsOak() {
        assertEquals(run(0, 10, 3), run(42, 10, 3));
    }

    @Test
    void leavesGrowWithRadiusAndStayBelowCeiling() {
        int y0 = ShadowColumn.solidTop(20.0) + 1;
        Map<String, String> wide = run(0, 10, 4);
        for (var e : wide.entrySet()) {
            int y = Integer.parseInt(e.getKey().split(",")[1]);
            assertTrue(y <= y0 + 10 + 1, e.getKey());
        }
        assertTrue(maxLeafReach(wide) >= 3);
        int narrow = maxLeafReach(run(0, 10, 1));
        assertTrue(narrow >= 1 && narrow <= 2, "narrow " + narrow);
    }

    @Test
    void bushAndShortTreesHaveNoLogs() {
        for (Map<String, String> m : List.of(run(6, 10, 3), run(0, 2.0, 1))) {
            assertFalse(m.isEmpty());
            for (String b : m.values()) assertTrue(b.startsWith("minecraft:oak_leaves"), b);
        }
    }
}
