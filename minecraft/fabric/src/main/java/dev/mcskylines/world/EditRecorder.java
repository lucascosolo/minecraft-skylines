package dev.mcskylines.world;

import dev.mcskylines.protocol.BlockEdits;
import it.unimi.dsi.fastutil.longs.Long2ObjectLinkedOpenHashMap;
import it.unimi.dsi.fastutil.objects.Object2IntOpenHashMap;
import java.util.ArrayList;
import java.util.List;
import java.util.function.Function;

/** Block changes since the last flush, latest state per position, drained into BLOCK_EDITS batches. */
public final class EditRecorder<S> {
	private final Long2ObjectLinkedOpenHashMap<S> latest = new Long2ObjectLinkedOpenHashMap<>();

	public void record(long key, S state) {
		latest.put(key, state);
	}

	public int size() {
		return latest.size();
	}

	public boolean isEmpty() {
		return latest.isEmpty();
	}

	public void clear() {
		latest.clear();
	}

	public List<BlockEdits> drain(int openSeq, Function<S, String> serialize) {
		return drain(openSeq, serialize, BlockEdits.MAX_EDITS);
	}

	public List<BlockEdits> drain(int openSeq, Function<S, String> serialize, int maxPerBatch) {
		List<BlockEdits> batches = new ArrayList<>();
		Object2IntOpenHashMap<String> index = new Object2IntOpenHashMap<>();
		List<String> palette = new ArrayList<>();
		List<BlockEdits.Edit> edits = new ArrayList<>();
		for (var e : latest.long2ObjectEntrySet()) {
			String state = serialize.apply(e.getValue());
			int i = index.computeIfAbsent(state, s -> {
				palette.add(state);
				return palette.size() - 1;
			});
			long k = e.getLongKey();
			edits.add(new BlockEdits.Edit(BlockKey.x(k), BlockKey.y(k), BlockKey.z(k), i));
			if (edits.size() == maxPerBatch) {
				batches.add(new BlockEdits(openSeq, 0, palette, edits));
				index.clear();
				palette.clear();
				edits.clear();
			}
		}
		if (!edits.isEmpty()) {
			batches.add(new BlockEdits(openSeq, 0, palette, edits));
		}
		latest.clear();
		return batches;
	}
}
