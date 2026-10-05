package dev.mcskylines.protocol;

/** CS1 (Unity, left-handed, metres) to Minecraft (right-handed, blocks), per minecraft-skylines-v1.md. */
public final class MinecraftFrame {
	public static final double Y_OFFSET = 0;

	public record Pose(double x, double y, double z, double yaw, double pitch) {
	}

	private MinecraftFrame() {
	}

	/** Wraps degrees into [-180, 180). */
	public static double wrap180(double deg) {
		return deg - 360 * Math.floor((deg + 180) / 360);
	}

	public static Pose toMinecraft(double csX, double csY, double csZ, double eulerX, double eulerY) {
		return new Pose(csX, csY + Y_OFFSET, -csZ, wrap180(eulerY + 180), wrap180(eulerX));
	}
}
