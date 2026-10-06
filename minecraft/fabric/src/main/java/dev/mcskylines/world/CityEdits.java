package dev.mcskylines.world;

import com.mojang.brigadier.exceptions.CommandSyntaxException;
import dev.mcskylines.bridge.BridgeGuest;
import dev.mcskylines.bridge.ProtocolException;
import dev.mcskylines.player.DevWorld;
import dev.mcskylines.protocol.AppProtocol;
import dev.mcskylines.protocol.BlockEdits;
import dev.mcskylines.protocol.CityClose;
import dev.mcskylines.protocol.CityOpen;
import dev.mcskylines.protocol.CityState;
import dev.mcskylines.protocol.EditSync;
import dev.mcskylines.protocol.GuestStatus;
import dev.mcskylines.protocol.LightSources;
import it.unimi.dsi.fastutil.longs.Long2IntOpenHashMap;
import it.unimi.dsi.fastutil.longs.Long2ObjectMap;
import it.unimi.dsi.fastutil.longs.Long2ObjectOpenHashMap;
import java.io.IOException;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.ConcurrentLinkedQueue;
import net.minecraft.commands.arguments.blocks.BlockStateParser;
import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.Registries;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.LightBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.chunk.LevelChunk;
import net.minecraft.world.level.storage.LevelResource;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * Protocol 1.5 on the guest: makes the city world's overworld match the open city's edit set and reports every
 * block change back. Bridge messages are decoded on the client thread and queued; everything else runs on the
 * integrated server thread (or on the client thread while no city-world server is running, when no world exists
 * to touch). All state is guarded by this object's monitor.
 */
public final class CityEdits {
	private static final Logger LOG = LoggerFactory.getLogger("mcskylines");
	private static final String PREFIX = "[MinecraftSkylines] ";
	// UPDATE_CLIENTS keeps players' views current; UPDATE_SKIP_ALL_SIDEEFFECTS (Block, 26.3: KNOWN_SHAPE |
	// SUPPRESS_DROPS | SKIP_BLOCK_ENTITY_SIDEEFFECTS | SKIP_ON_PLACE) means no shape updates to neighbours, no
	// drops (items or container contents) and no onPlace (falling blocks, fluids, redstone do not schedule ticks).
	// Without UPDATE_NEIGHBORS nothing around is notified, so the world ends exactly as the snapshot says.
	private static final int APPLY_FLAGS = Block.UPDATE_CLIENTS | Block.UPDATE_SKIP_ALL_SIDEEFFECTS;
	private static final Object LINK_DOWN = new Object();
	private static volatile CityEdits recording;

	private final BridgeGuest guest;
	private final ConcurrentLinkedQueue<Object> inbox = new ConcurrentLinkedQueue<>();
	private volatile MinecraftServer serverRef;
	private volatile UUID pairedSaveId = GuestStatus.NO_SAVE;

	private Open open;
	private Long2ObjectOpenHashMap<String> target; // what the world must match; null: leave it as it is
	private boolean targetApplied;
	private MinecraftServer server;
	private ServerLevel level;
	private TouchedSet touched;
	private Path touchedFile;
	private final Long2ObjectOpenHashMap<Long2ObjectMap<String>> pending = new Long2ObjectOpenHashMap<>();
	private final Map<String, Optional<BlockState>> parsed = new HashMap<>();
	private final EditRecorder<BlockState> recorder = new EditRecorder<>();
	private boolean applying;
	private final Long2IntOpenHashMap lamps = new Long2IntOpenHashMap(); // our light blocks: position to level

	private static final class Open {
		final CityOpen msg;
		final Long2ObjectOpenHashMap<String> snapshot = new Long2ObjectOpenHashMap<>();
		final Map<String, String> strings = new HashMap<>();
		long received;
		boolean complete;
		boolean ready;

		Open(CityOpen msg) {
			this.msg = msg;
		}

		int seq() {
			return msg.openSeq();
		}
	}

	public CityEdits(BridgeGuest guest) {
		this.guest = guest;
	}

	/** GUEST_STATUS pairedSaveId: the saveId of the current open, zero when none. */
	public UUID pairedSaveId() {
		return pairedSaveId;
	}

