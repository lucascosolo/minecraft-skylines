package dev.mcskylines.shadow;

import dev.mcskylines.collision.SkyTri;
import java.util.List;

/**
 * CS1 props and bushes (hedges, fences, benches, bins) as invisible path-finding cells: mobs collide with their boxes,
 * so the path finder must route around them too (owner, 2026-10-06: a chicken "trying to pathfind through a hedge").
 */
public final class ShadowObstacles {
	/** Lower obstacles are stepped over. */
	public static final double MIN_HEIGHT = 0.6;
	/** Up to this a mob can jump onto the obstacle: one cell; above it two, which the path finder never jumps. */
	public static final double JUMP_HEIGHT = 1.2;
	/** The column square shrunk by this on each side, so a fence on a cell edge does not block both neighbours. */
	public static final double INSET = 0.2;
	/** A box counts only with a horizontal face at or below ground + this: overhead signs and arms are not obstacles. */
	public static final double GROUNDED = 0.5;
	private static final int FLAGS = SkyTri.VEGETATION | SkyTri.PROP;
	private static final double EPS = 1e-4; // triangle coordinates are floats

	private ShadowObstacles() {
	}

	/** Top of the prop or bush boxes standing over column (x, z), or NaN when none is at least {@link #MIN_HEIGHT} tall. */
	public static double top(List<SkyTri> tris, int x, int z, double ground) {
		if (Double.isNaN(ground)) {
			return Double.NaN;
		}
		double x0 = x + INSET, x1 = x + 1 - INSET, z0 = z + INSET, z1 = z + 1 - INSET;
		double top = Double.NEGATIVE_INFINITY;
		boolean grounded = false;
		for (SkyTri t : tris) {
			if ((t.flags & FLAGS) == 0 || Math.abs(t.ny) < 0.9 || !overlaps(t, x0, z0, x1, z1)) {
				continue;
			}
			top = Math.max(top, t.maxY);
			grounded |= t.maxY <= ground + GROUNDED + EPS;
		}
		return grounded && top - ground >= MIN_HEIGHT - EPS ? top : Double.NaN;
	}

	/** Path-finding cells over the ground: 0, 1 or 2. */
	public static int cells(double ground, double top) {
		double h = top - ground;
		if (Double.isNaN(h) || h < MIN_HEIGHT - EPS) {
			return 0;
		}
		return h <= JUMP_HEIGHT + EPS ? 1 : 2;
	}

	/** Separating-axis test of the triangle's x/z footprint against the rectangle. */
	private static boolean overlaps(SkyTri t, double x0, double z0, double x1, double z1) {
		if (t.maxX < x0 || t.minX > x1 || t.maxZ < z0 || t.minZ > z1) {
			return false;
		}
		double[] px = {t.ax, t.bx, t.cx}, pz = {t.az, t.bz, t.cz};
		double[] rx = {x0, x1, x1, x0}, rz = {z0, z0, z1, z1};
		for (int i = 0; i < 3; i++) {
			int j = (i + 1) % 3, k = (i + 2) % 3;
			double ex = px[j] - px[i], ez = pz[j] - pz[i];
			double nx = -ez, nz = ex;
			double side = nx * (px[k] - px[i]) + nz * (pz[k] - pz[i]);
			if (Math.abs(side) < 1e-12) {
				return false; // degenerate in plan
			}
			boolean separated = true;
			for (int c = 0; c < 4 && separated; c++) {
				double d = nx * (rx[c] - px[i]) + nz * (rz[c] - pz[i]);
				separated = side > 0 ? d < 0 : d > 0;
			}
			if (separated) {
				return false;
			}
		}
		return true;
	}
}
