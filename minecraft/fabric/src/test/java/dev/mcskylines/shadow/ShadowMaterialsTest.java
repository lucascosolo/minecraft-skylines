package dev.mcskylines.shadow;

import static org.junit.jupiter.api.Assertions.*;

import java.util.*;
import org.junit.jupiter.api.Test;

class ShadowMaterialsTest {
    private static final List<String> ORES = List.of("coal", "copper", "iron", "gold", "redstone", "lapis", "diamond");
    private static final ShadowMaterials.Top GRASS = ShadowMaterials.Top.GRASS;

    private static String oreOf(String id) {
        for (String o : ORES) {
            if (id.equals("minecraft:" + o + "_ore") || id.equals("minecraft:deepslate_" + o + "_ore")) return o;
        }
        return null;
    }

    private static boolean inBand(String ore, int y) {
        return switch (ore) {
            case "coal" -> y >= 0 && y <= 256;
            case "copper" -> y >= -16 && y <= 112;
            case "gold" -> y <= 32;
            case "redstone" -> y <= 15;
            case "lapis" -> y <= 64;
            case "diamond" -> y <= -48;
            default -> true;
        };
    }

    @Test
    void seedIsDeterministicAndDiffers() {
        assertEquals(ShadowMaterials.seed(new UUID(1, 2)), ShadowMaterials.seed(new UUID(1, 2)));
        assertNotEquals(ShadowMaterials.seed(new UUID(1, 2)), ShadowMaterials.seed(new UUID(3, 4)));
    }

    @Test
    void bedrockAndSurfaceRules() {
        assertEquals("minecraft:bedrock", ShadowMaterials.ground(7, 1, ShadowMaterials.BOTTOM_Y, 1, 60, GRASS));
        assertEquals("minecraft:grass_block", ShadowMaterials.ground(7, 1, 60, 1, 60, GRASS));
        assertEquals("minecraft:sand", ShadowMaterials.ground(7, 1, 60, 1, 60, ShadowMaterials.Top.SAND));
        assertEquals("minecraft:stone", ShadowMaterials.ground(7, 1, 60, 1, 60, ShadowMaterials.Top.STONE));
    }

    @Test
    void subsurfaceLayers() {
        for (int x = 0; x < 200; x++) {
            for (int d = 1; d <= 3; d++) {
                assertEquals("minecraft:dirt", ShadowMaterials.ground(5, x, 60 - d, x * 3, 60, GRASS));
                String sand = ShadowMaterials.ground(5, x, 60 - d, x * 3, 60, ShadowMaterials.Top.SAND);
                assertTrue(sand.equals("minecraft:sand") || sand.equals("minecraft:clay"), sand);
            }
        }
    }

    @Test
    void deepBaseIsStoneAboveZeroAndDeepslateBelow() {
        Set<String> stoneFamily = Set.of("minecraft:stone", "minecraft:gravel", "minecraft:andesite", "minecraft:diorite", "minecraft:granite");
        Set<String> seenDeep = new HashSet<>();
        for (int x = 0; x < 100; x++) {
            for (int y = 5; y < 40; y++) {
                String id = ShadowMaterials.ground(9, x, y, x + y, 100, GRASS);
                assertTrue(stoneFamily.contains(id) || oreOf(id) != null, id);
            }
            seenDeep.add(ShadowMaterials.ground(9, x, -30, x, 100, GRASS));
        }
        assertTrue(seenDeep.contains("minecraft:deepslate"));
        assertFalse(seenDeep.contains("minecraft:stone"));
    }

    @Test
    void oresRespectBandsAppearAndStayRare() {
        Map<String, Integer> counts = new HashMap<>();
        int total = 0, ores = 0;
        for (int i = 0; i < 200_000; i++) {
            int x = i % 100, z = (i / 100) % 100, y = -60 + (i % 337) % 330;
            String id = ShadowMaterials.ground(12345, x, y, z, 400, ShadowMaterials.Top.STONE);
            total++;
            String ore = oreOf(id);
            if (ore == null) continue;
            ores++;
            counts.merge(ore, 1, Integer::sum);
            assertTrue(inBand(ore, y), ore + " at y=" + y);
            assertEquals(y < 0, id.startsWith("minecraft:deepslate_"), id + " at y=" + y);
        }
        for (String o : ORES) assertTrue(counts.getOrDefault(o, 0) > 0, "missing " + o);
        double share = (double) ores / total;
        assertTrue(share >= 0.005 && share <= 0.08, "ore share " + share);
    }

    @Test
    void groundIsDeterministicAndSeedDependent() {
        boolean differs = false;
        for (int i = 0; i < 5000; i++) {
            String a = ShadowMaterials.ground(1, i, -20, i * 7, 50, GRASS);
            assertEquals(a, ShadowMaterials.ground(1, i, -20, i * 7, 50, GRASS));
            if (!a.equals(ShadowMaterials.ground(2, i, -20, i * 7, 50, GRASS))) differs = true;
        }
        assertTrue(differs);
    }

    @Test
    void plantDistribution() {
        int n = 0, shortG = 0, tall = 0;
        for (int x = 0; x < 100; x++) {
            for (int z = 0; z < 100; z++) {
                String p = ShadowMaterials.plant(3, x, z);
                assertEquals(p, ShadowMaterials.plant(3, x, z));
                n++;
                if ("minecraft:short_grass".equals(p)) shortG++;
                else if ("minecraft:tall_grass".equals(p)) tall++;
                else assertNull(p);
            }
        }
        assertTrue(shortG >= n * 0.10 && shortG <= n * 0.35, "short " + shortG);
        assertTrue(tall > 0 && tall < n * 0.10 && tall < shortG, "tall " + tall);
    }
}
