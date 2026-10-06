package dev.mcskylines.shadow;

import java.util.UUID;

/**
 * What a shadow ground cell is made of (docs/plans/survival.md, "Material rules for mining"): fixed per city and
 * position. Minecraft-like ore bands by absolute y; stone pockets in 4-block blobs.
 */
public final class ShadowMaterials {
	public static final int BOTTOM_Y = -64;

	public enum Top { GRASS, SAND, STONE }

	// Checked in this order against one uniform draw per cell: name, lowest y, highest y, chance per cell.
	private static final String[] ORES = {"coal", "copper", "iron", "gold", "redstone", "lapis", "diamond"};
	private static final int[][] BANDS = {{0, 256}, {-16, 112}, {BOTTOM_Y, Integer.MAX_VALUE}, {BOTTOM_Y, 32}, {BOTTOM_Y, 15},
		{BOTTOM_Y, 64}, {BOTTOM_Y, -48}};
	private static final double[] CHANCE = {0.010, 0.008, 0.007, 0.0025, 0.006, 0.002, 0.0025};
	private static final String[] POCKETS = {"minecraft:gravel", "minecraft:andesite", "minecraft:diorite", "minecraft:granite"};
	private static final double POCKET_CHANCE = 0.03;

	private ShadowMaterials() {
	}

	public static long seed(UUID saveId) {
		return mix(saveId.getMostSignificantBits() ^ mix(saveId.getLeastSignificantBits()));
	}

	public static String ground(long seed, int x, int y, int z, int topY, Top top) {
		if (y <= BOTTOM_Y) {
			return "minecraft:bedrock";
		}
		int depth = topY - y;
		if (depth == 0) {
			return switch (top) {
				case GRASS -> "minecraft:grass_block";
				case SAND -> "minecraft:sand";
				case STONE -> "minecraft:stone";
			};
		}
		if (depth <= 3 && top != Top.STONE) {
			if (top == Top.GRASS) {
				return "minecraft:dirt";
			}
			return depth <= 2 && unit(seed, x, y, z, 7) < 0.15 ? "minecraft:clay" : "minecraft:sand";
		}
		double pocket = unit(seed, x >> 2, y >> 2, z >> 2, 11);
		if (pocket < POCKET_CHANCE) {
			return POCKETS[(int) (pocket / POCKET_CHANCE * POCKETS.length)];
		}
		double u = unit(seed, x, y, z, 13), acc = 0;
		for (int i = 0; i < ORES.length; i++) {
			if (y < BANDS[i][0] || y > BANDS[i][1]) {
				continue;
			}
			acc += CHANCE[i];
			if (u < acc) {
				return y < 0 ? "minecraft:deepslate_" + ORES[i] + "_ore" : "minecraft:" + ORES[i] + "_ore";
			}
		}
		return y < 0 ? "minecraft:deepslate" : "minecraft:stone";
	}

	/**
	 * The plant on a grass-topped column: grass on every one, tall on about one in fifty. CS1 draws grass on all of its
	 * grassy ground, so any spot the player punches there breaks grass (owner, 2026-10-06: "the grass above the ground
	 * in CS1 isn't showing an outline or breaking when I punch it").
	 */
	public static String plant(long seed, int x, int z) {
		double u = unit(seed, x, 0, z, 17);
		return u < 0.02 ? "minecraft:tall_grass" : "minecraft:short_grass";
	}

	static double unit(long seed, int x, int y, int z, int salt) {
		long h = mix(seed ^ mix(x * 0x9E3779B97F4A7C15L + salt) ^ mix(y * 0xC2B2AE3D27D4EB4FL + 1) ^ mix(z * 0x165667B19E3779F9L + 2));
		return (h >>> 11) * 0x1.0p-53;
	}

	private static long mix(long z) {
		z = (z ^ (z >>> 30)) * 0xBF58476D1CE4E5B9L;
		z = (z ^ (z >>> 27)) * 0x94D049BB133111EBL;
		return z ^ (z >>> 31);
	}
}