	/** Client thread: CITY_OPEN, BLOCK_EDITS, CITY_CLOSE or EDIT_SYNC from a host speaking minor 5. */
	public void deliver(int type, byte[] payload) {
		try {
			inbox.add(switch (type) {
				case AppProtocol.CITY_OPEN -> CityOpen.decode(payload);
				case AppProtocol.BLOCK_EDITS -> BlockEdits.decode(payload);
				case AppProtocol.CITY_CLOSE -> CityClose.decode(payload);
				case AppProtocol.EDIT_SYNC -> EditSync.decode(payload);
				case AppProtocol.LIGHT_SOURCES -> LightSources.decode(payload);
				default -> throw new IllegalArgumentException("not a city message: 0x" + Integer.toHexString(type));
			});
		} catch (ProtocolException e) {
			LOG.warn(PREFIX + "ignoring malformed city message 0x{}: {}", Integer.toHexString(type), e.getMessage());
			return;
		}
		dispatch();
	}

	/** Client thread: the link went down; stop recording (the host re-opens on reconnect). */
	public void linkDown() {
		inbox.add(LINK_DOWN);
		dispatch();
	}

	/** Client thread, every tick: handles messages left over while no server was taking them. */
	public void clientTick() {
		if (!inbox.isEmpty()) {
			dispatch();
		}
	}

	private void dispatch() {
		MinecraftServer s = serverRef;
		if (s != null) {
			s.execute(this::drain);
		} else {
			drain();
		}
	}

	private synchronized void drain() {
		if (server != null && !server.isSameThread()) {
			server.execute(this::drain);
			return;
		}
		Object m;
		while ((m = inbox.poll()) != null) {
			switch (m) {
				case CityOpen o -> onOpen(o);
				case BlockEdits b -> onSnapshot(b);
				case CityClose c -> onClose(c);
				case EditSync s -> onSync(s);
				case LightSources l -> onLights(l);
				default -> onLinkDown();
			}
		}
	}

	private void onOpen(CityOpen o) {
		lamps.clear();
		flushRecorded();
		stopRecording();
		open = new Open(o);
		pairedSaveId = o.saveId();
		LOG.info(PREFIX + "CITY_OPEN {} '{}' ({} edits)", Integer.toUnsignedString(o.openSeq()), o.cityName(),
			Integer.toUnsignedLong(o.editCount()));
		send(AppProtocol.CITY_STATE, new CityState(o.openSeq(), CityState.APPLYING, 0).encode());
	}

	private void onSnapshot(BlockEdits b) {
		if (open == null || open.complete || b.openSeq() != open.seq()) {
			LOG.debug(PREFIX + "dropping stale BLOCK_EDITS for open {}", Integer.toUnsignedString(b.openSeq()));
			return;
		}
		for (BlockEdits.Edit e : b.edits()) {
			if (!BlockKey.fits(e.x(), e.y(), e.z())) {
				LOG.warn(PREFIX + "skipping edit outside the world: {}, {}, {}", e.x(), e.y(), e.z());
				continue;
			}
			long key = BlockKey.pack(e.x(), e.y(), e.z());
			String state = b.palette().get(e.state());
			if (ReconcilePlan.AIR.equals(state)) {
				open.snapshot.remove(key);
			} else {
				open.snapshot.put(key, open.strings.computeIfAbsent(state, s -> s));
			}
		}
		open.received += b.edits().size();
		if (!b.last()) {
			return;
		}
		if (open.received != Integer.toUnsignedLong(open.msg.editCount())) {
			LOG.warn(PREFIX + "snapshot for open {} had {} edits, CITY_OPEN announced {}", Integer.toUnsignedString(open.seq()),
				open.received, Integer.toUnsignedLong(open.msg.editCount()));
		}
		open.complete = true;
		open.strings.clear();
		target = open.snapshot;
		targetApplied = false;
		applyTarget();
	}

	private void onClose(CityClose c) {
		if (open == null || c.openSeq() != open.seq()) {
			LOG.debug(PREFIX + "dropping stale CITY_CLOSE {}", Integer.toUnsignedString(c.openSeq()));
			return;
		}
		stopRecording();
		flushRecorded();
		lamps.clear();
		LOG.info(PREFIX + "CITY_CLOSE {}; reverting the city world to empty", Integer.toUnsignedString(c.openSeq()));
		open = null;
		pairedSaveId = GuestStatus.NO_SAVE;
		target = new Long2ObjectOpenHashMap<>();
		targetApplied = false;
		applyTarget();
		writeTouched();
		send(AppProtocol.CITY_STATE, new CityState(c.openSeq(), CityState.CLOSED, 0).encode());
	}

