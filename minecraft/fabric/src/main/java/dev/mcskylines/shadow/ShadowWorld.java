package dev.mcskylines.shadow;

import dev.mcskylines.collision.CollisionStore;
import dev.mcskylines.collision.SkyTri;
import dev.mcskylines.protocol.Trees;
import dev.mcskylines.world.BlockKey;
import dev.mcskylines.world.HostWater;
import it.unimi.dsi.fastutil.ints.Int2IntOpenHashMap;
import it.unimi.dsi.fastutil.longs.Long2IntOpenHashMap;
import it.unimi.dsi.fastutil.longs.Long2ObjectOpenHashMap;
import it.unimi.dsi.fastutil.longs.LongLinkedOpenHashSet;
import it.unimi.dsi.fastutil.longs.LongOpenHashSet;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ConcurrentLinkedQueue;

/**
 * The shadow world (docs/plans/survival.md): real blocks under and inside the city, rebuilt per chunk from the streamed
 * collision triangles and trees. Never saved as edits: every placed cell is touched (the touched-set rules revert it on
 * a restart or another city), the city edit set wins over it, and a player change to a shadow cell becomes an edit.
 * Server thread only, except the static inbox methods, which the client thread calls.
 */
public final class ShadowWorld {
	/** The world side: CityEdits, which owns the level, the touched set and the edit set. */
	public interface Host {
		/** True when the player's edit set (or a lamp) owns the position, so no shadow block goes there. */
		boolean playerOwns(long key);

		/** True while the chunk is loaded and nothing else (a reconcile) is still queued for it. */
		boolean chunkReady(long chunkKey);

		/** True when the world already holds that block at the position. */
		boolean holds(long key, String block);

		/** Places (or with null, clears to air) a shadow cell; not recorded as an edit; keeps the touched set. */
		void place(long key, String block);
	}

	public static final String CAVE_AIR = "minecraft:cave_air";
	private static final int CELLS_PER_TICK = 4000;
	private static final ConcurrentLinkedQueue<Long> CHANGED = new ConcurrentLinkedQueue<>();
	private static final ConcurrentHashMap<Long, List<Trees.Tree>> TREES = new ConcurrentHashMap<>();

	private final Long2ObjectOpenHashMap<Chunk> chunks = new Long2ObjectOpenHashMap<>();
	private final LongLinkedOpenHashSet dirty = new LongLinkedOpenHashSet();
	private final Int2IntOpenHashMap logsLeft = new Int2IntOpenHashMap();
	private long seed;
	private boolean active;

	private static final class Chunk {
		final int[] floor = new int[256];
		final boolean[] protectedCols = new boolean[256]; // columns under a road or building when last built
		LongOpenHashSet cells = new LongOpenHashSet(); // shadow blocks now in the world
		LongOpenHashSet planned = new LongOpenHashSet(); // every cell the plan fills, owned by the player or not
		final Long2IntOpenHashMap logs = new Long2IntOpenHashMap(); // standing generated logs: position to tree id

		Chunk() {
			Arrays.fill(floor, Integer.MAX_VALUE);
		}
	}

	/** Client thread: a collision region (= chunk) arrived; it and its neighbours (overhanging trees) need a rebuild. */
	public static void regionChanged(int rx, int rz) {
		for (int dx = -1; dx <= 1; dx++) {
			for (int dz = -1; dz <= 1; dz++) {
				CHANGED.add(chunkKey(rx + dx, rz + dz));
			}
		}
	}

	/** Client thread: TREES (minor 12) for one region. */
	public static void acceptTrees(Trees t) {
		if (Integer.compareUnsigned(t.epoch(), CollisionStore.INSTANCE.epoch()) < 0) {
			return;
		}
		TREES.put(chunkKey(t.regionX(), t.regionZ()), List.copyOf(t.trees()));
		regionChanged(t.regionX(), t.regionZ());
	}

	/** Client thread: COLLISION_RESET or link loss. */
	public static void clearTrees() {
		TREES.clear();
	}

	/** Any thread: a host tree (TREES) stands within {@code radius} m of x, z (horizontally). */
	public static boolean treeNear(double x, double z, double radius) {
		int c0x = (int) Math.floor((x - radius) / 16), c1x = (int) Math.floor((x + radius) / 16);
		int c0z = (int) Math.floor((z - radius) / 16), c1z = (int) Math.floor((z + radius) / 16);
		for (int cx = c0x; cx <= c1x; cx++) {
			for (int cz = c0z; cz <= c1z; cz++) {
				for (Trees.Tree t : TREES.getOrDefault(chunkKey(cx, cz), List.of())) {
					double dx = t.x() - x, dz = t.z() - z;
					if (dx * dx + dz * dz <= radius * radius) {
						return true;
					}
				}
			}
		}
		return false;
	}

