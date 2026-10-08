package dev.mcskylines.world;

/**
 * Protocol 1.21 (docs/DECISIONS.md, 2026-10-07): how the city's problems become Minecraft dangers, as numbers. Cells are
 * CS1's natural resource grid (512 x 512 cells of 33.75 m); pollution, fertility and forest are CS1's 0..255 bytes,
 * crime the district's 0..100 rate, dead the buildings in the cell waiting for a hearse.
 */
public final class CityHazards {
	public static final double CELL = 33.75;
	private static final int GRID_MAX = 511;

	private CityHazards() {
	}

	public static int cellX(double mcX) {
		return clamp((int) Math.floor(mcX / CELL + 256));
	}

	public static int cellZ(double mcZ) {
		return clamp((int) Math.floor(-mcZ / CELL + 256));
	}

	private static int clamp(int c) {
		return Math.max(0, Math.min(GRID_MAX, c));
	}

	/** Poison on a player standing in the cell: -1 none, 0 Poison I, 1 Poison II. */
	public static int poisonAmplifier(int pollution) {
		return pollution < 128 ? -1 : pollution < 224 ? 0 : 1;
	}

	/** Random ticks a crop or sapling gets per vanilla one: pollution slows and then stops it, fertility speeds it up to 2x. */
	public static double growthFactor(int pollution, int fertility) {
		if (pollution >= 192) {
			return 0.0;
		}
		double part = pollution < 64 ? 1 : 1 - 0.75 * (pollution - 64) / 128.0;
		return part * (1 + fertility / 255.0);
	}

	/** Ticks to apply for one vanilla random tick at {@code factor}, with {@code u} uniform in [0, 1). */
	public static int growthTicks(double factor, double u) {
		double whole = Math.floor(factor);
		return (int) whole + (u < factor - whole ? 1 : 0);
	}

	/** Chance that broken grass also drops a sapling. */
	public static double saplingChance(int forest) {
		return forest / 255.0 * 0.08;
	}

	/** Extra night-time hostile spawn attempts per 5 s near a cell with this crime rate. */
	public static int crimeAttempts(int crime) {
		return crime < 30 ? 0 : Math.min(4, (crime - 10) / 20);
	}

	/** Extra zombie spawn attempts per 5 s near a cell with this many buildings of uncollected dead. */
	public static int zombieAttempts(int dead) {
		return Math.min(4, Math.max(0, dead));
	}

	public static String crimeMob(double u) {
		return u < 0.6 ? "minecraft:pillager" : "minecraft:zombie";
	}

	public static String deadMob(double u) {
		return u < 0.1 ? "minecraft:zombie_villager" : "minecraft:zombie";
	}

	/** Fire blocks lit around a burning building per second. */
	public static int firesPerSecond(int intensity) {
		return intensity <= 0 ? 0 : (intensity + 63) / 64;
	}

	/** True when an entity this far (horizontally) from a burning building's centre catches fire. */
	public static boolean inFireReach(double dx, double dz, double radius) {
		double r = radius + 2;
		return dx * dx + dz * dz <= r * r;
	}
}
