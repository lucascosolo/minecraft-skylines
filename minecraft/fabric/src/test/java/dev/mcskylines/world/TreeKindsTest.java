package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.Test;

class TreeKindsTest {
    @Test
    void saplingKinds() {
        String[][] t = {{"oak_sapling", "0"}, {"cherry_sapling", "0"}, {"spruce_sapling", "1"}, {"birch_sapling", "2"},
            {"poplar_sapling", "2"}, {"jungle_sapling", "3"}, {"mangrove_propagule", "3"}, {"acacia_sapling", "4"},
            {"dark_oak_sapling", "5"}, {"pale_oak_sapling", "5"}};
        for (String[] e : t) assertEquals(Integer.parseInt(e[1]), TreeKinds.ofSapling("minecraft:" + e[0]), e[0]);
    }

    @Test
    void unknownIsMinusOne() {
        assertEquals(-1, TreeKinds.ofSapling("minecraft:stone"));
        assertEquals(-1, TreeKinds.ofSapling("oak_sapling"));
        assertEquals(-1, TreeKinds.ofSapling("other:oak_sapling"));
    }

    @Test
    void heightAndRadiusTable() {
        float[] h = {10, 14, 12, 12, 9, 10, 2};
        float[] r = {4, 3, 3, 4, 5, 5, 1.5f};
        for (int i = 0; i < 7; i++) {
            assertEquals(h[i], TreeKinds.height(i), 0f, "height " + i);
            assertEquals(r[i], TreeKinds.radius(i), 0f, "radius " + i);
        }
    }

    @Test
    void outOfRangeKindThrows() {
        assertThrows(IllegalArgumentException.class, () -> TreeKinds.height(-1));
        assertThrows(IllegalArgumentException.class, () -> TreeKinds.height(7));
        assertThrows(IllegalArgumentException.class, () -> TreeKinds.radius(-1));
        assertThrows(IllegalArgumentException.class, () -> TreeKinds.radius(7));
    }
}
