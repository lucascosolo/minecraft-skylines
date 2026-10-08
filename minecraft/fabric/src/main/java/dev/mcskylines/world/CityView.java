package dev.mcskylines.world;

import dev.mcskylines.protocol.CityFocus;
import java.util.List;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.TicketType;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.level.ChunkPos;
import net.minecraft.world.phys.AABB;

/**
 * Protocol 1.18 CITY_FOCUS on the guest: while the city view is in use, a chunk ticket keeps the area around where
 * CS1's camera looks loaded and simulated (CameraArea), mobs there are not despawned for being far from the player,
 * and the entities there are published for EntityExporter. The focus arrives on the client thread; everything else
 * runs on the server thread (CityEdits.serverTick).
 */
public final class CityView {
	// DRAGON is vanilla's non-persistent, non-expiring loading + simulation ticket (flags 6); vanilla uses it only in
	// the End at chunk 0,0, so it never meets ours in the city's overworld. A registered type of our own would need a
	// main-entrypoint registry write before the freeze, which this client-only mod does not have.
	private static final TicketType TICKET = TicketType.DRAGON;

	private static volatile CameraArea wanted;
	private static volatile CameraArea held;
	private static volatile ServerLevel heldLevel;
	private static volatile List<Entity> entities = List.of();

	private CityView() {
	}

	/** Client thread: CITY_FOCUS. */
	public static void accept(CityFocus f) {
		wanted = f.active() ? CameraArea.at(f.x(), f.z()) : null;
	}

	/** Client thread: a new open, CITY_CLOSE or link loss. */
	public static void clear() {
		wanted = null;
	}

	/** Server thread, every tick: the ticket follows the focus while the city is ready; publishes the area's entities. */
	static void tick(ServerLevel level, boolean cityReady) {
		CameraArea target = cityReady ? wanted : null;
		if (target == null ? held != null : !target.equals(held) || heldLevel != level) {
			release();
			if (target != null) {
				level.getChunkSource().addTicketWithRadius(TICKET, new ChunkPos(target.chunkX(), target.chunkZ()), CameraArea.TICKET_RADIUS);
				heldLevel = level;
				held = target;
			}
		}
		CameraArea a = held;
		entities = a == null ? List.of() : List.copyOf(level.getEntities((Entity) null,
			new AABB(a.minX(), level.getMinY(), a.minZ(), a.maxX(), level.getMaxY() + 1, a.maxZ()), e -> !(e instanceof Player)));
	}

	/** Server thread: drops the ticket (the world detaches, the city is no longer ready). */
	static void release() {
		CameraArea a = held;
		ServerLevel l = heldLevel;
		if (a != null && l != null) {
			l.getChunkSource().removeTicketWithRadius(TICKET, new ChunkPos(a.chunkX(), a.chunkZ()), CameraArea.TICKET_RADIUS);
		}
		held = null;
		heldLevel = null;
		entities = List.of();
	}

	/** Mob.checkDespawn: the city view's area keeps its mobs however far the player is. */
	public static boolean keepsFromDespawn(Mob m) {
		CameraArea a = held;
		return a != null && m.level() == heldLevel && a.ticksAt(m.getX(), m.getZ());
	}

	/** The simulated city-view area in {@code level}, or null. */
	public static CameraArea area(ServerLevel level) {
		CameraArea a = held;
		return a != null && heldLevel == level ? a : null;
	}

	/** Any thread: the server's non-player entities in the city view's area, as of the last server tick. */
	public static List<Entity> entities() {
		return entities;
	}
}