	static long chunkKey(int cx, int cz) {
		return cx & 0xFFFFFFFFL | (long) cz << 32;
	}

	/** A city became ready: build everything the host has streamed, seeded by the city's save id. */
	public void start(long citySeed) {
		reset();
		seed = citySeed;
		active = true;
		for (long rk : CollisionStore.INSTANCE.regionKeys()) {
			dirty.add(chunkKey((int) (rk >> 32), (int) rk));
		}
	}

	/** The city closed, the world detached or the link dropped: forget everything (the touched set reverts the blocks). */
	public void reset() {
		active = false;
		chunks.clear();
		dirty.clear();
		logsLeft.clear();
		CHANGED.clear();
		ShadowCells.INSTANCE.clear();
	}

	/** Whether the shadow plan fills the cell: a player emptying it must be saved as an explicit (cave air) edit. */
	public boolean wouldFill(long key) {
		Chunk c = chunks.get(BlockKey.chunkKey(key));
		return c != null && c.planned.contains(key);
	}

	/** The server refuses breaking planned cells in columns under a road or building. */
	public boolean refusesBreak(long key) {
		Chunk c = chunks.get(BlockKey.chunkKey(key));
		return c != null && c.planned.contains(key) && c.protectedCols[(BlockKey.x(key) & 15) | (BlockKey.z(key) & 15) << 4];
	}

	/**
	 * The player changed a cell (recorded as an edit). Returns the CS1 tree id whose last generated log this was, or -1.
	 * Digging at the bottom of the filled crust deepens the surrounding columns.
	 */
	/** The generated tree a standing log belongs to, or -1. Server thread. */
	public int treeOfLog(long key) {
		Chunk c = chunks.get(BlockKey.chunkKey(key));
		return c == null ? -1 : c.logs.getOrDefault(key, -1);
	}

	/** Every cell of generated tree {@code treeId} (logs and leaves) that is still a shadow block. Server thread. */
	public it.unimi.dsi.fastutil.longs.LongArrayList treeCells(int treeId) {
		it.unimi.dsi.fastutil.longs.LongArrayList out = new it.unimi.dsi.fastutil.longs.LongArrayList();
		for (List<Trees.Tree> list : TREES.values()) {
			for (Trees.Tree t : list) {
				if (t.id() != treeId) {
					continue;
				}
				TreeLayout.blocks(t.kind(), t.x(), t.y(), t.z(), t.height(), t.radius(), (x, y, z, block) -> {
					if (BlockKey.fits(x, y, z) && ShadowCells.INSTANCE.contains(x, y, z)) {
						out.add(BlockKey.pack(x, y, z));
					}
				});
				return out;
			}
		}
		return out;
	}

	public int playerChanged(long key, boolean nowAir) {
		Chunk c = chunks.get(BlockKey.chunkKey(key));
		if (c == null || !c.planned.contains(key)) {
			return -1;
		}
		if (c.cells.contains(key)) {
			LongOpenHashSet copy = new LongOpenHashSet(c.cells);
			copy.remove(key);
			c.cells = copy;
			ShadowCells.INSTANCE.remove(key);
		}
		int x = BlockKey.x(key), y = BlockKey.y(key), z = BlockKey.z(key);
		if (nowAir && y <= c.floor[(x & 15) | (z & 15) << 4] + 1) {
			deepen(x, y - ShadowPlanner.CRUST, z);
		}
		if (!c.logs.containsKey(key)) {
			return -1;
		}
		int tree = c.logs.remove(key);
		int left = logsLeft.addTo(tree, -1) - 1;
		if (left > 0) {
			return -1;
		}
		logsLeft.remove(tree);
		return tree;
	}

	private void deepen(int x, int floor, int z) {
		int target = Math.max(ShadowMaterials.BOTTOM_Y, floor);
		for (int dx = -1; dx <= 1; dx++) {
			for (int dz = -1; dz <= 1; dz++) {
				long ck = chunkKey((x + dx) >> 4, (z + dz) >> 4);
				Chunk c = chunks.get(ck);
				if (c == null) {
					continue;
				}
				int i = ((x + dx) & 15) | ((z + dz) & 15) << 4;
				if (target < c.floor[i]) {
					c.floor[i] = target;
					dirty.add(ck);
				}
			}
		}
	}

