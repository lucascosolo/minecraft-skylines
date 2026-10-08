package dev.mcskylines.world;

/**
 * Protocol 1.18: the chunks simulated around the city view's focus. A ticket of radius r (level 33 - r) loads chunks
 * within r and ticks entities within r - 2, so TICKET_RADIUS leaves two loaded, frozen rings around the 5 x 5 ticking
 * chunks (80 m square): mobs stop at its edge instead of walking off the shadow ground the host streamed (64 m).
 */
public record CameraArea(int chunkX, int chunkZ) {
	public static final int TICK_RADIUS = 2;
	public static final int TICKET_RADIUS = TICK_RADIUS + 2;

	public static CameraArea at(double x, double z) {
		return new CameraArea(chunk(x), chunk(z));
	}

	public boolean ticks(int cx, int cz) {
		return Math.max(Math.abs(cx - chunkX), Math.abs(cz - chunkZ)) <= TICK_RADIUS;
	}

	public boolean ticksAt(double x, double z) {
		return ticks(chunk(x), chunk(z));
	}

	public double minX() {
		return (chunkX - TICK_RADIUS) * 16.0;
	}

	public double maxX() {
		return (chunkX + TICK_RADIUS + 1) * 16.0;
	}

	public double minZ() {
		return (chunkZ - TICK_RADIUS) * 16.0;
	}

	public double maxZ() {
		return (chunkZ + TICK_RADIUS + 1) * 16.0;
	}

	private static int chunk(double v) {
		return Math.floorDiv((int) Math.floor(v), 16);
	}
}
