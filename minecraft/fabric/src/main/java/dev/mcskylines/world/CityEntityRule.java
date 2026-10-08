package dev.mcskylines.world;

import java.util.Set;

/**
 * Protocol 1.18: which entities belong to the open city. Each open gets a fresh generation; entities restored or
 * spawned during it carry its tag, so anything else the world loads (another city's, an earlier open's, a copy on
 * disk from before a restart) is discarded.
 */
public final class CityEntityRule {
	public static final String TAG_PREFIX = "mcskylines.gen.";

	public enum Fate { KEEP, ADOPT, DISCARD }

	private CityEntityRule() {
	}

	/** Riders are saved inside their vehicle, players with the player data. */
	public static boolean saved(boolean player, boolean passenger, boolean serializable, boolean removed) {
		return !player && !passenger && serializable && !removed;
	}

	public static String tag(long generation) {
		return TAG_PREFIX + Long.toHexString(generation);
	}

	public static boolean isGenTag(String s) {
		return s.startsWith(TAG_PREFIX);
	}

	/** What to do with an entity that just joined the world. */
	public static Fate onLoad(boolean player, Set<String> tags, long generation, boolean cityReady) {
		if (player || tags.contains(tag(generation))) {
			return Fate.KEEP;
		}
		for (String t : tags) {
			if (isGenTag(t)) {
				return Fate.DISCARD;
			}
		}
		return cityReady ? Fate.ADOPT : Fate.DISCARD;
	}
}
