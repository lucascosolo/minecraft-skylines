package dev.mcskylines.collision;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import dev.mcskylines.protocol.DynamicObstacles;
import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.Test;

class ObstacleBoxTest {
	private static final double EPS = 1e-9;
	private static final double R = 0.3, H = 1.8;

	/** Vehicle at the origin, length along +X (yaw -90), moving at vx. */
	private static ObstacleBox car(double vx) {
		return new ObstacleBox(1, 7, 0, 0.75, 0, -90, 1, 0.75, 2, vx, 0, 0);
	}

	private static void assertPush(double dx, double dz, double[] got) {
		assertEquals(2, got.length);
		assertEquals(dx, got[0], EPS);
		assertEquals(dz, got[1], EPS);
	}

	@Test
	void constants() {
		assertEquals(0.1, ObstacleBox.MIN_PUSH_SPEED, 0.0);
		assertEquals(0.01, ObstacleBox.MARGIN, 0.0);
	}

	@Test
	void ofCopiesProtocolObstacle() {
		DynamicObstacles.Obstacle o = new DynamicObstacles.Obstacle(2, -1, 1f, 2f, 3f, 45f, 0.5f, 0.75f, 0.25f, 1f, 0f, -1f);
		ObstacleBox b = ObstacleBox.of(o);
		assertEquals(2, b.kind);
		assertEquals(-1, b.id);
		assertEquals(1.0, b.x, 0.0);
		assertEquals(2.0, b.y, 0.0);
		assertEquals(3.0, b.z, 0.0);
		assertEquals(45.0, b.yaw, 0.0);
		assertEquals(0.5, b.halfWidth, 0.0);
		assertEquals(0.75, b.halfHeight, 0.0);
		assertEquals(0.25, b.halfLength, 0.0);
		assertEquals(1.0, b.vx, 0.0);
		assertEquals(0.0, b.vy, 0.0);
		assertEquals(-1.0, b.vz, 0.0);
	}

	@Test
	void advancedMovesByVelocityOnly() {
		ObstacleBox b = new ObstacleBox(1, 9, 1, 2, 3, 30, 1, 0.5, 2, 4, 1, -2).advanced(0.25);
		assertEquals(2.0, b.x, EPS);
		assertEquals(2.25, b.y, EPS);
		assertEquals(2.5, b.z, EPS);
		assertEquals(1, b.kind);
		assertEquals(9, b.id);
		assertEquals(30.0, b.yaw, 0.0);
		assertEquals(1.0, b.halfWidth, 0.0);
		assertEquals(0.5, b.halfHeight, 0.0);
		assertEquals(2.0, b.halfLength, 0.0);
		assertEquals(4.0, b.vx, 0.0);
		assertEquals(1.0, b.vy, 0.0);
		assertEquals(-2.0, b.vz, 0.0);
	}

	@Test
	void trianglesAreTwelveOutwardFacingAndSpanTheCorners() {
		// Kind 2 (citizen) is always one box; a car this size would get the hood/cabin/boot profile.
		ObstacleBox b = new ObstacleBox(2, 1, 10, 5, 20, -90, 1, 0.5, 2, 0, 0, 0);
		List<SkyTri> tris = new ArrayList<>();
		b.triangles(tris);
		assertEquals(12, tris.size());
		double minX = 1e9, maxX = -1e9, minY = 1e9, maxY = -1e9, minZ = 1e9, maxZ = -1e9;
		for (SkyTri t : tris) {
			assertEquals(0, t.flags);
			double ux = t.bx - t.ax, uy = t.by - t.ay, uz = t.bz - t.az;
			double wx = t.cx - t.ax, wy = t.cy - t.ay, wz = t.cz - t.az;
			double qx = uy * wz - uz * wy, qy = uz * wx - ux * wz, qz = ux * wy - uy * wx;
			assertTrue(qx * qx + qy * qy + qz * qz > 1e-6, "degenerate triangle");
			double mx = (t.ax + t.bx + t.cx) / 3 - 10, my = (t.ay + t.by + t.cy) / 3 - 5, mz = (t.az + t.bz + t.cz) / 3 - 20;
			assertTrue(mx * qx + my * qy + mz * qz > 0, "normal points into the box");
			double[][] vs = {{t.ax, t.ay, t.az}, {t.bx, t.by, t.bz}, {t.cx, t.cy, t.cz}};
			for (double[] v : vs) {
				minX = Math.min(minX, v[0]);
				maxX = Math.max(maxX, v[0]);
				minY = Math.min(minY, v[1]);
				maxY = Math.max(maxY, v[1]);
				minZ = Math.min(minZ, v[2]);
				maxZ = Math.max(maxZ, v[2]);
				assertTrue(Math.abs(Math.abs(v[0] - 10) - 2) < 1e-4, "x is a corner x");
				assertTrue(Math.abs(Math.abs(v[1] - 5) - 0.5) < 1e-4, "y is a corner y");
				assertTrue(Math.abs(Math.abs(v[2] - 20) - 1) < 1e-4, "z is a corner z");
			}
		}
		assertEquals(8.0, minX, 1e-4);
		assertEquals(12.0, maxX, 1e-4);
		assertEquals(4.5, minY, 1e-4);
		assertEquals(5.5, maxY, 1e-4);
		assertEquals(19.0, minZ, 1e-4);
		assertEquals(21.0, maxZ, 1e-4);
	}