	private void onLights(LightSources l) {
		if (open == null || !open.ready || level == null) {
			return;
		}
		LampPlan plan = LampPlan.plan(lamps, LampPlan.wanted(l.lights()), this::cellAt);
		applying = true;
		try {
			BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
			for (var e : plan.place().long2IntEntrySet()) {
				long k = e.getLongKey();
				BlockState light = Blocks.LIGHT.defaultBlockState().setValue(LightBlock.LEVEL, e.getIntValue());
				level.setBlock(pos.set(BlockKey.x(k), BlockKey.y(k), BlockKey.z(k)), light, APPLY_FLAGS);
				touched.add(k);
				lamps.put(k, e.getIntValue());
			}
			for (long k : plan.remove()) {
				level.setBlock(pos.set(BlockKey.x(k), BlockKey.y(k), BlockKey.z(k)), Blocks.AIR.defaultBlockState(), APPLY_FLAGS);
				touched.remove(k);
				lamps.remove(k);
			}
			for (long k : plan.forget()) {
				lamps.remove(k);
			}
		} finally {
			applying = false;
		}
		LOG.debug(PREFIX + "LIGHT_SOURCES: {} placed, {} removed, {} forgotten", plan.place().size(), plan.remove().size(),
			plan.forget().size());
	}

	private LampPlan.Cell cellAt(long k) {
		if (level.getChunkSource().getChunkNow(BlockKey.chunkX(BlockKey.chunkKey(k)), BlockKey.chunkZ(BlockKey.chunkKey(k))) == null) {
			return LampPlan.Cell.UNLOADED;
		}
		BlockState state = level.getBlockState(new BlockPos(BlockKey.x(k), BlockKey.y(k), BlockKey.z(k)));
		if (state.isAir()) {
			return LampPlan.Cell.AIR;
		}
		return state.is(Blocks.LIGHT) && lamps.containsKey(k) ? LampPlan.Cell.OURS : LampPlan.Cell.OTHER;
	}

	private void onSync(EditSync s) {
		if (open != null && open.ready && s.openSeq() == open.seq()) {
			flushRecorded();
		}
		send(AppProtocol.EDIT_SYNC_ACK, s.encode());
	}

	private void onLinkDown() {
		lamps.clear();
		stopRecording();
		recorder.clear();
		open = null;
		pairedSaveId = GuestStatus.NO_SAVE;
	}

	/** Server thread (SERVER_STARTED). */
	public void attach(MinecraftServer s) {
		if (!DevWorld.isOurs(s)) {
			return;
		}
		synchronized (this) {
			server = s;
			level = s.overworld();
			lamps.clear();
			touchedFile = s.getWorldPath(LevelResource.ROOT).resolve("mcskylines").resolve("touched.bin");
			touched = new TouchedSet(TouchedFile.read(touchedFile));
			LOG.info(PREFIX + "city world attached; {} touched positions", touched.live().size());
			targetApplied = false;
			applyTarget();
			serverRef = s;
			drain();
		}
	}

	/** Server thread (SERVER_STOPPING): the final save follows, so persist everything that might differ. */
	public synchronized void detach(MinecraftServer s) {
		if (s != server) {
			return;
		}
		flushRecorded();
		stopRecording();
		writeTouched();
		if (open != null && open.ready) {
			open.ready = false;
			send(AppProtocol.CITY_STATE, new CityState(open.seq(), CityState.APPLYING, 0).encode());
		}
		lamps.clear();
		targetApplied = false;
		pending.clear();
		parsed.clear();
		serverRef = null;
		server = null;
		level = null;
		touched = null;
	}

	/** BEFORE_SAVE: positions added since the last write reach disk no later than their chunks. */
	public synchronized void beforeSave(MinecraftServer s) {
		if (s == server) {
			writeTouched();
		}
	}

	/** AFTER_SAVE: after a flushing save the chunks of reverted positions are on disk, so they can be forgotten. */
	public synchronized void afterSave(MinecraftServer s, boolean flush) {
		if (s == server && flush) {
			touched.savedWithFlush();
			writeTouched();
		}
	}

	/** CHUNK_UNLOAD: the chunk is about to be saved, so new touched positions go to disk first. */
	public synchronized void chunkUnloading(ServerLevel l) {
		if (l == level && touched.dirty()) {
			writeTouched();
		}
	}

