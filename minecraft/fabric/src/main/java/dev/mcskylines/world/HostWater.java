/*
 * Ported from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
 * See minecraft/THIRD-PARTY-NOTICES.md. Adapted for Minecraft Skylines (WATER_SURFACE grid instead of shared memory).
 */
package dev.mcskylines.world;

import dev.mcskylines.protocol.WaterSurface;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.level.material.FluidState;
import net.minecraft.world.level.material.Fluids;
import org.jspecify.annotations.Nullable;

/**
 * The city's lakes, rivers and sea as Minecraft water: wherever Minecraft has air below the host's water surface (and
 * above its ground), entities treat it as water, so the player swims, floats, sinks slowly and drowns there. Only
 * entity physics sees it; no blocks change. Written by the link (client thread), read by entity ticks on the client
 * and the integrated server.
 */
public final class HostWater {
	private static volatile @Nullable WaterGrid grid;

	private HostWater() {
	}

	public static void accept(WaterSurface surface) {
		grid = surface.size() == 0 ? null : new WaterGrid(surface);
	}

	public static void clear() {
		grid = null;
	}

	public static boolean active() {
		return grid != null;
	}

	public static boolean anyIn(int x0, int y0, int z0, int x1, int y1, int z1) {
		WaterGrid g = grid;
		return g != null && g.anyIn(x0, y0, z0, x1, y1, z1);
	}

	/** The city's water in an otherwise empty (air) Minecraft cell, as a Minecraft fluid; null if none. */
	public static @Nullable FluidState fluidAt(BlockGetter level, BlockPos pos) {
		WaterGrid g = grid;
		if (g == null || g.depthIn(pos.getX(), pos.getY(), pos.getZ()) <= 0.0F || !level.getBlockState(pos).isAir()) {
			return null;
		}
		return Fluids.WATER.getSource(false);
	}

	/** The exact water height in a cell only the city fills (so floating matches its surface); -1 otherwise. */
	public static float substitutedHeight(BlockGetter level, BlockPos pos) {
		WaterGrid g = grid;
		float depth = g == null ? 0.0F : g.depthIn(pos.getX(), pos.getY(), pos.getZ());
		if (depth <= 0.0F || !level.getFluidState(pos).isEmpty() || !level.getBlockState(pos).isAir()) {
			return -1.0F;
		}
		return depth;
	}
}
