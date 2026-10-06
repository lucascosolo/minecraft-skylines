package dev.mcskylines.world;

/** Minecraft saplings to the host's TREES kind bytes (0 oak, 1 spruce, 2 birch, 3 jungle, 4 acacia, 5 dark oak, 6 bush). */
public final class TreeKinds {
	private static final float[] HEIGHT = {10, 14, 12, 12, 9, 10, 2};
	private static final float[] RADIUS = {4, 3, 3, 4, 5, 5, 1.5f};

	private TreeKinds() {
	}

	public static int ofSapling(String blockId) {
		return switch (blockId) {
			case "minecraft:oak_sapling", "minecraft:cherry_sapling" -> 0;
			case "minecraft:spruce_sapling" -> 1;
			case "minecraft:birch_sapling", "minecraft:poplar_sapling" -> 2;
			case "minecraft:jungle_sapling", "minecraft:mangrove_propagule" -> 3;
			case "minecraft:acacia_sapling" -> 4;
			case "minecraft:dark_oak_sapling", "minecraft:pale_oak_sapling" -> 5;
			default -> -1;
		};
	}

	public static float height(int kind) {
		return HEIGHT[check(kind)];
	}

	public static float radius(int kind) {
		return RADIUS[check(kind)];
	}

	private static int check(int kind) {
		if (kind < 0 || kind >= HEIGHT.length) {
			throw new IllegalArgumentException("tree kind " + kind);
		}
		return kind;
	}
}
