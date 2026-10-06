package dev.mcskylines.world;

import dev.mcskylines.protocol.LightSources;
import it.unimi.dsi.fastutil.longs.Long2IntMap;
import it.unimi.dsi.fastutil.longs.Long2IntOpenHashMap;
import it.unimi.dsi.fastutil.longs.LongOpenHashSet;
import java.util.List;
import java.util.function.LongFunction;

/** What to do to the world so the light blocks we placed match the wanted set; never touches blocks that are not ours. */
public record LampPlan(Long2IntOpenHashMap place, LongOpenHashSet remove, LongOpenHashSet forget) {
	public enum Cell { AIR, OURS, OTHER, UNLOADED }

	/** Position to level; the highest level wins when several lights share a position. */
	public static Long2IntOpenHashMap wanted(List<LightSources.Light> lights) {
		Long2IntOpenHashMap w = new Long2IntOpenHashMap();
		for (LightSources.Light l : lights) {
			if (BlockKey.fits(l.x(), l.y(), l.z())) {
				w.merge(BlockKey.pack(l.x(), l.y(), l.z()), l.level(), Math::max);
			}
		}
		return w;
	}

	public static LampPlan plan(Long2IntMap placed, Long2IntMap wanted, LongFunction<Cell> cellAt) {
		Long2IntOpenHashMap place = new Long2IntOpenHashMap();
		LongOpenHashSet remove = new LongOpenHashSet();
		LongOpenHashSet forget = new LongOpenHashSet();
		for (var e : wanted.long2IntEntrySet()) {
			long k = e.getLongKey();
			int level = e.getIntValue();
			switch (cellAt.apply(k)) {
				case OURS -> {
					if (!placed.containsKey(k) || placed.get(k) != level) {
						place.put(k, level);
					}
				}
				case AIR -> place.put(k, level);
				case OTHER -> {
					if (placed.containsKey(k)) {
						forget.add(k);
					}
				}
				case UNLOADED -> {
				}
			}
		}
		for (long k : placed.keySet()) {
			if (wanted.containsKey(k)) {
				continue;
			}
			switch (cellAt.apply(k)) {
				case OURS -> remove.add(k);
				case AIR, OTHER -> forget.add(k);
				case UNLOADED -> {
				}
			}
		}
		return new LampPlan(place, remove, forget);
	}
}
