package dev.mcskylines.world;

import dev.mcskylines.protocol.TimeSet;

/** Minecraft clock changes as the city's TIME_SET (protocol minor 16). Pure. */
public final class CityTime {
	private static final long DAY = 24000L;

	private CityTime() {
	}

	/** The TIME_SET for a change of Minecraft's clock from before to after (total ticks); null when it did not move. */
	public static TimeSet toCity(long before, long after) {
		if (after == before) return null;
		float hour = (float) ((Math.floorMod(after, DAY) / 1000.0 + 6.0) % 24.0);
		int days = after > before ? (int) Math.min(65535L, (after - before) / DAY) : 0;
		return new TimeSet(hour, days);
	}

	/** {@code /time set <ticks>} counts from the start of the Minecraft day shown at {@code current}. */
	public static long rebase(long current, long value) {
		return Math.floorDiv(current, DAY) * DAY + value;
	}
}
