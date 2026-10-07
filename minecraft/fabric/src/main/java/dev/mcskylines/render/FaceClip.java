package dev.mcskylines.render;

/**
 * Clips a shadow block's side face toward a dug cell at CS1's ground surface: the top edge comes down to the surface at
 * each corner and the texture is cropped (UVs moved with it), so the pit wall meets the host's skirt instead of
 * sticking up into the air (owner, 2026-10-06).
 */
public final class FaceClip {
	private static final float EPS = 1e-4f;

	private FaceClip() {
	}

	/** Clips one quad in place; false when nothing of it is left. {@code surface[i]} is the ground at vertex i (NaN: keep). */
	public static boolean clipTop(float[] x, float[] y, float[] z, float[] u, float[] v, float[] surface) {
		float bottom = Math.min(Math.min(y[0], y[1]), Math.min(y[2], y[3]));
		float top = Math.max(Math.max(y[0], y[1]), Math.max(y[2], y[3]));
		float span = top - bottom;
		if (span < 1e-6f) {
			return true;
		}
		boolean left = false;
		for (int i = 0; i < 4; i++) {
			if (Math.abs(y[i] - top) >= EPS) {
				continue;
			}
			if (Float.isNaN(surface[i])) {
				left = true;
				continue;
			}
			int j = partner(x, y, z, i, bottom);
			float t = Math.clamp((surface[i] - bottom) / span, 0f, 1f);
			left |= t > 0;
			if (j < 0) {
				continue;
			}
			y[i] = bottom + t * span;
			u[i] = u[j] + t * (u[i] - u[j]);
			v[i] = v[j] + t * (v[i] - v[j]);
		}
		return left;
	}

	private static int partner(float[] x, float[] y, float[] z, int i, float bottom) {
		for (int j = 0; j < 4; j++) {
			if (Math.abs(y[j] - bottom) < EPS && Math.abs(x[j] - x[i]) < EPS && Math.abs(z[j] - z[i]) < EPS) {
				return j;
			}
		}
		return -1;
	}
}
