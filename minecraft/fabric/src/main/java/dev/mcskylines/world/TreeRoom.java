package dev.mcskylines.world;

import dev.mcskylines.collision.SkyTri;
import java.util.List;

/** Whether the city has room for a CS1 tree: no blocking triangle inside its trunk or crown box. */
public final class TreeRoom {
	/** COLLISION_REGION flag bits 1..8 (not terrain, not the dug surface). */
	public static final int BLOCKING = 0x1FE;

	private TreeRoom() {
	}

	public static double[] queryBox(double x, double y, double z, double height, double radius) {
		double r = Math.max(0.5, radius / 2);
		return new double[] {x - r, y + 0.25, z - r, x + r, y + height, z + r};
	}

	public static boolean fits(List<SkyTri> tris, double x, double y, double z, double height, double radius) {
		double[] trunk = {x - 0.5, y + 0.25, z - 0.5, x + 0.5, y + height, z + 0.5};
		double r = radius / 2;
		double[] crown = {x - r, y + 0.4 * height, z - r, x + r, y + height, z + r};
		for (SkyTri t : tris) {
			if ((t.flags & BLOCKING) != 0 && (hits(t, trunk) || hits(t, crown))) {
				return false;
			}
		}
		return true;
	}

	/** Exact triangle / box overlap (separating axis test: 9 edge cross axes, the box faces, the triangle plane). */
	private static boolean hits(SkyTri t, double[] b) {
		if (t.maxX < b[0] || t.minX > b[3] || t.maxY < b[1] || t.minY > b[4] || t.maxZ < b[2] || t.minZ > b[5]) {
			return false;
		}
		double cx = (b[0] + b[3]) / 2, cy = (b[1] + b[4]) / 2, cz = (b[2] + b[5]) / 2;
		double hx = (b[3] - b[0]) / 2, hy = (b[4] - b[1]) / 2, hz = (b[5] - b[2]) / 2;
		double[][] v = {{t.ax - cx, t.ay - cy, t.az - cz}, {t.bx - cx, t.by - cy, t.bz - cz}, {t.cx - cx, t.cy - cy, t.cz - cz}};
		double[][] e = {sub(v[1], v[0]), sub(v[2], v[1]), sub(v[0], v[2])};
		double[][] axes = {{1, 0, 0}, {0, 1, 0}, {0, 0, 1}};
		for (double[] ei : e) {
			for (double[] a : axes) {
				if (separated(cross(a, ei), v, hx, hy, hz)) {
					return false;
				}
			}
		}
		return !separated(cross(e[0], e[1]), v, hx, hy, hz);
	}

	private static boolean separated(double[] ax, double[][] v, double hx, double hy, double hz) {
		if (ax[0] * ax[0] + ax[1] * ax[1] + ax[2] * ax[2] < 1e-18) {
			return false;
		}
		double p0 = dot(ax, v[0]), p1 = dot(ax, v[1]), p2 = dot(ax, v[2]);
		double r = hx * Math.abs(ax[0]) + hy * Math.abs(ax[1]) + hz * Math.abs(ax[2]);
		return Math.min(p0, Math.min(p1, p2)) > r || Math.max(p0, Math.max(p1, p2)) < -r;
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
