package dev.mcskylines.world;

import dev.mcskylines.collision.CollisionStore;
import dev.mcskylines.collision.SkyTri;
import java.util.ArrayList;
import java.util.List;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * Keeps dropped items and experience orbs on CS1's ground. With no step height, an item resting on a slope that slides
 * uphill ends below the triangles (TriCollider only looks for ground above the feet while airborne), and from half a
 * block under the surface the player's pickup box (its own box inflated by 0.5 vertically) no longer reaches it.
 */
public final class DropGround {
	private static final Logger LOG = LoggerFactory.getLogger("MinecraftSkylines");
	/** How far below the surface a drop is lifted back; deeper ones are left alone (a cave, a dug pit). */
	public static final double MAX_SINK = 1.0;
	private static final long LOG_INTERVAL_NS = 1_000_000_000L;
	private static long lastLog;

	private DropGround() {
	}

	/** Highest walkable solid surface at (x, z) between y - 0.01 and y + {@link #MAX_SINK}, or NaN. */
	public static double surfaceAt(double x, double y, double z) {
		return highest(x, z, y - 0.01, y + MAX_SINK);
	}

	/** Server side: logs (at most once a second) where a drop landed and the surface height there. */
	public static void landed(String what, int id, double x, double y, double z) {
		long now = System.nanoTime();
		if (now - lastLog < LOG_INTERVAL_NS) {
			return;
		}
		lastLog = now;
		double top = highest(x, z, -1e9, y + 0.5);
		LOG.info("[MinecraftSkylines] {} {} landed at ({}, {}, {}); CS1 surface there {} (y - surface {})", what, id,
			String.format("%.3f", x), String.format("%.3f", y), String.format("%.3f", z), String.format("%.3f", top),
			String.format("%.3f", y - top));
	}

	private static double highest(double x, double z, double lo, double hi) {
		List<SkyTri> tris = new ArrayList<>();
		CollisionStore.INSTANCE.trianglesNear(x - 0.01, lo, z - 0.01, x + 0.01, hi, z + 0.01, tris);
		double best = Double.NaN;
		for (SkyTri t : tris) {
			double h = t.walkable ? t.heightAt(x, z) : Double.NaN;
			if (h >= lo && h <= hi && !(h <= best)) {
				best = h;
			}
		}
		return best;
	}
}
