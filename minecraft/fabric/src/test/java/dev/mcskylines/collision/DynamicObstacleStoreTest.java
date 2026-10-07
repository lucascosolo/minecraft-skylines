package dev.mcskylines.collision;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import dev.mcskylines.protocol.DynamicObstacles;
import java.util.List;
import org.junit.jupiter.api.Test;

class DynamicObstacleStoreTest {
	private static final long MS = 1_000_000L;

	private static DynamicObstacles.Obstacle ob(int id, float x, float vx, float vz) {
		return new DynamicObstacles.Obstacle(1, id, x, 1f, 0f, 0f, 1f, 1f, 2f, vx, 0f, vz);
	}

	private static DynamicObstacles msg(DynamicObstacles.Obstacle... os) {
		return new DynamicObstacles(List.of(os));
	}

	@Test
	void instanceExistsAndExpiryConstant() {
		assertNotNull(DynamicObstacleStore.INSTANCE);
		assertEquals(500_000_000L, DynamicObstacleStore.EXPIRY_NANOS);
	}

	@Test
	void emptyUntilFirstAccept() {
		assertTrue(new DynamicObstacleStore().current(0).isEmpty());
	}

	@Test
	void currentReturnsAcceptedSetAtAcceptTime() {
		DynamicObstacleStore s = new DynamicObstacleStore();
		s.accept(msg(ob(1, 5f, 0f, 0f), ob(2, 9f, 0f, 0f)), 1000 * MS);
		List<ObstacleBox> now = s.current(1000 * MS);
		assertEquals(2, now.size());
		assertEquals(1, now.get(0).id);
		assertEquals(5.0, now.get(0).x, 1e-9);
		assertEquals(2, now.get(1).id);
	}

	@Test
	void acceptReplacesTheWholeSet() {
		DynamicObstacleStore s = new DynamicObstacleStore();
		s.accept(msg(ob(1, 0f, 0f, 0f), ob(2, 0f, 0f, 0f)), 0);
		s.accept(msg(ob(3, 0f, 0f, 0f)), 100 * MS);
		List<ObstacleBox> now = s.current(100 * MS);
		assertEquals(1, now.size());
		assertEquals(3, now.get(0).id);
	}

	@Test
	void currentExtrapolatesByAge() {
		DynamicObstacleStore s = new DynamicObstacleStore();
		s.accept(msg(ob(1, 0f, 4f, -2f)), 1000 * MS);
		ObstacleBox b = s.current(1250 * MS).get(0);
		assertEquals(1.0, b.x, 1e-6);
		assertEquals(0.0, b.y - 1.0, 1e-6);
		assertEquals(-0.5, b.z, 1e-6);
	}

	@Test
	void currentTurnsByYawRateAndKeepsTheProfile() {
		DynamicObstacleStore s = new DynamicObstacleStore();
		DynamicObstacles.Obstacle o = new DynamicObstacles.Obstacle(2, 1, 0f, 1f, 0f, 10f, 1f, 1f, 2f, 0f, 0f, 0f, 30f,
				new byte[] {(byte) 255, 0, (byte) 255});
		s.accept(msg(o), 1000 * MS);
		ObstacleBox b = s.current(1100 * MS).get(0);
		assertEquals(13.0, b.yaw, 1e-6);
		assertEquals(30.0, b.yawRate, 0.0);
		assertEquals(3, b.sliceTops().length);
	}

	@Test
	void negativeAgeIsTreatedAsZero() {
		DynamicObstacleStore s = new DynamicObstacleStore();
		s.accept(msg(ob(1, 3f, 4f, 0f)), 1000 * MS);
		List<ObstacleBox> now = s.current(900 * MS);
		assertEquals(1, now.size());
		assertEquals(3.0, now.get(0).x, 1e-9);
	}

	@Test
	void expiresAtExactlyHalfASecond() {
		DynamicObstacleStore s = new DynamicObstacleStore();
		s.accept(msg(ob(1, 0f, 0f, 0f)), 1000 * MS);
		assertEquals(1, s.current(1000 * MS + DynamicObstacleStore.EXPIRY_NANOS - 1).size());
		assertTrue(s.current(1000 * MS + DynamicObstacleStore.EXPIRY_NANOS).isEmpty());
		assertTrue(s.current(1000 * MS + DynamicObstacleStore.EXPIRY_NANOS + 1).isEmpty());
		assertTrue(s.current(5000 * MS).isEmpty());
	}

	@Test
	void clearEmptiesTheStore() {
		DynamicObstacleStore s = new DynamicObstacleStore();
		s.accept(msg(ob(1, 0f, 0f, 0f)), 0);
		s.clear();
		assertTrue(s.current(0).isEmpty());
	}

	@Test
	void emptyMessageGivesEmptyList() {
		DynamicObstacleStore s = new DynamicObstacleStore();
		s.accept(msg(ob(1, 0f, 0f, 0f)), 0);
		s.accept(msg(), 10 * MS);
		assertTrue(s.current(10 * MS).isEmpty());
	}
}
