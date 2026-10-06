package dev.mcskylines.shadow;

import dev.mcskylines.world.BlockKey;
import it.unimi.dsi.fastutil.longs.LongOpenHashSet;
import it.unimi.dsi.fastutil.longs.LongSet;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Positions currently holding shadow blocks. Written on the server thread, read by collision queries on any thread:
 * a chunk's set is replaced, never mutated in place.
 */
public final class ShadowCells {
	public static final ShadowCells INSTANCE = new ShadowCells();
	private final ConcurrentHashMap<Long, LongSet> chunks = new ConcurrentHashMap<>();

	public void setChunk(long chunkKey, LongSet cells) {
		if (cells.isEmpty()) {
			chunks.remove(chunkKey);
		} else {
			chunks.put(chunkKey, new LongOpenHashSet(cells));
		}
	}

	public void remove(long key) {
		chunks.computeIfPresent(BlockKey.chunkKey(key), (k, cells) -> {
			if (!cells.contains(key)) {
				return cells;
			}
			LongOpenHashSet copy = new LongOpenHashSet(cells);
			copy.remove(key);
			return copy.isEmpty() ? null : copy;
		});
	}

	public void clear() {
		chunks.clear();
	}

	public boolean contains(int x, int y, int z) {
		if (chunks.isEmpty() || !BlockKey.fits(x, y, z)) {
			return false;
		}
		long key = BlockKey.pack(x, y, z);
		LongSet cells = chunks.get(BlockKey.chunkKey(key));
		return cells != null && cells.contains(key);
	}

	public int size() {
		int n = 0;
		for (LongSet s : chunks.values()) {
			n += s.size();
		}
		return n;
	}

	/** Shadow blocks never take part in entity movement collision where the city's triangles are loaded. */
	public boolean ignoresCollision(boolean entityContext, int x, int y, int z, boolean regionLoaded) {
		return entityContext && regionLoaded && contains(x, y, z);
	}
}
