package dev.mcskylines.world;

import dev.mcskylines.player.DevWorld;
import dev.mcskylines.shadow.ShadowPlanner;
import dev.mcskylines.shadow.ShadowWorld;
import java.util.Optional;
import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.util.RandomSource;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.MobCategory;
import net.minecraft.world.entity.SpawnPlacements;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.levelgen.Heightmap;
import net.minecraft.world.phys.AABB;

/**
 * Passive animals near each player of the city world, by city context (AnimalChoice), every 5 s: vanilla spawns
 * passive mobs almost only at world generation, which a city world never has. Respects vanilla's per-player caps
 * (MobCategory.getMaxInstancesPerChunk within 128 blocks) and, on land, vanilla's spawn rules (grass below, light).
 * Server thread.
 */
public final class AnimalSpawner {
	private static final int PERIOD_TICKS = 100;
	private static final int ATTEMPTS = 3;
	private static final double CAP_RANGE = 128;

	private AnimalSpawner() {
	}

	public static void tick(MinecraftServer server) {
		if (server.getTickCount() % PERIOD_TICKS != 0 || !DevWorld.isOurs(server)) {
			return;
		}
		for (ServerPlayer player : server.getPlayerList().getPlayers()) {
			if (player.level() instanceof ServerLevel level && !player.isSpectator()) {
				for (int i = 0; i < ATTEMPTS; i++) {
					attempt(level, player, level.getRandom());
				}
			}
		}
	}

	private static void attempt(ServerLevel level, ServerPlayer player, RandomSource random) {
		double angle = random.nextDouble() * Math.PI * 2, dist = 24 + random.nextDouble() * 24;
		int x = (int) Math.floor(player.getX() + Math.cos(angle) * dist), z = (int) Math.floor(player.getZ() + Math.sin(angle) * dist);
		if (!level.hasChunk(x >> 4, z >> 4)) {
			return;
		}
		BlockPos ground = new BlockPos(x, level.getHeight(Heightmap.Types.MOTION_BLOCKING_NO_LEAVES, x, z), z);
		BlockPos water = waterAt(level, ground);
		int built = 0;
		for (int k = 0; k < 8; k++) {
			double a = k * Math.PI / 4;
			int sx = x + (int) Math.round(Math.cos(a) * 12), sz = z + (int) Math.round(Math.sin(a) * 12);
			var below = level.getBlockState(new BlockPos(sx, level.getHeight(Heightmap.Types.MOTION_BLOCKING_NO_LEAVES, sx, sz) - 1, sz));
			String name = BuiltInRegistries.BLOCK.getKey(below.getBlock()).toString();
			if (name.equals(ShadowPlanner.PAVED) || below.is(Blocks.BARRIER)) {
				built++;
			}
		}
		AnimalChoice.Context context = AnimalChoice.classify(water != null, level.getBlockState(ground.below()).is(Blocks.GRASS_BLOCK),
			ShadowWorld.treeNear(x, z, 12), built);
		String id = AnimalChoice.choose(context, random.nextDouble());
		if (id == null) {
			return;
		}
		Optional<EntityType<?>> type = BuiltInRegistries.ENTITY_TYPE.getOptional(Identifier.parse(id));
		if (type.isEmpty()) {
			return;
		}
		MobCategory category = type.get().getCategory();
		AABB around = player.getBoundingBox().inflate(CAP_RANGE);
		if (level.getEntitiesOfClass(Mob.class, around, m -> m.getType().getCategory() == category).size() >= category.getMaxInstancesPerChunk()) {
			return;
		}
		BlockPos at = water != null ? water : ground;
		if (water == null && !SpawnPlacements.checkSpawnRules(type.get(), level, EntitySpawnReason.NATURAL, at, random)) {
			return;
		}
		if (!level.noCollision(type.get().getSpawnAABB(at.getX() + 0.5, at.getY(), at.getZ() + 0.5))) {
			return;
		}
		type.get().spawn(level, at, EntitySpawnReason.NATURAL);
	}

	/** One block under the city's water surface over this column, or null when the host draws no water there. */
	private static BlockPos waterAt(ServerLevel level, BlockPos ground) {
		for (int dy = 0; dy < 24; dy++) {
			BlockPos p = ground.above(dy);
			var fluid = HostWater.fluidAt(level, p);
			if (fluid == null || fluid.isEmpty()) {
				return dy == 0 ? null : p.below();
			}
		}
		return null;
	}
}
