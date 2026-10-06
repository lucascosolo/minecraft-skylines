package dev.mcskylines.collision;

import java.util.ArrayList;
import java.util.List;

/**
 * How far the host's collision triangles fill a block cell, for Minecraft fluids that must rest on and stop against
 * city geometry (see {@code FlowingFluidMixin}). The counterpart of SkyCraft's voxel fill, computed from triangles.
 */
public final class FluidGround {
	/** No host geometry in the cell. */
	public static final float NONE = -1.0F;
	private static final double STEEP_NY = 0.3;
	private static final double TOUCH = 1e-6;
	private static final double[] SAMPLES = {0.05, 0.5, 0.95};
	private static final ThreadLocal<List<SkyTri>> SCRATCH = ThreadLocal.withInitial(ArrayList::new);

	private FluidGround() {
	}

	/** Top of the host geometry in cell (x, y, z), 0..1 of the cell, or {@link #NONE}. A wall crossing the cell fills it. */
	public static float groundTop(List<SkyTri> tris, int x, int y, int z) {
		float top = NONE;
		for (SkyTri t : tris) {
			if (!crosses(t, x, y, z)) {
				continue;
			}
			top = Math.max(top, Math.abs(t.ny) < STEEP_NY ? 1.0F : flatTop(t, x, y, z));
			if (top >= 1.0F) {
				break;
			}
		}
		return top;
	}

	public static float groundTop(CollisionStore store, int x, int y, int z) {
		List<SkyTri> near = SCRATCH.get();
		near.clear();
		store.trianglesNear(x, y, z, x + 1, y + 1, z + 1, near);
		float top = groundTop(near, x, y, z);
		near.clear();
		return top;
	}

	/** True when the host has sent the collision region holding column (x, z). */
	public static boolean isKnown(CollisionStore store, int x, int z) {
		return store.isLoaded(Math.floorDiv(x, 16), Math.floorDiv(z, 16));
	}

	private static float flatTop(SkyTri t, int x, int y, int z) {
		float top = NONE;
		for (double sx : SAMPLES) {
			for (double sz : SAMPLES) {
				double h = t.heightAt(x + sx, z + sz);
				if (!Double.isNaN(h) && h >= y) {
					top = Math.max(top, clamp01(h - y));
				}
			}
		}
		return top >= 0.0F ? top : clamp01(Math.min(t.maxY, y + 1) - y);
	}

	private static float clamp01(double v) {
		return (float) Math.max(0.0, Math.min(1.0, v));
	}

	/** Separating-axis test of the triangle against the cell shrunk by {@link #TOUCH} (Akenine-Moller). */
	static boolean crosses(SkyTri t, int x, int y, int z) {
		double h = 0.5 - TOUCH;
		double cx = x + 0.5, cy = y + 0.5, cz = z + 0.5;
		double[] v0 = {t.ax - cx, t.ay - cy, t.az - cz};
		double[] v1 = {t.bx - cx, t.by - cy, t.bz - cz};
		double[] v2 = {t.cx - cx, t.cy - cy, t.cz - cz};
		for (int a = 0; a < 3; a++) {
			double mn = Math.min(v0[a], Math.min(v1[a], v2[a])), mx = Math.max(v0[a], Math.max(v1[a], v2[a]));
			if (mn > h || mx < -h) {
				return false;
			}
		}
		double[][] e = {sub(v1, v0), sub(v2, v1), sub(v0, v2)};
		for (double[] edge : e) {
			for (int a = 0; a < 3; a++) {
				double[] axis = cross(unit(a), edge);
				if (separates(axis, v0, v1, v2, h)) {
					return false;
				}
			}
		}
		double[] n = cross(e[0], e[1]);
		return !separates(n, v0, v1, v2, h);
	}

	private static boolean separates(double[] axis, double[] v0, double[] v1, double[] v2, double h) {
		double p0 = dot(axis, v0), p1 = dot(axis, v1), p2 = dot(axis, v2);
		double r = h * (Math.abs(axis[0]) + Math.abs(axis[1]) + Math.abs(axis[2]));
		return Math.min(p0, Math.min(p1, p2)) > r || Math.max(p0, Math.max(p1, p2)) < -r;
	}

	private static double[] unit(int a) {
		double[] u = new double[3];
		u[a] = 1.0;
		return u;
	}

	private static double[] sub(double[] a, double[] b) {
		return new double[] {a[0] - b[0], a[1] - b[1], a[2] - b[2]};
	}

	private static double[] cross(double[] a, double[] b) {
		return new double[] {a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]};
	}

	private static double dot(double[] a, double[] b) {
		return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
	}
}
