/*
 * Ported from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
 * See minecraft/THIRD-PARTY-NOTICES.md. Adapted for Minecraft Skylines.
 */
package dev.mcskylines.collision;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import dev.mcskylines.shadow.ShadowCells;
import net.minecraft.tags.BlockTags;
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
		// A real block at the surface wins over the bare surface: first a plant on it (shadow grass, in the placement cell
		// or on top of the ground block behind the surface, which differ when the surface sits near a block's middle),
		// then the block just behind it (the shadow ground), so breaking works as vanilla mining.
		BlockPos above = new BlockPos(place[0], place[1], place[2]);
		Vec3 dir = to.subtract(from).normalize();
		BlockPos behind = BlockPos.containing(hit.x() + dir.x * 0.05, hit.y() + dir.y * 0.05, hit.z() + dir.z * 0.05);
		Vec3 at = new Vec3(loc[0], loc[1], loc[2]);
		// A CS1 tree's trunk (a 0.6 m post centred on the tree) can straddle a cell edge, so the cell behind its surface
		// may not hold the trunk's log: target the nearest city log beside it, so punching the trunk always fells it.
		BlockPos log = trunkLog(level, behind, hit.x(), hit.z());
		if (log != null) {
			return new BlockHitResult(at, face, log, false);
		}
		if (targetable(level, above)) {
			return new BlockHitResult(at, face, above, false);
		}
		BlockPos onGround = behind.above();
		if (!onGround.equals(above) && targetable(level, onGround) && level.getBlockState(onGround).getCollisionShape(level, onGround).isEmpty()) {
			return new BlockHitResult(at, face, onGround, false);
		}
		if (targetable(level, behind)) {
			return new BlockHitResult(at, face, behind, false);
		}
		return new HostHitResult(new Vec3(loc[0], loc[1], loc[2]), face, new BlockPos(place[0], place[1], place[2]),
			new BlockPos(place[0], place[1], place[2]));
	}

	private static BlockPos trunkLog(BlockGetter level, BlockPos behind, double hx, double hz) {
		BlockPos best = null;
		double bestD = 1.5;
		for (int dx = -1; dx <= 1; dx++) {
			for (int dz = -1; dz <= 1; dz++) {
				BlockPos p = behind.offset(dx, 0, dz);
				if (!level.getBlockState(p).is(BlockTags.LOGS) || !ShadowCells.INSTANCE.contains(p.getX(), p.getY(), p.getZ())) {
					continue;
				}
				double cx = p.getX() + 0.5 - hx, cz = p.getZ() + 0.5 - hz, d = cx * cx + cz * cz;
				if (d < bestD) {
					best = p;
					bestD = d;
				}
			}
		}
		return best;
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
