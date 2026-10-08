package dev.mcskylines.world;

import dev.mcskylines.protocol.OreMined;
import it.unimi.dsi.fastutil.longs.Long2IntAVLTreeMap;
import java.util.ArrayList;
import java.util.List;

/** Shadow ore blocks the player broke, counted per resource and CS1 resource cell until sent as ORE_MINED. */
public final class OreLedger {
	private static final int MAX_BLOCKS = 0xFFFF;
	// Key: resource << 20 | cx << 10 | cz, so the map's order is resource, then cx, then cz.
	private final Long2IntAVLTreeMap counts = new Long2IntAVLTreeMap();

	public void broke(String blockId, double x, double z) {
		if (!blockId.startsWith("minecraft:") || !blockId.endsWith("_ore")) {
			return;
		}
		int resource = blockId.contains("coal") ? OreMined.OIL : OreMined.ORE;
		long key = (long) resource << 20 | (long) CityHazards.cellX(x) << 10 | CityHazards.cellZ(z);
		counts.put(key, Math.min(MAX_BLOCKS, counts.get(key) + 1));
	}

	public boolean isEmpty() {
		return counts.isEmpty();
	}

	public void clear() {
		counts.clear();
	}

	/** The oldest-keyed entries, at most {@link OreMined#MAX_ENTRIES}, removed from the ledger; null when empty. */
	public OreMined drain(int openSeq) {
		if (counts.isEmpty()) {
			return null;
		}
		List<OreMined.Entry> entries = new ArrayList<>();
		var it = counts.long2IntEntrySet().iterator();
		while (it.hasNext() && entries.size() < OreMined.MAX_ENTRIES) {
			var e = it.next();
			long k = e.getLongKey();
			entries.add(new OreMined.Entry((int) (k >> 20), (int) (k >> 10) & 0x3FF, (int) k & 0x3FF, e.getIntValue()));
			it.remove();
		}
		return new OreMined(openSeq, List.copyOf(entries));
	}
}
