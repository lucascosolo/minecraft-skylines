package dev.mcskylines.world;

import it.unimi.dsi.fastutil.longs.Long2ObjectMap;
import it.unimi.dsi.fastutil.longs.Long2ObjectOpenHashMap;
import it.unimi.dsi.fastutil.longs.LongSet;

/** What makes the world match a target edit set: every target edit, and air at every touched position outside it, by chunk. */
public final class ReconcilePlan {
	public static final String AIR = "minecraft:air";
	private final Long2ObjectMap<Long2ObjectMap<String>> byChunk = new Long2ObjectOpenHashMap<>();
	private int edits;
	private int reverts;

	private ReconcilePlan() {
	}

	public static ReconcilePlan plan(LongSet touched, Long2ObjectMap<String> target) {
		ReconcilePlan p = new ReconcilePlan();
		for (var e : target.long2ObjectEntrySet()) {
			p.put(e.getLongKey(), e.getValue());
		}
		p.edits = target.size();
		touched.forEach((long k) -> {
			if (!target.containsKey(k)) {
				p.put(k, AIR);
				p.reverts++;
			}
		});
		return p;
	}

	private void put(long key, String state) {
		byChunk.computeIfAbsent(BlockKey.chunkKey(key), c -> new Long2ObjectOpenHashMap<>()).put(key, state);
	}

	public Long2ObjectMap<Long2ObjectMap<String>> byChunk() {
		return byChunk;
	}

	public int edits() {
		return edits;
	}

	public int reverts() {
		return reverts;
	}
}