	/** END_SERVER_TICK: queued edits for chunks that loaded, then at most one BLOCK_EDITS flush per tick. */
	public synchronized void serverTick(MinecraftServer s) {
		if (s != server) {
			return;
		}
		// Every queued chunk is checked each tick until it is fully loaded (owner's run 2026-10-06: a chunk whose
		// CHUNK_LOAD came before getChunkNow saw it, or before the reconcile, was dropped and its blocks never placed).
		// getChunkNow is a map lookup, so this stays cheap for the few hundred chunks a city touches.
		if (!pending.isEmpty()) {
			var it = pending.long2ObjectEntrySet().fastIterator();
			while (it.hasNext()) {
				var e = it.next();
				long key = e.getLongKey();
				if (level.getChunkSource().getChunkNow(BlockKey.chunkX(key), BlockKey.chunkZ(key)) != null) {
					applyChunk(e.getValue());
					it.remove();
				}
			}
			if (pending.isEmpty()) {
				LOG.info(PREFIX + "city world: all queued chunks applied");
			}
		}
		flushRecorded();
	}

	/** LevelChunkMixin: a block state in a loaded chunk changed (any level, any side). */
	public static void blockChanged(Level l, BlockPos pos, BlockState state) {
		CityEdits r = recording;
		if (r != null) {
			r.record(l, pos, state);
		}
	}

	private synchronized void record(Level l, BlockPos pos, BlockState state) {
		if (l != level || applying || recording != this || !BlockKey.fits(pos.getX(), pos.getY(), pos.getZ())) {
			return;
		}
		long key = BlockKey.pack(pos.getX(), pos.getY(), pos.getZ());
		touched.add(key);
		recorder.record(key, state);
	}

	private void applyTarget() {
		if (target == null || targetApplied || level == null) {
			return;
		}
		ReconcilePlan plan = ReconcilePlan.plan(touched.live(), target);
		target.keySet().forEach((long key) -> touched.add(key));
		pending.clear();
		int now = 0;
		for (var chunk : plan.byChunk().long2ObjectEntrySet()) {
			long ck = chunk.getLongKey();
			if (level.getChunkSource().getChunkNow(BlockKey.chunkX(ck), BlockKey.chunkZ(ck)) != null) {
				now += chunk.getValue().size();
				applyChunk(chunk.getValue());
			} else {
				pending.put(ck, chunk.getValue());
			}
		}
		targetApplied = true;
		LOG.info(PREFIX + "city world reconciled: {} edits, {} reverts; {} applied now, {} chunks queued until loaded",
			plan.edits(), plan.reverts(), now, pending.size());
		if (open != null && open.complete && !open.ready) {
			open.ready = true;
			recording = this;
			send(AppProtocol.CITY_STATE, new CityState(open.seq(), CityState.READY, target.size()).encode());
		}
	}

	private void applyChunk(Long2ObjectMap<String> edits) {
		applying = true;
		try {
			BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
			for (var e : edits.long2ObjectEntrySet()) {
				long key = e.getLongKey();
				Optional<BlockState> state = parse(e.getValue());
				if (state.isEmpty()) {
					continue;
				}
				level.setBlock(pos.set(BlockKey.x(key), BlockKey.y(key), BlockKey.z(key)), state.get(), APPLY_FLAGS);
				if (ReconcilePlan.AIR.equals(e.getValue())) {
					touched.remove(key);
				}
			}
		} finally {
			applying = false;
		}
	}

	private Optional<BlockState> parse(String state) {
		return parsed.computeIfAbsent(state, s -> {
			try {
				return Optional.of(BlockStateParser.parseForBlock(level.holderLookup(Registries.BLOCK), s, false).blockState());
			} catch (CommandSyntaxException e) {
				LOG.warn(PREFIX + "skipping unknown block state '{}': {}", s, e.getMessage());
				return Optional.empty();
			}
		});
	}

	private void flushRecorded() {
		if (recorder.isEmpty()) {
			return;
		}
		if (open == null || !open.ready) {
			recorder.clear();
			return;
		}
		List<BlockEdits> batches = recorder.drain(open.seq(), BlockStateParser::serialize);
		for (BlockEdits b : batches) {
			for (BlockEdits.Edit e : b.edits()) {
				long key = BlockKey.pack(e.x(), e.y(), e.z());
				String state = b.palette().get(e.state());
				if (ReconcilePlan.AIR.equals(state)) {
					open.snapshot.remove(key);
				} else {
					open.snapshot.put(key, state);
				}
			}
			send(AppProtocol.BLOCK_EDITS, b.encode());
		}
	}

	private void stopRecording() {
		if (recording == this) {
			recording = null;
		}
	}

	private void writeTouched() {
		if (touched == null) {
			return;
		}
		try {
			TouchedFile.write(touchedFile, touched.persistent());
			touched.markWritten();
		} catch (IOException e) {
			LOG.error(PREFIX + "could not write {}: {}", touchedFile, e.toString());
		}
	}

	private void send(int type, byte[] payload) {
		guest.send(type, payload);
	}
}
