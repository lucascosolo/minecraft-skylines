/*
 * Ported from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
 * See minecraft/THIRD-PARTY-NOTICES.md. Adapted for Minecraft Skylines.
 */
package dev.mcskylines.collision;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.HitResult;
import net.minecraft.world.phys.Vec3;

/** Makes the crosshair pick hit the city's collision triangles; vanilla still clips real blocks and the nearer hit wins. */
public final class SkyClip {
	private static final List<SkyTri> SCRATCH = new ArrayList<>();
	/** COLLISION_REGION triangle flag bit 8: a boundary wall at the edge of the owned land. */
	public static final int BOUNDARY = 1 << 8;

	private SkyClip() {
	}

	/** Client thread only. */
	public static BlockHitResult pick(BlockGetter level, Vec3 from, Vec3 to, BlockHitResult vanilla) {
		// The invisible fill inside buildings (shadow barrier blocks) is never targeted; the building's own surface is.
		if (vanilla.getType() != HitResult.Type.MISS && level.getBlockState(vanilla.getBlockPos()).is(Blocks.BARRIER)) {
			vanilla = BlockHitResult.miss(to, vanilla.getDirection(), BlockPos.containing(to));
		}
		SCRATCH.clear();
		CollisionStore.INSTANCE.trianglesNear(Math.min(from.x, to.x) - 0.01, Math.min(from.y, to.y) - 0.01, Math.min(from.z, to.z) - 0.01,
			Math.max(from.x, to.x) + 0.01, Math.max(from.y, to.y) + 0.01, Math.max(from.z, to.z) + 0.01, SCRATCH);
		SCRATCH.removeIf(t -> (t.flags & BOUNDARY) != 0); // invisible city-edge walls are solid but not targetable
		SkyRay.Hit hit = SCRATCH.isEmpty() ? null : SkyRay.cast(SCRATCH, from.x, from.y, from.z, to.x, to.y, to.z);
		SCRATCH.clear();
		if (hit == null) {
			return vanilla;
		}
		if (vanilla.getType() != HitResult.Type.MISS && from.distanceToSqr(vanilla.getLocation()) <= from.distanceToSqr(hit.x(), hit.y(), hit.z())) {
			return vanilla;
		}
		double[] loc = SkyRay.faceLocation(hit);
		int[] place = SkyRay.placementCell(hit);
		Direction face = Direction.values()[SkyRay.dominantFace(hit.nx(), hit.ny(), hit.nz())];
		// A real block at the surface wins over the bare surface: first a plant in the cell above it (shadow grass), then
		// the block just behind it (the shadow ground), so breaking works as vanilla mining.
		BlockPos above = new BlockPos(place[0], place[1], place[2]);
		Vec3 dir = to.subtract(from).normalize();
		BlockPos behind = BlockPos.containing(hit.x() + dir.x * 0.05, hit.y() + dir.y * 0.05, hit.z() + dir.z * 0.05);
		Vec3 at = new Vec3(loc[0], loc[1], loc[2]);
		if (targetable(level, above)) {
			return new BlockHitResult(at, face, above, false);
		}
		if (targetable(level, behind)) {
			return new BlockHitResult(at, face, behind, false);
		}
		return new HostHitResult(new Vec3(loc[0], loc[1], loc[2]), face, new BlockPos(place[0], place[1], place[2]),
			new BlockPos(place[0], place[1], place[2]));
	}

	private static boolean targetable(BlockGetter level, BlockPos pos) {
		BlockState s = level.getBlockState(pos);
		return !s.isAir() && !s.is(Blocks.BARRIER) && !s.getShape(level, pos).isEmpty();
	}

	/**
	 * A hit on city geometry. {@link #getBlockPos()} is the empty cell a placed block fills (vanilla places into a
	 * clicked replaceable cell); {@link #outlinePos} is the cell that is outlined: the same cell, so the player sees where
	 * the block will go, sunk into the surface or not (the cell behind the surface would mostly be buried).
	 */
	public static final class HostHitResult extends BlockHitResult {
		public final BlockPos outlinePos;

		HostHitResult(Vec3 location, Direction face, BlockPos placePos, BlockPos outlinePos) {
			super(location, face, placePos, false);
			this.outlinePos = outlinePos;
		}
	}
}
