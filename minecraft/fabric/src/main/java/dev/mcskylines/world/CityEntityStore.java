package dev.mcskylines.world;

import java.io.IOException;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashMap;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ThreadLocalRandom;
import java.util.function.LongPredicate;
import net.minecraft.core.UUIDUtil;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.nbt.NbtUtils;
import net.minecraft.nbt.Tag;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.ProblemReporter;
import net.minecraft.util.datafix.DataFixTypes;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.level.ChunkPos;
import net.minecraft.world.level.storage.TagValueInput;
import net.minecraft.world.level.storage.TagValueOutput;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * Protocol 1.18 on the Fabric guest: the open city's entities. The city save is their authority (CITY_ENTITIES); the
 * world only caches them. Each open (and each world attach) starts a new generation: entities carrying another
 * generation's tag, or none while no city is ready, are discarded when they join the world, so another city's mobs and
 * stale copies on disk never show. The city's own entities are restored from its blob as their chunks become ready;
 * those unloaded to disk are remembered (parked) so a capture always holds the whole city. Server thread only (CityEdits
 * calls it under its monitor).
 */
final class CityEntityStore {
	private static final Logger LOG = LoggerFactory.getLogger("mcskylines");
	private static final String PREFIX = "[MinecraftSkylines] ";

	private record Saved(int priority, CompoundTag tag) {
	}

	private long generation = ThreadLocalRandom.current().nextLong();
	private boolean sweep;
	private final Map<UUID, CompoundTag> restore = new LinkedHashMap<>();
	private final Map<UUID, Saved> parked = new HashMap<>();
	private final List<Entity> doomed = new ArrayList<>();
	private byte[] authority; // the newest whole-city blob known: the host's, then each capture

	/** A new open, a close or a re-attach: everything the world holds now is stale. */
	void begin(byte[] blob) {
		generation = ThreadLocalRandom.current().nextLong();
		sweep = true;
		restore.clear();
		parked.clear();
		doomed.clear();
		authority = blob;
	}

	/** The newest blob known (null when none), to restore from after the world re-attaches. */
	byte[] authority() {
		return authority;
	}

	/** Queues the blob's entities for restoring; throws when it cannot be read (the caller then never sends back). */
	void load(byte[] blob, ServerLevel level) throws IOException {
		EntityBlob.Decoded d = EntityBlob.decode(blob);
		List<CompoundTag> list = d.entities();
		int current = NbtUtils.getDataVersion(NbtUtils.addCurrentDataVersion(new CompoundTag()));
		if (!list.isEmpty() && d.dataVersion() < current) {
			CompoundTag chunk = new CompoundTag();
			ListTag all = new ListTag();
			all.addAll(list);
			chunk.put("Entities", all);
			chunk = DataFixTypes.ENTITY_CHUNK.updateToCurrentVersion(level.getServer().getFixerUpper(), chunk, d.dataVersion());
			list = new ArrayList<>();
			for (Tag t : chunk.getListOrEmpty("Entities")) {
				if (t instanceof CompoundTag c) {
					list.add(c);
				}
			}
		}
		for (CompoundTag t : list) {
			UUID id = t.read("UUID", UUIDUtil.CODEC).orElse(null);
			if (id != null) {
				restore.put(id, t);
			}
		}
		authority = blob;
		LOG.info(PREFIX + "city entities: {} to restore", restore.size());
	}

	/** ENTITY_LOAD in the city world. */
	void joined(Entity e, boolean cityReady) {
		switch (CityEntityRule.onLoad(e instanceof Player, e.entityTags(), generation, cityReady)) {
			case KEEP -> parked.remove(e.getUUID());
			case ADOPT -> e.addTag(CityEntityRule.tag(generation));
			case DISCARD -> doomed.add(e);
		}
	}

	/** ENTITY_UNLOAD in the city world: one of ours going to disk with its chunk stays part of the city. */
	void left(Entity e, ServerLevel level) {
		if (e.getRemovalReason() == Entity.RemovalReason.UNLOADED_TO_CHUNK && e.entityTags().contains(CityEntityRule.tag(generation))) {
			Saved s = save(e, level);
			if (s != null) {
				parked.put(e.getUUID(), s);
			}
		}
	}

