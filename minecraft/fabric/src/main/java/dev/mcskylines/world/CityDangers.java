package dev.mcskylines.world;

import dev.mcskylines.protocol.CityConditions;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import net.fabricmc.fabric.api.event.player.PlayerBlockBreakEvents;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.util.RandomSource;
import net.minecraft.world.effect.MobEffectInstance;
import net.minecraft.world.effect.MobEffects;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.MobCategory;
import net.minecraft.world.entity.SpawnPlacements;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.level.block.BaseFireBlock;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.levelgen.Heightmap;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;

/**
 * Protocol 1.21: the city's problems as Minecraft dangers, only in the simulated area (around each player and the city
 * view's area) of an open, ready, Minecraft-enabled city. Numbers in CityHazards; decisions in docs/DECISIONS.md.
 * Server thread.
 */
public final class CityDangers {
	private static final int SECOND = 20;
	private static final int SPAWN_PERIOD = 100;
	private static final double FIRE_RANGE = 96;
	private static final double CAP_RANGE = 128;
	private static CityEdits city;

	private CityDangers() {
	}

	public static void register(CityEdits c) {
		city = c;
		PlayerBlockBreakEvents.AFTER.register((level, player, pos, state, be) -> {
			if (level instanceof ServerLevel sl && active(sl) && (state.is(Blocks.SHORT_GRASS) || state.is(Blocks.TALL_GRASS))) {
				CityConditions.Cell cell = CityConditionsStore.at(pos.getX() + 0.5, pos.getZ() + 0.5);
				if (cell != null && sl.getRandom().nextDouble() < CityHazards.saplingChance(cell.forest())) {
					net.minecraft.world.level.block.Block.popResource(sl, pos, new ItemStack(Items.OAK_SAPLING));
				}
			}
		});
	}

	private static boolean active(ServerLevel level) {
		CityEdits c = city;
		if (c == null || c.cityLevel() != level) {
			return false;
		}
		long seq = c.readySeq();
		return seq >= 0 && CityConditionsStore.currentFor((int) seq);
	}

	public static void tick(MinecraftServer server) {
		if (server.getTickCount() % SECOND != 0 || city == null) {
			return;
		}
		ServerLevel level = city.cityLevel();
		if (level == null || !active(level)) {
			return;
		}
		RandomSource random = level.getRandom();
		List<Vec3> centres = new ArrayList<>();
		for (ServerPlayer p : level.players()) {
			if (!p.isSpectator()) {
				centres.add(p.position());
				poison(p);
			}
		}
		CameraArea area = CityView.area(level);
		if (area != null) {
			centres.add(new Vec3((area.minX() + area.maxX()) / 2, 0, (area.minZ() + area.maxZ()) / 2));
		}
		for (CityConditions.Fire f : CityConditionsStore.fires()) {
			if (centres.stream().anyMatch(c -> Math.hypot(c.x - f.x(), c.z - f.z()) <= FIRE_RANGE)) {
				burn(level, f, random);
			}
		}
		if (server.getTickCount() % SPAWN_PERIOD == 0 && level.isDarkOutside()) {
			for (Vec3 c : centres) {
				spawnNear(level, c, random);
			}
		}
	}

	private static void poison(ServerPlayer p) {
		CityConditions.Cell c = CityConditionsStore.at(p.getX(), p.getZ());
		int amp = c == null ? -1 : CityHazards.poisonAmplifier(c.pollution());
		if (amp >= 0 && p.onGround() && !p.isCreative()) {
			p.addEffect(new MobEffectInstance(MobEffects.POISON, 60, amp));
		}
	}

	// Entities by the building catch fire; fire blocks are lit on the ground around it (they never reach the city).
	private static void burn(ServerLevel level, CityConditions.Fire f, RandomSource random) {
		double reach = f.radius() + 2;
		AABB box = new AABB(f.x() - reach, f.y() - 2, f.z() - reach, f.x() + reach, f.y() + 24, f.z() + reach);
		for (Entity e : level.getEntities((Entity) null, box, e -> !e.fireImmune())) {
			if (CityHazards.inFireReach(e.getX() - f.x(), e.getZ() - f.z(), f.radius()) && !(e instanceof ServerPlayer sp && sp.isCreative())) {
				e.igniteForSeconds(4);
			}
		}
		for (int i = CityHazards.firesPerSecond(f.intensity()); i > 0; i--) {
			double a = random.nextDouble() * Math.PI * 2, d = f.radius() + 1 + random.nextDouble() * 3;
			int x = (int) Math.floor(f.x() + Math.cos(a) * d), z = (int) Math.floor(f.z() + Math.sin(a) * d);
			if (!level.hasChunk(x >> 4, z >> 4)) {
				continue;
			}
			BlockPos at = new BlockPos(x, level.getHeight(Heightmap.Types.MOTION_BLOCKING_NO_LEAVES, x, z), z);
			if (level.getBlockState(at).isAir() && BaseFireBlock.canBePlacedAt(level, at, Direction.UP)) {
				level.setBlockAndUpdate(at, BaseFireBlock.getState(level, at));
			}
		}
	}

	// Crime raises night-time hostile spawns there, uncollected dead raise zombies; vanilla's spawn rules still decide.
	private static void spawnNear(ServerLevel level, Vec3 centre, RandomSource random) {
		CityConditions.Cell c = CityConditionsStore.at(centre.x, centre.z);
		if (c == null) {
			return;
		}
		for (int i = CityHazards.crimeAttempts(c.crime()); i > 0; i--) {
			attempt(level, centre, CityHazards.crimeMob(random.nextDouble()), random);
		}
		for (int i = CityHazards.zombieAttempts(c.dead()); i > 0; i--) {
			attempt(level, centre, CityHazards.deadMob(random.nextDouble()), random);
		}
	}

	private static void attempt(ServerLevel level, Vec3 centre, String id, RandomSource random) {
		double a = random.nextDouble() * Math.PI * 2, d = 24 + random.nextDouble() * 16;
		int x = (int) Math.floor(centre.x + Math.cos(a) * d), z = (int) Math.floor(centre.z + Math.sin(a) * d);
		if (!level.hasChunk(x >> 4, z >> 4)) {
			return;
		}
		Optional<EntityType<?>> type = BuiltInRegistries.ENTITY_TYPE.getOptional(Identifier.parse(id));
		if (type.isEmpty()) {
			return;
		}
		AABB around = new AABB(centre, centre).inflate(CAP_RANGE);
		if (level.getEntitiesOfClass(Mob.class, around, m -> m.getType().getCategory() == MobCategory.MONSTER).size()
				>= MobCategory.MONSTER.getMaxInstancesPerChunk()) {
			return;
		}
		BlockPos at = new BlockPos(x, level.getHeight(Heightmap.Types.MOTION_BLOCKING_NO_LEAVES, x, z), z);
		if (!SpawnPlacements.checkSpawnRules(type.get(), level, EntitySpawnReason.NATURAL, at, random)
				|| !level.noCollision(type.get().getSpawnAABB(at.getX() + 0.5, at.getY(), at.getZ() + 0.5))) {
			return;
		}
		type.get().spawn(level, at, EntitySpawnReason.NATURAL);
	}
}
