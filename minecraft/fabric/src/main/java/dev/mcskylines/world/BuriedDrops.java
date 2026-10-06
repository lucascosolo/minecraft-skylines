package dev.mcskylines.world;

import dev.mcskylines.collision.CollisionStore;
import dev.mcskylines.collision.SkyRay;
import dev.mcskylines.collision.SkyTri;
import dev.mcskylines.player.DevWorld;
import java.util.ArrayList;
import java.util.List;
import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.Level;

/**
 * Drops from blocks broken below CS1's ground (shadow terrain, ore) would fall through it: nothing there collides
 * with items (the shadow collision rule), and CS1 does not draw Minecraft items yet. Until entities are drawn in CS1
 * (docs/plans/survival.md step 4) such drops, and their experience, go straight to the nearest player (owner,
 * 2026-10-06: "I don't get items dropped"); what does not fit lands at the player's feet.
 */
public final class BuriedDrops {
	private static final double REACH = 8.0;
	private static final double PROBE_UP = 64.0;
	/** Set once entities (dropped items) are drawn in CS1; then only drops from under the ground are handed over. */
	static final boolean ENTITIES_DRAWN_IN_CS1 = false;

	private BuriedDrops() {
	}

	/** The player who should receive a drop at {@code pos}, or null when vanilla dropping is fine. */
	public static ServerPlayer receiver(Level level, BlockPos pos) {
		if (!(level instanceof ServerLevel server) || level.dimension() != Level.OVERWORLD || !DevWorld.isOurs(server.getServer())) {
			return null;
		}
		// Until entities are drawn in CS1 every block drop near the player is handed over (items on the ground would be
		// invisible), not only drops from under the ground.
		if (ENTITIES_DRAWN_IN_CS1 && !belowHostSurface(pos)) {
			return null;
		}
		ServerPlayer best = null;
		double bestD = REACH * REACH;
		for (ServerPlayer p : server.players()) {
			double d = p.distanceToSqr(pos.getX() + 0.5, pos.getY() + 0.5, pos.getZ() + 0.5);
			if (d <= bestD && p.isAlive()) {
				best = p;
				bestD = d;
			}
		}
		return best;
	}

	/** Gives the stack to the player; the rest is dropped at the player's feet. */
	public static void give(ServerPlayer player, ItemStack stack) {
		ItemStack rest = stack.copy();
		player.getInventory().add(rest);
		if (!rest.isEmpty()) {
			ItemEntity e = new ItemEntity(player.level(), player.getX(), player.getY() + 0.25, player.getZ(), rest);
			e.setDefaultPickUpDelay();
			player.level().addFreshEntity(e);
		}
	}

	// True when the city's triangles have a surface above the centre of the block (the block was under CS1's ground).
	static boolean belowHostSurface(BlockPos pos) {
		double cx = pos.getX() + 0.5, cy = pos.getY() + 0.5, cz = pos.getZ() + 0.5;
		List<SkyTri> tris = new ArrayList<>();
		CollisionStore.INSTANCE.trianglesNear(cx - 0.01, cy, cz - 0.01, cx + 0.01, cy + PROBE_UP, cz + 0.01, tris);
		return !tris.isEmpty() && SkyRay.cast(tris, cx, cy + PROBE_UP, cz, cx, cy, cz) != null;
	}
}
