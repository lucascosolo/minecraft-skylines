/*
 * Ported from SkyCraft (https://github.com/chasmlol/SkyCraft) SkyCollider, MIT License, Copyright (c) 2026 chasmlol.
 * See minecraft/THIRD-PARTY-NOTICES.md. Adapted for Minecraft Skylines.
 */
package dev.mcskylines.collision;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;

/** Feeds an entity's movement (the local player on the client; mobs and items on the server) through {@link TriCollider} against the nearby streamed triangles and moving obstacles. */
public final class PlayerCollider {
	/** One Minecraft tick: an entity moves once per tick. */
	static final double TICK_SECONDS = 0.05;
	/** Feet this close (m) to a vehicle's top ride on it. */
	static final double RIDE_TOLERANCE = 0.2;

	private PlayerCollider() {
	}

	/** True while the host has streamed any collision geometry; the mixins stay inert otherwise. */
	public static boolean active() {
		return CollisionStore.INSTANCE.regionCount() > 0;
	}

	public static Vec3 collide(Entity player, Vec3 move) {
		AABB body = player.getBoundingBox();
		double fx = (body.minX + body.maxX) * 0.5, fy = body.minY, fz = (body.minZ + body.maxZ) * 0.5;
		double radius = body.getXsize() * 0.5, height = body.getYsize();
		AABB box = body.expandTowards(move).inflate(1.0, 1.0 + player.maxUpStep(), 1.0);
		List<SkyTri> tris = new ArrayList<>();
		CollisionStore.INSTANCE.trianglesNear(box.minX, box.minY, box.minZ, box.maxX, box.maxY, box.maxZ, tris);
		// A vehicle or citizen that moved into the player pushes it out; the rest are solid like the city.
		double pushX = 0, pushZ = 0, rideTop = Double.NEGATIVE_INFINITY;
		double[] ride = null;
		for (ObstacleBox o : DynamicObstacleStore.INSTANCE.current(System.nanoTime())) {
			// Standing on a vehicle (owner, 2026-10-06: "I would like to be able to jump onto a car and ride it"): the feet
			// keep their place on it for this tick's 1/20 s as it moves and turns.
			double top = o.supportTop(fx, fz, radius);
			if (!Double.isNaN(top) && Math.abs(fy - top) <= RIDE_TOLERANCE && top > rideTop) {
				rideTop = top;
				ride = o.carry(fx, fy, fz, TICK_SECONDS);
			}
			// Pushed out only by a wall that moved into the body: the feet in the lower half of the box and a part of it
			// higher than a step. Landing on a roof or hood dips the feet a little into the box, which used to shove the
			// player off sideways (owner, 2026-10-06: "When I jump onto the car, I slide off like it's a sheer edge").
			if (o.overlaps(fx, fy, fz, radius, height) && fy < o.y && o.blocks(fx, fy, fz, radius, height, player.maxUpStep())) {
				double[] p = o.pushOut(fx, fy, fz, radius, height);
				pushX += p[0];
				pushZ += p[1];
			} else if (Math.abs(o.x - fx) < box.getXsize() * 0.5 + o.halfWidth + o.halfLength
					&& Math.abs(o.z - fz) < box.getZsize() * 0.5 + o.halfWidth + o.halfLength) {
				o.triangles(tris);
			}
		}
		if (ride != null) {
			pushX += ride[0];
			pushZ += ride[2];
			if (ride[1] > 0) {
				move = new Vec3(move.x, move.y + ride[1], move.z);
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