	/** Server thread, every tick: rebuilds dirty chunks, nearest work first come first served, within a cell budget. */
	public void tick(Host host) {
		Long ck;
		while ((ck = CHANGED.poll()) != null) {
			if (active) {
				dirty.add(ck.longValue());
			}
		}
		if (!active || dirty.isEmpty()) {
			return;
		}
		int spent = 0;
		var it = dirty.iterator();
		while (it.hasNext() && spent < CELLS_PER_TICK) {
			long key = it.nextLong();
			if (!host.chunkReady(key)) {
				continue;
			}
			it.remove();
			spent += build(host, key);
		}
	}

	private int build(Host host, long ck) {
		int cx = BlockKey.chunkX(ck), cz = BlockKey.chunkZ(ck);
		int x0 = cx << 4, z0 = cz << 4;
		List<SkyTri> tris = new ArrayList<>();
		CollisionStore.INSTANCE.trianglesNear(x0, -1e9, z0, x0 + 16, 1e9, z0 + 16, tris);
		CollisionStore.INSTANCE.surfaceNear(x0, -1e9, z0, x0 + 16, 1e9, z0 + 16, tris);
		Chunk c = chunks.computeIfAbsent(ck, k -> new Chunk());
		Long2ObjectOpenHashMap<String> want = new Long2ObjectOpenHashMap<>();
		CellSink sink = (x, y, z, block) -> {
			if (BlockKey.fits(x, y, z) && (x >> 4) == cx && (z >> 4) == cz) {
				want.putIfAbsent(BlockKey.pack(x, y, z), block);
			}
		};
		if (!tris.isEmpty()) {
			List<SkyTri> column = new ArrayList<>();
			for (int dz = 0; dz < 16; dz++) {
				for (int dx = 0; dx < 16; dx++) {
					double px = x0 + dx + 0.5, pz = z0 + dz + 0.5;
					column.clear();
					for (SkyTri t : tris) {
						if (t.minX <= px && t.maxX >= px && t.minZ <= pz && t.maxZ >= pz) {
							column.add(t);
						}
					}
					ShadowColumn.Sample s = ShadowColumn.sample(column, px, pz);
					int i = dx | dz << 4;
					c.protectedCols[i] = ShadowPlanner.protects(s);
					if (c.floor[i] == Integer.MAX_VALUE) {
						c.floor[i] = ShadowPlanner.defaultFloor(s);
					}
					int top = Double.isNaN(s.terrain()) ? 0 : ShadowColumn.solidTop(s.terrain());
					boolean water = HostWater.active() && HostWater.anyIn(x0 + dx - 2, top - 1, z0 + dz - 2, x0 + dx + 2, top + 2, z0 + dz + 2);
					ShadowPlanner.column(seed, s, x0 + dx, z0 + dz, water, c.floor[i], sink);
				}
			}
		}
		Long2IntOpenHashMap logs = new Long2IntOpenHashMap();
		for (int rx = cx - 1; rx <= cx + 1; rx++) {
			for (int rz = cz - 1; rz <= cz + 1; rz++) {
				for (Trees.Tree t : TREES.getOrDefault(chunkKey(rx, rz), List.of())) {
					TreeLayout.blocks(t.kind(), t.x(), t.y(), t.z(), t.height(), t.radius(), (x, y, z, block) -> {
						if (BlockKey.fits(x, y, z) && (x >> 4) == cx && (z >> 4) == cz) {
							long k = BlockKey.pack(x, y, z);
							String old = want.get(k);
							if (old == null || old.endsWith("grass") || old.startsWith("minecraft:tall_grass")) {
								want.put(k, block);
								if (block.contains("_log")) {
									logs.put(k, t.id());
								}
							}
						}
					});
				}
			}
		}
		int placed = 0;
		LongOpenHashSet cells = new LongOpenHashSet(want.size());
		for (var e : want.long2ObjectEntrySet()) {
			long k = e.getLongKey();
			if (host.playerOwns(k)) {
				logs.remove(k);
				continue;
			}
			cells.add(k);
			if (!c.cells.contains(k) || !host.holds(k, e.getValue())) {
				host.place(k, e.getValue());
				placed++;
			}
		}
		for (long k : c.cells) {
			if (!cells.contains(k) && !host.playerOwns(k)) {
				host.place(k, null);
				placed++;
			}
		}
		for (var e : c.logs.long2IntEntrySet()) {
			logsLeft.addTo(e.getIntValue(), -1);
		}
		for (var e : logs.long2IntEntrySet()) {
			logsLeft.addTo(e.getIntValue(), 1);
		}
		logsLeft.values().removeIf(v -> v <= 0);
		c.logs.clear();
		c.logs.putAll(logs);
		c.cells = cells;
		c.planned = new LongOpenHashSet(want.keySet());
		ShadowCells.INSTANCE.setChunk(ck, cells);
		return Math.max(placed, 1);
	}
}
