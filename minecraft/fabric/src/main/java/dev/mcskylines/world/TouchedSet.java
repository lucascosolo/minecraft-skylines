package dev.mcskylines.world;

import it.unimi.dsi.fastutil.longs.LongCollection;
import it.unimi.dsi.fastutil.longs.LongOpenHashSet;
import it.unimi.dsi.fastutil.longs.LongSet;

/**
 * Positions whose block in the city world may differ from void. A position reverted to air leaves the live set
 * at once but stays persisted until a save has flushed its chunk to disk, so a crash never forgets a stray block.
 */
public final class TouchedSet {
	private final LongOpenHashSet live;
	private final LongOpenHashSet removed = new LongOpenHashSet();
	private boolean dirty;

	public TouchedSet() {
		this(LongOpenHashSet.of());
	}

	public TouchedSet(LongCollection initial) {
		live = new LongOpenHashSet(initial);
	}

	public void add(long key) {
		if (live.add(key) && !removed.remove(key)) {
			dirty = true;
		}
	}

	public void remove(long key) {
		if (live.remove(key)) {
			removed.add(key);
		}
	}

	public boolean contains(long key) {
		return live.contains(key);
	}

	public LongSet live() {
		return live;
	}

	public LongSet persistent() {
		LongOpenHashSet all = new LongOpenHashSet(live);
		all.addAll(removed);
		return all;
	}

	public void savedWithFlush() {
		removed.clear();
	}

	public boolean dirty() {
		return dirty;
	}

	public void markWritten() {
		dirty = false;
	}
}
