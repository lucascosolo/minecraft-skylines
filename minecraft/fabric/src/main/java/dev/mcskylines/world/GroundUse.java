package dev.mcskylines.world;

/** Which block a hoe, shovel or bone meal acts on when aimed at CS1 ground (shadow grass hides the ground block). */
public final class GroundUse {
	public static final long NONE = Long.MIN_VALUE;
	private static final int MAX_PLANTS = 2;

	public interface Cells {
		boolean shadowPlant(long key);

		boolean air(long key);

		boolean ground(long key);
	}

	private GroundUse() {
	}

	private static long below(long key) {
		int x = BlockKey.x(key), y = BlockKey.y(key) - 1, z = BlockKey.z(key);
		return BlockKey.fits(x, y, z) ? BlockKey.pack(x, y, z) : NONE;
	}

	public static long groundFor(long clicked, Cells c) {
		long k = clicked;
		if (c.shadowPlant(k)) {
			k = below(k);
			if (k != NONE && c.shadowPlant(k)) {
				k = below(k);
			}
		} else if (c.air(k)) {
			k = below(k);
			int skipped = 0;
			while (k != NONE && skipped < MAX_PLANTS && c.shadowPlant(k)) {
				skipped++;
				k = below(k);
			}
		}
		return k != NONE && c.ground(k) ? k : NONE;
	}

	/** The contiguous shadow plant cells directly above {@code ground}, bottom to top, at most two. */
	public static long[] plantsAbove(long ground, Cells c) {
		long[] out = new long[MAX_PLANTS];
		int n = 0;
		int x = BlockKey.x(ground), y = BlockKey.y(ground), z = BlockKey.z(ground);
		while (n < MAX_PLANTS && BlockKey.fits(x, y + 1 + n, z) && c.shadowPlant(BlockKey.pack(x, y + 1 + n, z))) {
			out[n] = BlockKey.pack(x, y + 1 + n, z);
			n++;
		}
		return java.util.Arrays.copyOf(out, n);
	}

	public static boolean isGroundTool(String itemId) {
		return "minecraft:bone_meal".equals(itemId)
			|| itemId.startsWith("minecraft:") && (itemId.endsWith("_hoe") || itemId.endsWith("_shovel"));
	}
}
