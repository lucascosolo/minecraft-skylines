package dev.mcskylines.collision;

import dev.mcskylines.protocol.DynamicObstacles;
import java.util.List;

/**
 * A moving vehicle or citizen as an upright box in Minecraft space: centre, yaw of the length axis
 * {@code (-sin yaw, 0, cos yaw)}, width axis {@code (cos yaw, 0, sin yaw)}, half extents and velocity (m/s).
 */
public final class ObstacleBox {
	/** Below this horizontal speed (m/s) a box pushes the player out the shortest way, whatever its motion. */
	public static final double MIN_PUSH_SPEED = 0.1;
	/** How far past the face a pushed player ends up. */
	public static final double MARGIN = 0.01;

	public final int kind, id;
	public final double x, y, z, yaw, halfWidth, halfHeight, halfLength, vx, vy, vz;
	private final double wx, wz, lx, lz;

	public ObstacleBox(int kind, int id, double x, double y, double z, double yaw, double halfWidth, double halfHeight,
			double halfLength, double vx, double vy, double vz) {
		this.kind = kind;
		this.id = id;
		this.x = x;
		this.y = y;
		this.z = z;
		this.yaw = yaw;
		this.halfWidth = halfWidth;
		this.halfHeight = halfHeight;
		this.halfLength = halfLength;
		this.vx = vx;
		this.vy = vy;
		this.vz = vz;
		double r = Math.toRadians(yaw);
		this.wx = Math.cos(r);
		this.wz = Math.sin(r);
		this.lx = -Math.sin(r);
		this.lz = Math.cos(r);
	}

	public static ObstacleBox of(DynamicObstacles.Obstacle o) {
		return new ObstacleBox(o.kind(), o.id(), o.x(), o.y(), o.z(), o.yaw(), o.halfWidth(), o.halfHeight(), o.halfLength(),
				o.vx(), o.vy(), o.vz());
	}

	/** This box moved by its velocity for {@code seconds}. */
	public ObstacleBox advanced(double seconds) {
		return new ObstacleBox(kind, id, x + vx * seconds, y + vy * seconds, z + vz * seconds, yaw, halfWidth, halfHeight,
				halfLength, vx, vy, vz);
	}

	/** Appends the box's 12 triangles, each wound so {@code (b-a)x(c-a)} points out of the box. */
	public void triangles(List<SkyTri> out) {
		double[][] axes = {{wx, 0, wz}, {0, 1, 0}, {lx, 0, lz}};
		double[] half = {halfWidth, halfHeight, halfLength};
		for (int k = 0; k < 3; k++) {
			int i = (k + 1) % 3, j = (k + 2) % 3;
			for (int s = -1; s <= 1; s += 2) {
				float[] q = new float[12];
				double[][] signs = {{1, 1}, {-1, 1}, {-1, -1}, {1, -1}};
				for (int c = 0; c < 4; c++) {
					for (int d = 0; d < 3; d++) {
						double centre = d == 0 ? x : d == 1 ? y : z;
						q[3 * c + d] = (float) (centre + s * half[k] * axes[k][d] + signs[c][0] * half[i] * axes[i][d]
								+ signs[c][1] * half[j] * axes[j][d]);
					}
				}
				// The corner cycle's handedness depends on the face; wind it outward by its normal.
				double ux = q[3] - q[0], uy = q[4] - q[1], uz = q[5] - q[2], tx = q[6] - q[0], ty = q[7] - q[1], tz = q[8] - q[2];
				double nx = uy * tz - uz * ty, ny = uz * tx - ux * tz, nz = ux * ty - uy * tx;
				boolean flip = (nx * axes[k][0] + ny * axes[k][1] + nz * axes[k][2]) * s < 0;
				int[] order = flip ? new int[] {0, 3, 2, 1} : new int[] {0, 1, 2, 3};
				out.add(new SkyTri(corners(q, order[0], order[1], order[2]), 0, 0));
				out.add(new SkyTri(corners(q, order[0], order[2], order[3]), 0, 0));
			}
		}
	}

	private static float[] corners(float[] q, int a, int b, int c) {
		return new float[] {q[3 * a], q[3 * a + 1], q[3 * a + 2], q[3 * b], q[3 * b + 1], q[3 * b + 2], q[3 * c], q[3 * c + 1], q[3 * c + 2]};
	}

	/** True when an upright body (feet centre, horizontal half-size {@code radius}, {@code height}) intersects the box. */
	public boolean overlaps(double fx, double fy, double fz, double radius, double height) {
		double dx = fx - x, dz = fz - z;
		return Math.abs(dx * wx + dz * wz) < halfWidth + radius && Math.abs(dx * lx + dz * lz) < halfLength + radius
				&& fy < y + halfHeight && fy + height > y - halfHeight;
	}

	/**
	 * Horizontal displacement {dx, dz} that moves an overlapping body out of the box through its nearest side face, never
	 * through a face that looks backwards against the box's horizontal motion. {0, 0} when not overlapping.
	 */
	public double[] pushOut(double fx, double fy, double fz, double radius, double height) {
		if (!overlaps(fx, fy, fz, radius, height)) {
			return new double[] {0, 0};
		}
		double dx = fx - x, dz = fz - z;
		double w = dx * wx + dz * wz, l = dx * lx + dz * lz;
		double[][] faces = {{wx, wz, halfWidth + radius - w}, {-wx, -wz, halfWidth + radius + w},
				{lx, lz, halfLength + radius - l}, {-lx, -lz, halfLength + radius + l}};
		double speed = Math.hypot(vx, vz);
		double[] best = null;
		for (double[] f : faces) {
			if (speed >= MIN_PUSH_SPEED && (f[0] * vx + f[1] * vz) / speed < -0.1) {
				continue;
			}
			if (best == null || f[2] < best[2]) {
				best = f;
			}
		}
		double d = best[2] + MARGIN;
		return new double[] {best[0] * d, best[1] * d};
	}
}
