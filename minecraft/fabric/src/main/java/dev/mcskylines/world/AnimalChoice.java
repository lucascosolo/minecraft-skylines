package dev.mcskylines.world;

/** Which passive animal the city's spawner tries where (owner, 2026-10-06: farming and animals readily available). */
public final class AnimalChoice {
	public enum Context { NONE, WATER, FOREST, OPEN_COUNTRY, GRASS }

	private static final String[] WATER = {"minecraft:cod", "minecraft:salmon", "minecraft:squid"};
	private static final double[] WATER_W = {0.4, 0.3, 0.3};
	private static final String[] FOREST = {"minecraft:rabbit", "minecraft:fox", "minecraft:wolf"};
	private static final double[] FOREST_W = {0.4, 0.35, 0.25};
	private static final String[] OPEN = {"minecraft:horse", "minecraft:llama", "minecraft:cow", "minecraft:sheep"};
	private static final double[] OPEN_W = {0.4, 0.2, 0.2, 0.2};
	private static final String[] GRASS = {"minecraft:cow", "minecraft:pig", "minecraft:sheep", "minecraft:chicken"};
	private static final double[] GRASS_W = {0.25, 0.25, 0.25, 0.25};

	private AnimalChoice() {
	}

	/** builtSamples: how many sampled columns around are paved or built on. */
	public static Context classify(boolean water, boolean grassTop, boolean treeNear, int builtSamples) {
		if (water) {
			return Context.WATER;
		}
		if (!grassTop) {
			return Context.NONE;
		}
		if (treeNear) {
			return Context.FOREST;
		}
		return builtSamples == 0 ? Context.OPEN_COUNTRY : Context.GRASS;
	}

	/** roll in [0, 1); null for NONE. */
	public static String choose(Context c, double roll) {
		return switch (c) {
			case WATER -> pick(WATER, WATER_W, roll);
			case FOREST -> pick(FOREST, FOREST_W, roll);
			case OPEN_COUNTRY -> pick(OPEN, OPEN_W, roll);
			case GRASS -> pick(GRASS, GRASS_W, roll);
			case NONE -> null;
		};
	}

	private static String pick(String[] ids, double[] weights, double roll) {
		double sum = 0;
		for (int i = 0; i < ids.length; i++) {
			sum += weights[i];
			if (roll < sum - 1e-9) {
				return ids[i];
			}
		}
		return ids[ids.length - 1];
	}
}
