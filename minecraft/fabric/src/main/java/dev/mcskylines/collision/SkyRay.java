/*
 * Ported from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
 * See minecraft/THIRD-PARTY-NOTICES.md. Adapted for Minecraft Skylines.
 */
package dev.mcskylines.collision;

import java.util.List;

/** Ray casts against the city's exact collision triangles (pure math, no Minecraft state). */
public final class SkyRay {
	/** Unit offsets of Minecraft's Direction values in ordinal order: DOWN, UP, NORTH, SOUTH, WEST, EAST. */
	private static final int[][] FACE = { { 0, -1, 0 }, { 0, 1, 0 }, { 0, 0, -1 }, { 0, 0, 1 }, { -1, 0, 0 }, { 1, 0, 0 } };

	private SkyRay() {
	}

	/** Nearest hit along a segment: {@code t} in [0, 1], the surface normal facing the ray origin, and the triangle. */
	public record Hit(double t, double x, double y, double z, double nx, double ny, double nz, SkyTri tri) {
	}

	/** First triangle hit on the segment from (fx, fy, fz) to (tx, ty, tz), or null. */
	public static Hit cast(List<SkyTri> tris, double fx, double fy, double fz, double tx, double ty, double tz) {
		double dx = tx - fx, dy = ty - fy, dz = tz - fz;
		double best = Double.POSITIVE_INFINITY;
		SkyTri hitTri = null;
		for (SkyTri tri : tris) {
			double t = intersect(tri, fx, fy, fz, dx, dy, dz);
			if (t >= 0.0 && t <= 1.0 && t < best) {
				best = t;
				hitTri = tri;
			}
		}
		if (hitTri == null) {
			return null;
		}
		double nx = hitTri.nx, ny = hitTri.ny, nz = hitTri.nz;
		if (nx * dx + ny * dy + nz * dz > 0.0) {
			nx = -nx;
			ny = -ny;
			nz = -nz;
		}
		return new Hit(best, fx + dx * best, fy + dy * best, fz + dz * best, nx, ny, nz, hitTri);
	}

	/** Moller-Trumbore, two-sided. Returns the segment parameter or -1. */
	static double intersect(SkyTri tri, double ox, double oy, double oz, double dx, double dy, double dz) {
		double e1x = tri.bx - tri.ax, e1y = tri.by - tri.ay, e1z = tri.bz - tri.az;
		double e2x = tri.cx - tri.ax, e2y = tri.cy - tri.ay, e2z = tri.cz - tri.az;
		double px = dy * e2z - dz * e2y, py = dz * e2x - dx * e2z, pz = dx * e2y - dy * e2x;
		double det = e1x * px + e1y * py + e1z * pz;
		if (Math.abs(det) < 1e-12) {
			return -1.0;
		}
		double inv = 1.0 / det;
		double sx = ox - tri.ax, sy = oy - tri.ay, sz = oz - tri.az;
		double u = (sx * px + sy * py + sz * pz) * inv;
		if (u < -1e-9 || u > 1.0 + 1e-9) {
			return -1.0;
		}
		double qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x;
		double v = (dx * qx + dy * qy + dz * qz) * inv;
		if (v < -1e-9 || u + v > 1.0 + 1e-9) {
			return -1.0;
		}
		return (e2x * qx + e2y * qy + e2z * qz) * inv;
	}

	/** The cell just behind the surface: the "virtual block" the crosshair targets, outlined like a vanilla block. */
	public static int[] surfaceCell(Hit hit) {
		double in = 0.01;
		return new int[] {
			(int) Math.floor(hit.x - hit.nx * in), (int) Math.floor(hit.y - hit.ny * in), (int) Math.floor(hit.z - hit.nz * in)
		};
	}

	/** Where a block placed against the surface goes: the neighbour of {@link #surfaceCell} across the dominant face. */
	public static int[] placementCell(Hit hit) {
		int[] cell = surfaceCell(hit);
		int[] f = FACE[dominantFace(hit.nx, hit.ny, hit.nz)];
		return new int[] { cell[0] + f[0], cell[1] + f[1], cell[2] + f[2] };
	}

	/**
	 * The hit point moved onto the face shared by {@link #surfaceCell} and {@link #placementCell} along the
	 * dominant axis, so the server's use-on-block check (hit within one block of the clicked cell's centre) holds.
	 */
	public static double[] faceLocation(Hit hit) {
		int face = dominantFace(hit.nx, hit.ny, hit.nz);
		int axis = face < 2 ? 1 : face < 4 ? 2 : 0;
		double[] loc = { hit.x, hit.y, hit.z };
		int cell = surfaceCell(hit)[axis];
		loc[axis] = face % 2 == 1 ? cell + 1 : cell;
		return loc;
	}

	/** Direction ordinal (DOWN, UP, NORTH, SOUTH, WEST, EAST) of the normal's largest component; ties prefer Y, then X. */
	public static int dominantFace(double nx, double ny, double nz) {
		double ax = Math.abs(nx), ay = Math.abs(ny), az = Math.abs(nz);
		if (ay >= ax && ay >= az) {
			return ny >= 0 ? 1 : 0;
		}
		if (ax >= az) {
			return nx >= 0 ? 5 : 4;
		}
		return nz >= 0 ? 3 : 2;
	}
}
