package dev.mcskylines.collision;

import dev.mcskylines.protocol.DynamicObstacles;
import java.util.ArrayList;
import java.util.List;

/** The latest DYNAMIC_OBSTACLES set, extrapolated by its age and dropped when it goes stale. Safe from any thread. */
public final class DynamicObstacleStore {
	public static final DynamicObstacleStore INSTANCE = new DynamicObstacleStore();
	/** A set older than this is dropped (the host stopped sending). */
	public static final long EXPIRY_NANOS = 500_000_000L;

	private record Snapshot(List<ObstacleBox> boxes, long atNanos) {
	}

	private volatile Snapshot latest;

	public void accept(DynamicObstacles message, long nowNanos) {
		List<ObstacleBox> boxes = new ArrayList<>(message.obstacles().size());
		for (DynamicObstacles.Obstacle o : message.obstacles()) {
			boxes.add(ObstacleBox.of(o));
		}
		latest = new Snapshot(List.copyOf(boxes), nowNanos);
	}

	public void clear() {
		latest = null;
	}

	/** The latest set, each box advanced by the set's age; empty when there is none or it is stale. */
	public List<ObstacleBox> current(long nowNanos) {
		Snapshot s = latest;
		if (s == null) {
			return List.of();
		}
		long age = Math.max(0, nowNanos - s.atNanos());
		if (age >= EXPIRY_NANOS) {
			return List.of();
		}
		double seconds = age / 1e9;
		List<ObstacleBox> out = new ArrayList<>(s.boxes().size());
		for (ObstacleBox b : s.boxes()) {
			out.add(b.advanced(seconds));
		}
		return out;
	}
}
