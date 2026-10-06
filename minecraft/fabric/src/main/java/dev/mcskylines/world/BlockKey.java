package dev.mcskylines.world;

/** A block position packed into a long (26 bits x, 26 bits z, 12 bits y, as Minecraft's BlockPos), and its chunk. */
public final class BlockKey {
	private static final int XZ_LIMIT = 1 << 25;
	private static final int Y_LIMIT = 1 << 11;

	private BlockKey() {
	}

	public static boolean fits(int x, int y, int z) {
		return x >= -XZ_LIMIT && x < XZ_LIMIT && z >= -XZ_LIMIT && z < XZ_LIMIT && y >= -Y_LIMIT && y < Y_LIMIT;
	}

	public static long pack(int x, int y, int z) {
		if (!fits(x, y, z)) {
			throw new IllegalArgumentException("block position out of range: " + x + ", " + y + ", " + z);
		}
		return ((long) x & 0x3FFFFFFL) << 38 | ((long) z & 0x3FFFFFFL) << 12 | (y & 0xFFFL);
	}

	public static int x(long key) {
		return (int) (key >> 38);
	}

	public static int y(long key) {
		return (int) (key << 52 >> 52);
	}

	public static int z(long key) {
		return (int) (key << 26 >> 38);
	}

	public static long chunkKey(long key) {
		return (x(key) >> 4) & 0xFFFFFFFFL | (long) (z(key) >> 4) << 32;
	}

	public static int chunkX(long chunkKey) {
		return (int) chunkKey;
	}

	public static int chunkZ(long chunkKey) {
		return (int) (chunkKey >> 32);
	}
}
