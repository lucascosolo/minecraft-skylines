/*
 * Ported from SkyCraft (https://github.com/chasmlol/SkyCraft) SkyCollider, MIT License, Copyright (c) 2026 chasmlol.
 * See minecraft/THIRD-PARTY-NOTICES.md. Adapted for Minecraft Skylines.
 */
package dev.mcskylines.collision;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;

/** Feeds the local player's movement through {@link TriCollider} against the nearby streamed triangles. */
public final class PlayerCollider {
	private PlayerCollider() {
	}

	/** True while the host has streamed any collision geometry; the mixins stay inert otherwise. */
	public static boolean active() {
		return CollisionStore.INSTANCE.regionCount() > 0;
	}

	public static Vec3 collide(LocalPlayer player, Vec3 move) {
		AABB box = player.getBoundingBox().expandTowards(move).inflate(1.0, 1.0 + player.maxUpStep(), 1.0);
		List<SkyTri> tris = new ArrayList<>();
		CollisionStore.INSTANCE.trianglesNear(box.minX, box.minY, box.minZ, box.maxX, box.maxY, box.maxZ, tris);
		if (tris.isEmpty()) {
			return move;
		}
		AABB body = player.getBoundingBox();
		double[] r = TriCollider.resolve(tris, (body.minX + body.maxX) * 0.5, body.minY, (body.minZ + body.maxZ) * 0.5,
				body.getXsize() * 0.5, body.getYsize(), player.maxUpStep(), player.onGround(), move.x, move.y, move.z);
		if (r[0] == move.x && r[1] == move.y && r[2] == move.z) {
			return move;
		}
		// Snapping down a slope or pushing out of a wall can move the player into a Minecraft block; collide again.
		return Entity.collideBoundingBox(player, new Vec3(r[0], r[1], r[2]), body, player.level(), List.of());
	}
}
