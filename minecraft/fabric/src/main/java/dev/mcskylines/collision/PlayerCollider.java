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

/** Feeds the local player's movement through {@link TriCollider} against the nearby streamed triangles and moving obstacles. */
public final class PlayerCollider {
	private PlayerCollider() {
	}

	/** True while the host has streamed any collision geometry; the mixins stay inert otherwise. */
	public static boolean active() {
		return CollisionStore.INSTANCE.regionCount() > 0;
	}

	public static Vec3 collide(LocalPlayer player, Vec3 move) {
		AABB body = player.getBoundingBox();
		double fx = (body.minX + body.maxX) * 0.5, fy = body.minY, fz = (body.minZ + body.maxZ) * 0.5;
		double radius = body.getXsize() * 0.5, height = body.getYsize();
		AABB box = body.expandTowards(move).inflate(1.0, 1.0 + player.maxUpStep(), 1.0);
		List<SkyTri> tris = new ArrayList<>();
		CollisionStore.INSTANCE.trianglesNear(box.minX, box.minY, box.minZ, box.maxX, box.maxY, box.maxZ, tris);
		// A vehicle or citizen that moved into the player pushes it out; the rest are solid like the city.
		double pushX = 0, pushZ = 0;
		for (ObstacleBox o : DynamicObstacleStore.INSTANCE.current(System.nanoTime())) {
			if (o.overlaps(fx, fy, fz, radius, height)) {
				double[] p = o.pushOut(fx, fy, fz, radius, height);
				pushX += p[0];
				pushZ += p[1];
			} else if (Math.abs(o.x - fx) < box.getXsize() * 0.5 + o.halfWidth + o.halfLength
					&& Math.abs(o.z - fz) < box.getZsize() * 0.5 + o.halfWidth + o.halfLength) {
				o.triangles(tris);
			}
		}
		Vec3 wanted = new Vec3(move.x + pushX, move.y, move.z + pushZ);
		if (tris.isEmpty()) {
			return pushX == 0 && pushZ == 0 ? move : Entity.collideBoundingBox(player, wanted, body, player.level(), List.of());
		}
		double[] r = TriCollider.resolve(tris, fx, fy, fz, radius, height, player.maxUpStep(), player.onGround(),
				wanted.x, wanted.y, wanted.z);
		if (r[0] == move.x && r[1] == move.y && r[2] == move.z) {
			return move;
		}
		// Snapping down a slope or pushing out of a wall can move the player into a Minecraft block; collide again.
		return Entity.collideBoundingBox(player, new Vec3(r[0], r[1], r[2]), body, player.level(), List.of());
	}
}
