package dev.mcskylines.player;

/** Where a respawning player should come back: decided from facts only. */
public final class RespawnChoice {
	public enum Choice { NONE, OWN_SPAWN, ASK_HOST }

	private RespawnChoice() {
	}

	public static Choice decide(boolean alive, boolean cityWorld, boolean hasOwnSpawn) {
		if (alive || !cityWorld) {
			return Choice.NONE;
		}
		return hasOwnSpawn ? Choice.OWN_SPAWN : Choice.ASK_HOST;
	}
}
