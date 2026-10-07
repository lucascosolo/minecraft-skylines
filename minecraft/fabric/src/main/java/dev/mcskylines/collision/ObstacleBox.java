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
	/** Turn rate, yaw degrees per second. */
	public final double yawRate;
	private final byte[] profile;
	private final double wx, wz, lx, lz;
	private double[] tops;

	public ObstacleBox(int kind, int id, double x, double y, double z, double yaw, double halfWidth, double halfHeight,
			double halfLength, double vx, double vy, double vz) {
		this(kind, id, x, y, z, yaw, halfWidth, halfHeight, halfLength, vx, vy, vz, 0, new byte[0]);
	}

	/** {@code profile}: SHAPED_OBSTACLES slice tops in 1/255 of the height from the -length end; empty for none. */
	public ObstacleBox(int kind, int id, double x, double y, double z, double yaw, double halfWidth, double halfHeight,
			double halfLength, double vx, double vy, double vz, double yawRate, byte[] profile) {
		this.yawRate = yawRate;
		this.profile = profile == null ? new byte[0] : profile;
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
				o.vx(), o.vy(), o.vz(), o.yawRate(), o.profile());
	}

	/** This box moved by its velocity and turned by its yaw rate for {@code seconds}. */
	public ObstacleBox advanced(double seconds) {
		return new ObstacleBox(kind, id, x + vx * seconds, y + vy * seconds, z + vz * seconds, yaw + yawRate * seconds,
				halfWidth, halfHeight, halfLength, vx, vy, vz, yawRate, profile);
	}

	/** Largest rise (m) between neighbouring slices of a car, below Minecraft's 0.6 step height, so it can be walked over. */
	public static final double STEP = 0.5;
	private static final double SLICE = 0.25;

	private boolean carSized() {
		return (kind == 1 || kind == 3) && 2 * halfHeight <= CAR_MAX_HEIGHT;
	}

	/**
	 * World-y top of each equal slice along the length axis, from the -halfLength end: the host's profile, else for a car
	 * a low hood and boot either side of a full-height cabin (owner, 2026-10-06: "cars with low hoods that look like I ought
	 * to be able to jump on them"), else one full slice. A car's tops are then lowered to stairs no higher than
	 * {@link #STEP} from the ground and from each other (owner, same day: "make the cars have stairs where the windshield
	 * and back windows would be ... so I can walk seamlessly up and over the tops").
	 */
	public double[] sliceTops() {
		if (tops != null) {
			return tops;
		}
		double full = 2 * halfHeight, bottom = y - halfHeight;
		double[] h;
		if (profile.length > 0) {
			h = new double[profile.length];
			for (int i = 0; i < h.length; i++) {
				h[i] = (profile[i] & 0xFF) * full / 255.0;
			}
		} else if (carSized() && 2 * halfLength >= CAR_MIN_LENGTH) {
			h = new double[Math.max(1, (int) Math.ceil(2 * halfLength / SLICE - 1e-4))];
			double slice = 2 * halfLength / h.length, end = CAR_END_SHARE * halfLength;
			for (int i = 0; i < h.length; i++) {
				double c = (i + 0.5) * slice;
				h[i] = c <= end || 2 * halfLength - c <= end ? CAR_END_HEIGHT * full : full;
			}
		} else {
			h = new double[] {full};
		}
		if (carSized() && (profile.length > 0 || h.length > 1)) {
			h = stairs(h);
		}
		for (int i = 0; i < h.length; i++) {
			h[i] += bottom;
		}
		tops = h;
		return h;
	}

	// The highest profile at or below h whose neighbours (and the ground beyond both ends) differ by at most STEP.
	private static double[] stairs(double[] h) {
		int n = h.length;
		double[] s = new double[n];
		for (int i = 0; i < n; i++) {
			double v = Math.min(h[i], STEP * Math.min(i + 1, n - i));
			for (int j = 0; j < n; j++) {
				v = Math.min(v, h[j] + STEP * Math.abs(i - j));
			}
			s[i] = v;
		}
		return s;
	}

	/** Highest slice top under a body of horizontal half-size {@code radius} at feet (fx, fz); NaN when there is none. */
	public double supportTop(double fx, double fz, double radius) {
		double dx = fx - x, dz = fz - z, w = dx * wx + dz * wz, l = dx * lx + dz * lz;
		if (Math.abs(w) >= halfWidth + radius) {
			return Double.NaN;
		}
		double[] t = sliceTops();
		double slice = 2 * halfLength / t.length, bottom = y - halfHeight, best = Double.NaN;
		for (int i = 0; i < t.length; i++) {
			double lo = -halfLength + i * slice;
			if (l + radius > lo && l - radius < lo + slice && t[i] > bottom + 1e-6 && !(t[i] <= best)) {
				best = t[i];
			}
		}
		return best;
	}

	/** True when the body overlaps a slice whose top is more than {@code step} above its feet: a wall, not a stair. */
	public boolean blocks(double fx, double fy, double fz, double radius, double height, double step) {
		double dx = fx - x, dz = fz - z, w = dx * wx + dz * wz, l = dx * lx + dz * lz, bottom = y - halfHeight;
		if (Math.abs(w) >= halfWidth + radius || fy + height <= bottom) {
			return false;
		}
		double[] t = sliceTops();
		double slice = 2 * halfLength / t.length;
		for (int i = 0; i < t.length; i++) {
			double lo = -halfLength + i * slice;
			if (l + radius > lo && l - radius < lo + slice && fy < t[i] && t[i] > fy + step) {
				return true;
			}
		}
		return false;
	}

	/**
	 * How a body standing at feet (fx, fy, fz) on this obstacle moves with it in {@code seconds}: {dx, dy, dz, dyaw}. The
	 * feet keep their place in the box's frame while the box moves by its velocity and turns about its centre.
	 */
	public double[] carry(double fx, double fy, double fz, double seconds) {
		double dyaw = yawRate * seconds;
		double dx = fx - x, dz = fz - z, a = dx * lx + dz * lz, b = dx * wx + dz * wz;
		double r = Math.toRadians(yaw + dyaw), nwx = Math.cos(r), nwz = Math.sin(r), nlx = -Math.sin(r), nlz = Math.cos(r);
		return new double[] {a * nlx + b * nwx - dx + vx * seconds, vy * seconds, a * nlz + b * nwz - dz + vz * seconds, dyaw};
	}

	/** Cars at most this tall (m) and at least this long get a car profile instead of one box. */
	static final double CAR_MAX_HEIGHT = 2.2, CAR_MIN_LENGTH = 3.4;
	/** Car profile: each end (hood, boot) takes this share of the length at this share of the height. */
	static final double CAR_END_SHARE = 0.3, CAR_END_HEIGHT = 0.55;

	/**
	 * Appends the obstacle's triangles, each wound so {@code (b-a)x(c-a)} points out of it: a full-width box from the
	 * bottom to each run of equal {@link #sliceTops()}, nothing where a slice is empty.
	 */
	public void triangles(List<SkyTri> out) {
		double[] t = sliceTops();
		double slice = 2 * halfLength / t.length, bottom = y - halfHeight;
		for (int i = 0; i < t.length;) {
			int j = i + 1;
			while (j < t.length && Math.abs(t[j] - t[i]) < 1e-9) {
				j++;
			}
			if (t[i] > bottom + 1e-6) {
				double hh = (t[i] - bottom) * 0.5, mid = -halfLength + (i + j) * 0.5 * slice;
				box(x + lx * mid, bottom + hh, z + lz * mid, halfWidth, hh, (j - i) * 0.5 * slice, out);
			}
			i = j;
		}
	}

	private void box(double cx, double cy, double cz, double hw, double hh, double hl, List<SkyTri> out) {
		double[][] axes = {{wx, 0, wz}, {0, 1, 0}, {lx, 0, lz}};
		double[] half = {hw, hh, hl};
		for (int k = 0; k < 3; k++) {
			int i = (k + 1) % 3, j = (k + 2) % 3;
			for (int s = -1; s <= 1; s += 2) {
				float[] q = new float[12];
				double[][] signs = {{1, 1}, {-1, 1}, {-1, -1}, {1, -1}};
				for (int c = 0; c < 4; c++) {
					for (int d = 0; d < 3; d++) {
						double centre = d == 0 ? cx : d == 1 ? cy : cz;
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
