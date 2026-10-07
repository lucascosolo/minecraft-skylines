package dev.mcskylines.debug;

import dev.mcskylines.collision.CollisionStore;
import dev.mcskylines.collision.SkyTri;
import dev.mcskylines.shadow.ShadowColumn;
import dev.mcskylines.shadow.ShadowObstacles;
import dev.mcskylines.shadow.ShadowPlanner;
import dev.mcskylines.world.BlockKey;
import dev.mcskylines.world.CityEdits;
import it.unimi.dsi.fastutil.ints.Int2ObjectOpenHashMap;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;
import java.util.UUID;
import net.minecraft.client.Minecraft;
import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.ExperienceOrb;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.HitResult;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/** Ctrl+Shift+D on the guest: logs client entities, the look cell, and the server's block, shadow and entity state. */
public final class ShadowDump {
	private static final Logger LOG = LoggerFactory.getLogger("mcskylines");
	private static final String PREFIX = "[MinecraftSkylines] ";
	private static final double RADIUS = 8;

	private ShadowDump() {
	}

	/** Client thread: gathers the client part, then the server part on the server thread, and logs one block. */
	public static void run(Minecraft mc) {
		if (mc.player == null || mc.level == null || mc.getSingleplayerServer() == null) {
			LOG.info(PREFIX + "shadow dump: no player, level or server");
			return;
		}
		StringBuilder sb = new StringBuilder("===== shadow dump begin\n");
		var p = mc.player;
		sb.append(String.format("player pos=(%.3f, %.3f, %.3f) block=%s%n", p.getX(), p.getY(), p.getZ(), p.blockPosition()));
		BlockPos look = mc.hitResult instanceof BlockHitResult h && h.getType() == HitResult.Type.BLOCK ? h.getBlockPos() : null;
		sb.append("look cell=").append(look == null ? "none" : look.toString()).append('\n');
		Int2ObjectOpenHashMap<String> clientById = new Int2ObjectOpenHashMap<>();
		for (Entity e : near(mc.level, p)) {
			String c = String.format("(%.3f, %.3f, %.3f) onGround=%s noPhysics=%s", e.getX(), e.getY(), e.getZ(), e.onGround(), e.noPhysics);
			clientById.put(e.getId(), c);
			sb.append("client entity ").append(e.getId()).append(' ').append(typeOf(e)).append(' ').append(c).append('\n');
		}
		UUID uuid = p.getUUID();
		mc.getSingleplayerServer().execute(() -> {
			try {
				var server = mc.getSingleplayerServer();
				var sp = server == null ? null : server.getPlayerList().getPlayer(uuid);
				if (sp == null) {
					sb.append("server: player not found\n");
				} else {
					serverPart(sb, server.overworld(), sp, look, clientById);
				}
			} catch (RuntimeException ex) {
				sb.append("server part failed: ").append(ex).append('\n');
			}
			sb.append("===== shadow dump end");
			LOG.info(PREFIX + "{}", sb);
		});
	}

	private static void serverPart(StringBuilder sb, ServerLevel level, Entity sp, BlockPos look, Int2ObjectOpenHashMap<String> client) {
		Set<BlockPos> columns = new LinkedHashSet<>();
		if (look != null) {
			columns.add(look);
		}
		BlockPos pb = sp.blockPosition();
		for (int dz = -1; dz <= 1; dz++) {
			for (int dx = -1; dx <= 1; dx++) {
				columns.add(new BlockPos(pb.getX() + dx, pb.getY(), pb.getZ() + dz));
			}
		}
		for (BlockPos col : columns) {
			double cx = col.getX() + 0.5, cz = col.getZ() + 0.5;
			List<SkyTri> near = new ArrayList<>();
			CollisionStore.INSTANCE.trianglesNear(cx - 0.5, -1e9, cz - 0.5, cx + 0.5, 1e9, cz + 0.5, near);
			List<SkyTri> all = new ArrayList<>(near);
			CollisionStore.INSTANCE.surfaceNear(cx - 0.5, -1e9, cz - 0.5, cx + 0.5, 1e9, cz + 0.5, all);
			ShadowColumn.Sample s = ShadowColumn.sample(all, cx, cz);
			int top = Double.isNaN(s.terrain()) ? 0 : ShadowColumn.solidTop(s.terrain());
			sb.append(String.format("column %d,%d terrain=%.3f road=%.3f building=%.3f ny=%.3f solidTop=%d protects=%s obstacleTop=%.3f%n",
				col.getX(), col.getZ(), s.terrain(), s.road(), s.building(), s.terrainNy(), top, ShadowPlanner.protects(s),
				ShadowObstacles.top(near, col.getX(), col.getZ(), s.terrain())));
			Set<BlockPos> cells = new LinkedHashSet<>();
			for (int y = top - 2; y <= top + 3; y++) {
				cells.add(new BlockPos(col.getX(), y, col.getZ()));
			}
			if (col == look) {
				cells.add(look);
			}
			for (BlockPos c : cells) {
				String shadow = BlockKey.fits(c.getX(), c.getY(), c.getZ())
					? CityEdits.describeCell(level, BlockKey.pack(c.getX(), c.getY(), c.getZ())) : "out of key range";
				sb.append("  cell ").append(c.getX()).append(',').append(c.getY()).append(',').append(c.getZ()).append(' ')
					.append(level.getBlockState(c)).append(" | ").append(shadow).append('\n');
			}
		}
		for (Entity e : near(level, sp)) {
			List<SkyTri> tris = new ArrayList<>();
			CollisionStore.INSTANCE.trianglesNear(e.getX() - 0.5, -1e9, e.getZ() - 0.5, e.getX() + 0.5, 1e9, e.getZ() + 0.5, tris);
			CollisionStore.INSTANCE.surfaceNear(e.getX() - 0.5, -1e9, e.getZ() - 0.5, e.getX() + 0.5, 1e9, e.getZ() + 0.5, tris);
			double terrain = ShadowColumn.sample(tris, e.getX(), e.getZ()).terrain();
			sb.append(String.format("server entity %d %s pos=(%.3f, %.3f, %.3f) onGround=%s noPhysics=%s delta=%s terrain=%.3f yAbove=%.3f",
				e.getId(), typeOf(e), e.getX(), e.getY(), e.getZ(), e.onGround(), e.noPhysics, e.getDeltaMovement(), terrain, e.getY() - terrain));
			if (e instanceof ItemEntity item) {
				sb.append(" hasPickupDelay=").append(item.hasPickUpDelay()).append(" pickupDelay=n/a age=").append(item.getAge());
			} else if (e instanceof ExperienceOrb orb) {
				sb.append(" xpValue=").append(orb.getValue());
			}
			String c = client.get(e.getId());
			sb.append(c == null ? " client=none" : " client=" + c).append('\n');
		}
	}

	private static List<Entity> near(Level level, Entity center) {
		AABB box = center.getBoundingBox().inflate(RADIUS);
		return new ArrayList<>(level.getEntities(center, box, e -> e.distanceTo(center) <= RADIUS));
	}

	private static String typeOf(Entity e) {
		return BuiltInRegistries.ENTITY_TYPE.getKey(e.getType()).toString();
	}
}
