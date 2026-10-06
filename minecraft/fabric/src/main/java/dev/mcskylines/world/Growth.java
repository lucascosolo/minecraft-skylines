package dev.mcskylines.world;

import it.unimi.dsi.fastutil.longs.Long2LongOpenHashMap;
import it.unimi.dsi.fastutil.longs.Long2ObjectOpenHashMap;
import it.unimi.dsi.fastutil.longs.LongOpenHashSet;
import java.util.function.LongPredicate;
import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.block.BambooSaplingBlock;
import net.minecraft.world.level.block.BambooStalkBlock;
import net.minecraft.world.level.block.CactusBlock;
import net.minecraft.world.level.block.CocoaBlock;
import net.minecraft.world.level.block.CropBlock;
import net.minecraft.world.level.block.FarmlandBlock;
import net.minecraft.world.level.block.NetherWartBlock;
import net.minecraft.world.level.block.SaplingBlock;
import net.minecraft.world.level.block.StemBlock;
import net.minecraft.world.level.block.SugarCaneBlock;
import net.minecraft.world.level.block.SweetBerryBushBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.gamerules.GameRules;

/**
 * Growth catch-up: every growing block the player owns (the open city's edits) is random-ticked by vanilla's own rules,
 * but at the city clock's pace and only when its chunk is loaded, so a farm grows wherever it stands and pausing CS1
 * pauses growth. Owned by CityEdits: server thread, under its monitor. The pure maths is {@link GrowthMath}.
 */
public final class Growth {
	/** Random ticks simulated per round; a round finishes the chunk it is in, so it can pass this by one chunk. */
	static final int ROUND_BUDGET = 4096;
	private static final int NO_OVERRIDE = -1;
	private static int skyOverride = NO_OVERRIDE;

	private final Long2ObjectOpenHashMap<LongOpenHashSet> cells = new Long2ObjectOpenHashMap<>();
	private final Long2LongOpenHashMap clocks = new Long2LongOpenHashMap();
	private int cursor;

	/** Level.getSkyDarken while a catch-up tick runs, or -1. */
	public static int skyOverride() {
		return skyOverride;
	}

	static boolean isGrowing(BlockState s) {
		var b = s.getBlock();
		return b instanceof SaplingBlock || b instanceof CropBlock || b instanceof StemBlock || b instanceof SugarCaneBlock
			|| b instanceof CactusBlock || b instanceof SweetBerryBushBlock || b instanceof BambooStalkBlock
			|| b instanceof BambooSaplingBlock || b instanceof CocoaBlock || b instanceof NetherWartBlock || b instanceof FarmlandBlock;
	}

	boolean owns(long key) {
		LongOpenHashSet set = cells.get(BlockKey.chunkKey(key));
		return set != null && set.contains(key);
	}

	/** A cell's owned state changed: growing adds it, anything else (or null) removes it. */
	void update(long key, boolean growing) {
		long ck = BlockKey.chunkKey(key);
		if (growing) {
			cells.computeIfAbsent(ck, k -> new LongOpenHashSet()).add(key);
			return;
		}
		LongOpenHashSet set = cells.get(ck);
		if (set != null && set.remove(key) && set.isEmpty()) {
			cells.remove(ck);
			clocks.remove(ck);
		}
	}

	void clear() {
		cells.clear();
		clocks.clear();
		cursor = 0;
		skyOverride = NO_OVERRIDE;
	}

	/** Clocks (chunkKey, tick pairs) read from the saved player data; applied once the index is built. */
	void loadClocks(long[] pairs) {
		for (int i = 0; i + 1 < pairs.length; i += 2) {
			clocks.put(pairs[i], pairs[i + 1]);
		}
	}

	/** After the index is built: forget saved clocks of chunks that hold nothing growing. */
	void pruneClocks() {
		clocks.keySet().removeIf(ck -> !cells.containsKey(ck));
	}

	long[] clockPairs() {
		long[] out = new long[clocks.size() * 2];
		int i = 0;
		for (var e : clocks.long2LongEntrySet()) {
			out[i++] = e.getLongKey();
			out[i++] = e.getLongValue();
		}
		return out;
	}

	/** One round: catches up loaded, applied chunks to city tick {@code now}. Returns the random ticks run. */
	int step(ServerLevel level, long now, long citySeed, boolean dayNight, LongPredicate chunkPending) {
		if (cells.isEmpty()) {
			return 0;
		}
		int speed = level.getGameRules().get(GameRules.RANDOM_TICK_SPEED);
		long[] order = cells.keySet().toLongArray();
		int spent = 0;
		int start = cursor % order.length;
		int n = 0;
		for (; n < order.length && spent < ROUND_BUDGET; n++) {
			long ck = order[(start + n) % order.length];
			LongOpenHashSet set = cells.get(ck);
			if (set == null || chunkPending.test(ck)
				|| level.getChunkSource().getChunkNow(BlockKey.chunkX(ck), BlockKey.chunkZ(ck)) == null) {
				continue;
			}
			if (!clocks.containsKey(ck)) {
				clocks.put(ck, now);
				continue;
			}
			long from = clocks.get(ck);
			if (now > from) {
				spent += simulate(level, set.toLongArray(), from, now, speed, citySeed, dayNight);
			}
			if (cells.containsKey(ck)) {
				clocks.put(ck, now);
			}
		}
		cursor = (start + n) % order.length;
		return spent;
	}

	private int simulate(ServerLevel level, long[] keys, long from, long now, int speed, long citySeed, boolean dayNight) {
		it.unimi.dsi.fastutil.longs.LongArrays.stableSort(keys, (a, b) -> Integer.compare(BlockKey.y(a), BlockKey.y(b)));
		BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
		int ticks = 0;
		for (long key : keys) {
			pos.set(BlockKey.x(key), BlockKey.y(key), BlockKey.z(key));
			for (long t : GrowthMath.eventTicks(from, now, speed, GrowthMath.mix(citySeed, key, from), GrowthMath.EVENT_CAP)) {
				BlockState state = level.getBlockState(pos);
				if (!isGrowing(state)) {
					break;
				}
				skyOverride = dayNight ? GrowthMath.skyDarken(t) : 0;
				try {
					state.randomTick(level, pos.immutable(), RandomSource.create(GrowthMath.mix(citySeed, key, t)));
				} finally {
					skyOverride = NO_OVERRIDE;
				}
				ticks++;
			}
		}
		return ticks;
	}
}
