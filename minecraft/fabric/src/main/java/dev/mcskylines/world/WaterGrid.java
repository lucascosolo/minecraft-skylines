/*
 * Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
 * See minecraft/THIRD-PARTY-NOTICES.md. Adapted for Minecraft Skylines (adds the host's ground under the water).
 */
package dev.mcskylines.world;

import dev.mcskylines.protocol.WaterSurface;

/** One WATER_SURFACE grid: which block cells around the player hold the city's water, and how full. Immutable. */
public final class WaterGrid {
	private static final double MIN_DEPTH = 0.02;

	private final int originX, originZ, size;
	private final float[] surface, bottom;

	public WaterGrid(WaterSurface s) {
		this.originX = s.originX();
		this.originZ = s.originZ();
		this.size = s.size();
		this.surface = s.surface().clone();
		this.bottom = s.bottom().clone();
	}

	private int index(int x, int z) {
		int dx = x - originX, dz = z - originZ;
		return dx < 0 || dz < 0 || dx >= size || dz >= size ? -1 : dz * size + dx;
	}

	/** Minecraft y of the water surface over this column, or NaN outside the grid or where there is no water. */
	public double surfaceAt(int x, int z) {
		int i = index(x, z);
		return i < 0 || !(surface[i] > bottom[i]) ? Double.NaN : surface[i];
	}

	/** Minecraft y of the host's ground in this column, or NaN outside the grid. */
	public double bottomAt(int x, int z) {
		int i = index(x, z);
		return i < 0 ? Double.NaN : bottom[i];
	}

	/** How much of this cell (0..1) is under the city's water; 0 above the surface and wholly under the ground. */
	public float depthIn(int x, int y, int z) {
		double s = surfaceAt(x, z);
		if (Double.isNaN(s) || y + 1 <= bottomAt(x, z)) {
			return 0.0F;
		}
		double h = s - y;
		return h < MIN_DEPTH ? 0.0F : (float) Math.min(1.0, h);
	}

	/** True if any cell of the inclusive box holds the city's water. */
	public boolean anyIn(int x0, int y0, int z0, int x1, int y1, int z1) {
		for (int x = x0; x <= x1; x++) {
			for (int z = z0; z <= z1; z++) {
				double s = surfaceAt(x, z);
				if (Double.isNaN(s)) {
					continue;
				}
				for (int y = y0; y <= y1; y++) {
					if (depthIn(x, y, z) > 0.0F) {
						return true;
					}
				}
			}
		}
		return false;
	}
}
