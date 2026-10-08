package dev.mcskylines.world;

import it.unimi.dsi.fastutil.ints.Int2LongOpenHashMap;

/** At most one PANIC per citizen per period, so a mob staring at a proxy does not flood the host. */
public final class PanicThrottle {
	private final long periodTicks;
	private final Int2LongOpenHashMap last = new Int2LongOpenHashMap();

	public PanicThrottle(long periodTicks) {
		this.periodTicks = periodTicks;
	}

	public boolean allow(int id, long tick) {
		if (last.containsKey(id) && tick - last.get(id) < periodTicks) return false;
		last.put(id, tick);
		return true;
	}

	public void forget(int id) {
		last.remove(id);
	}
}