	/** Server tick: discards stale entities, then restores the city's own where their chunk is loaded and built. */
	void tick(ServerLevel level, LongPredicate built) {
		if (sweep) {
			sweep = false;
			for (Entity e : level.getAllEntities()) {
				if (!(e instanceof Player) && !e.entityTags().contains(CityEntityRule.tag(generation))) {
					doomed.add(e);
				}
			}
		}
		if (!doomed.isEmpty()) {
			for (Entity e : doomed) {
				if (!e.isRemoved()) {
					e.discard();
				}
			}
			LOG.debug(PREFIX + "city entities: discarded {} not of this city", doomed.size());
			doomed.clear();
		}
		Iterator<Map.Entry<UUID, CompoundTag>> it = restore.entrySet().iterator();
		while (it.hasNext()) {
			Map.Entry<UUID, CompoundTag> en = it.next();
			ListTag pos = en.getValue().getListOrEmpty("Pos");
			long ck = ChunkPos.pack(((int) Math.floor(pos.getDoubleOr(0, 0))) >> 4, ((int) Math.floor(pos.getDoubleOr(2, 0))) >> 4);
			if (!level.areEntitiesLoaded(ck) || !built.test(ck)) {
				continue;
			}
			it.remove();
			spawn(level, en.getKey(), en.getValue());
		}
	}

	private void spawn(ServerLevel level, UUID id, CompoundTag tag) {
		Entity old = level.getEntity(id);
		if (old != null) {
			if (old.entityTags().contains(CityEntityRule.tag(generation))) {
				return;
			}
			old.discard();
		}
		try (ProblemReporter.ScopedCollector reporter = new ProblemReporter.ScopedCollector(LOG)) {
			Entity e = EntityType.loadEntityRecursive(TagValueInput.create(reporter, level.registryAccess(), tag), level,
				EntitySpawnReason.LOAD, x -> x);
			if (e == null) {
				LOG.warn(PREFIX + "city entities: could not restore {}", tag.getStringOr("id", "?"));
				return;
			}
			String ours = CityEntityRule.tag(generation);
			e.getSelfAndPassengers().forEach(x -> {
				x.entityTags().removeIf(CityEntityRule::isGenTag);
				x.addTag(ours);
			});
			if (!level.tryAddFreshEntityWithPassengers(e)) {
				LOG.warn(PREFIX + "city entities: {} {} already in the world", tag.getStringOr("id", "?"), id);
			}
		}
	}

	/** The whole city's entities as a CITY_ENTITIES blob: live ones of this generation, parked, and not yet restored. */
	byte[] capture(ServerLevel level) {
		List<Saved> all = new ArrayList<>();
		String ours = CityEntityRule.tag(generation);
		for (Entity e : level.getAllEntities()) {
			if (e.entityTags().contains(ours)) {
				Saved s = save(e, level);
				if (s != null) {
					all.add(s);
				}
			}
		}
		all.addAll(parked.values());
		for (CompoundTag t : restore.values()) {
			all.add(new Saved(1, t));
		}
		all.sort(Comparator.comparingInt(Saved::priority));
		List<CompoundTag> tags = new ArrayList<>(all.size());
		for (Saved s : all) {
			tags.add(s.tag());
		}
		byte[] blob = EntityBlob.encode(tags, NbtUtils.getDataVersion(NbtUtils.addCurrentDataVersion(new CompoundTag())));
		authority = blob;
		return blob;
	}

	private static Saved save(Entity e, ServerLevel level) {
		if (!CityEntityRule.saved(e instanceof Player, e.isPassenger(), e.getType().canSerialize(), e.isRemoved() && !e.getRemovalReason().shouldSave())) {
			return null;
		}
		try (ProblemReporter.ScopedCollector reporter = new ProblemReporter.ScopedCollector(LOG)) {
			TagValueOutput out = TagValueOutput.createWithContext(reporter, level.registryAccess());
			if (!e.saveAsPassenger(out)) {
				return null;
			}
			boolean mob = e instanceof Mob;
			return new Saved(EntityBlob.priority(mob, mob && ((Mob) e).isPersistenceRequired()), out.buildResult());
		}
	}
}
