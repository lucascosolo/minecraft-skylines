package dev.mcskylines.world;

/**
 * Minecraft's rule for a zombie's villager kill (Zombie.killedEntity, 26.3): Normal converts when the zombie's coin
 * comes up, Hard always, Peaceful and Easy never; plus the city's switch.
 */
public final class ZombieConversion {
	private ZombieConversion() {
	}

	/** {@code difficultyId}: Minecraft's Difficulty id (0 peaceful, 1 easy, 2 normal, 3 hard). */
	public static boolean converts(int difficultyId, boolean enabled, boolean coin) {
		return enabled && (difficultyId == 3 || difficultyId == 2 && coin);
	}
}
