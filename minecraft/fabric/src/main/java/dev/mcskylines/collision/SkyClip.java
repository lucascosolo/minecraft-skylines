/*
 * Ported from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
 * See minecraft/THIRD-PARTY-NOTICES.md. Adapted for Minecraft Skylines.
 */
package dev.mcskylines.collision;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.HitResult;
import net.minecraft.world.phys.Vec3;

/** Makes the crosshair pick hit the city's collision triangles; vanilla still clips real blocks and the nearer hit wins. */
public final class SkyClip {
	private static final List<SkyTri> SCRATCH = new ArrayList<>();

	private SkyClip() {
	}

	/** Client thread only. */
	public static BlockHitResult pick(Vec3 from, Vec3 to, BlockHitResult vanilla) {
		SCRATCH.clear();
		CollisionStore.INSTANCE.trianglesNear(Math.min(from.x, to.x) - 0.01, Math.min(from.y, to.y) - 0.01, Math.min(from.z, to.z) - 0.01,
			Math.max(from.x, to.x) + 0.01, Math.max(from.y, to.y) + 0.01, Math.max(from.z, to.z) + 0.01, SCRATCH);
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
		int[] outline = SkyRay.surfaceCell(hit);
		Direction face = Direction.values()[SkyRay.dominantFace(hit.nx(), hit.ny(), hit.nz())];
		return new HostHitResult(new Vec3(loc[0], loc[1], loc[2]), face, new BlockPos(place[0], place[1], place[2]),
			new BlockPos(outline[0], outline[1], outline[2]));
	}

	/**
	 * A hit on city geometry. {@link #getBlockPos()} is the empty cell a placed block fills (vanilla places into a
	 * clicked replaceable cell); {@link #outlinePos} is the virtual block behind the surface that is outlined.
	 */
	public static final class HostHitResult extends BlockHitResult {
		public final BlockPos outlinePos;

		HostHitResult(Vec3 location, Direction face, BlockPos placePos, BlockPos outlinePos) {
			super(location, face, placePos, false);
			this.outlinePos = outlinePos;
		}
	}
}