	@Test
	void overlapsIsStrictOnEveryAxis() {
		ObstacleBox b = new ObstacleBox(1, 1, 0, 0.75, 0, 0, 1, 0.75, 2, 0, 0, 0); // yaw 0: length along +Z
		double r = 0.25;
		assertTrue(b.overlaps(0, 0, 0, r, H));
		assertTrue(b.overlaps(1.2, 0, 0, r, H));
		assertFalse(b.overlaps(1.25, 0, 0, r, H)); // exactly halfWidth + radius
		assertFalse(b.overlaps(-1.25, 0, 0, r, H));
		assertTrue(b.overlaps(0, 0, 2.2, r, H));
		assertFalse(b.overlaps(0, 0, 2.25, r, H)); // exactly halfLength + radius
		assertFalse(b.overlaps(0, 0, -2.25, r, H));
		assertFalse(b.overlaps(0, 1.5, 0, r, H)); // standing exactly on the roof
		assertTrue(b.overlaps(0, 1.49, 0, r, H));
		assertFalse(b.overlaps(0, -1.8, 0, r, H)); // head exactly at the underside
		assertTrue(b.overlaps(0, -1.79, 0, r, H));
	}

	@Test
	void overlapsUsesBoxYaw() {
		ObstacleBox b = car(0); // length along +X
		assertTrue(b.overlaps(2.2, 0, 0, R, H));
		assertFalse(b.overlaps(0, 0, 2.2, R, H));
	}

	@Test
	void pushOutIsZeroWhenNotOverlapping() {
		assertPush(0, 0, car(5).pushOut(5, 0, 5, R, H));
		assertPush(0, 0, car(5).pushOut(0, 1.5, 0, R, H)); // on the roof
	}

	@Test
	void movingBoxPushesPlayerOutOfTheFront() {
		assertPush(0.81, 0, car(5).pushOut(1.5, 0, 0.2, R, H));
	}

	@Test
	void movingBoxNeverPushesPlayerBackwardsThroughIt() {
		// Rear face is nearest (0.4) but excluded; nearer remaining face is the +Z side (0.8).
		assertPush(0, 0.81, car(5).pushOut(-1.9, 0, 0.5, R, H));
	}

	@Test
	void stationaryBoxPushesOutThroughTheNearestFace() {
		assertPush(-0.41, 0, car(0).pushOut(-1.9, 0, 0.5, R, H));
	}

	@Test
	void slowBoxBelowMinPushSpeedDoesNotExcludeFaces() {
		assertPush(-0.41, 0, car(0.05).pushOut(-1.9, 0, 0.5, R, H));
	}

	@Test
	void pushOutFollowsYaw() {
		ObstacleBox b = new ObstacleBox(1, 1, 10, 1, 10, 0, 1, 1, 2, 0, 0, 0); // length along +Z
		assertPush(0, 0.21, b.pushOut(10.5, 1, 12.1, R, H)); // front depth 0.2 beats side 0.8
	}

	@Test
	void pushOutAtFortyFiveDegrees() {
		ObstacleBox b = new ObstacleBox(1, 1, 0, 1, 0, 45, 1, 1, 2, 0, 0, 0); // L = (-s, 0, s)
		double s = Math.sqrt(0.5);
		double[] got = b.pushOut(-2.0 * s, 1, 2.0 * s, R, H); // 2.0 along L: depth 0.3
		assertArrayEquals(new double[] {-0.31 * s, 0.31 * s}, got, EPS);
	}

	@Test
	void carGetsLowHoodAndBootEitherSideOfTheCabin() {
		// A 4.5 m long, 1.5 m tall car standing on y = 0, length along z (yaw 0).
		ObstacleBox car = new ObstacleBox(1, 3, 0, 0.75, 0, 0, 0.9, 0.75, 2.25, 0, 0, 0);
		List<SkyTri> tris = new ArrayList<>();
		car.triangles(tris);
		assertEquals(36, tris.size());
		double hood = ObstacleBox.CAR_END_HEIGHT * 1.5; // top of hood and boot above the ground
		double cabinStart = 2.25 - 2 * ObstacleBox.CAR_END_SHARE * 2.25;
		for (SkyTri t : tris) {
			double[][] vs = {{t.ax, t.ay, t.az}, {t.bx, t.by, t.bz}, {t.cx, t.cy, t.cz}};
			for (double[] v : vs) {
				if (Math.abs(v[2]) > cabinStart + 1e-4) {
					assertTrue(v[1] <= hood + 1e-4, "hood and boot stay low: y " + v[1] + " at z " + v[2]);
				}
				assertTrue(v[1] >= -1e-4 && v[1] <= 1.5 + 1e-4, "within the car's height");
				assertTrue(Math.abs(v[2]) <= 2.25 + 1e-4, "within the car's length");
			}
		}
		List<SkyTri> bus = new ArrayList<>();
		new ObstacleBox(1, 4, 0, 1.6, 0, 0, 1.25, 1.6, 6, 0, 0, 0).triangles(bus);
		assertEquals(12, bus.size(), "a bus (3.2 m tall) stays one box");
		List<SkyTri> parked = new ArrayList<>();
		new ObstacleBox(3, 5, 0, 0.75, 0, 0, 0.9, 0.75, 2.25, 0, 0, 0).triangles(parked);
		assertEquals(36, parked.size(), "parked cars get the profile too");
	}
}
