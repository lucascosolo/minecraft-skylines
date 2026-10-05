package dev.mcskylines.player;

/** ENTER_PLAYER_MODE teleport, hold until collision is ready (or a timeout), then acknowledge. Pure state, no Minecraft. */
public final class TeleportHold {
	public static final long TIMEOUT_MS = 6000;

	public record Target(int seq, double x, double y, double z, float yaw, float pitch) {
	}

	private Target target;
	private boolean pending;
	private long takenAt;
	private int ack;

	public void request(Target target) {
		this.target = target;
		pending = true;
	}

	/** The requested target, once; the hold clock starts now. */
	public Target takePending(long nowMs) {
		if (!pending) {
			return null;
		}
		pending = false;
		takenAt = nowMs;
		return target;
	}

	public Target holdTarget() {
		return target;
	}

	public boolean held() {
		return target != null;
	}

	public boolean update(long nowMs, boolean collisionReady) {
		if (target != null && !pending && (collisionReady || nowMs - takenAt >= TIMEOUT_MS)) {
			ack = target.seq();
			target = null;
		}
		return held();
	}

	public int ack() {
		return ack;
	}

	public void cancel() {
		target = null;
		pending = false;
	}
}
