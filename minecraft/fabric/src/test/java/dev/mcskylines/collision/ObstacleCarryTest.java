package dev.mcskylines.collision;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

class ObstacleCarryTest {
	private static final double EPS = 1e-9;

	private static ObstacleBox box(double x, double z, double yaw, double vx, double vy, double vz, double yawRate) {
		return new ObstacleBox(1, 1, x, 1, z, yaw, 1, 1, 2, vx, vy, vz, yawRate, new byte[0]);
	}

	@Test
	void pureTranslationCarriesByVelocity() {
		double[] c = box(10, 20, 33, 1, 2, 3, 0).carry(11, 1, 21, 0.5);
		assertArrayEquals(new double[] {0.5, 1.0, 1.5, 0.0}, c, EPS);
	}

	@Test
	void zeroVelocityAndYawRateCarryNothing() {
		assertArrayEquals(new double[] {0, 0, 0, 0}, box(10, 20, 33, 0, 0, 0, 0).carry(11, 1, 21, 0.7), EPS);
	}

	@Test
	void rotatesAPointAheadOfTheCentreAboutTheCentre() {
		// yaw 0: length axis +Z. Feet 2 m ahead; after 90 deg the length axis is (-1, 0, 0).
		double[] c = box(0, 0, 0, 0, 0, 0, 90).carry(0, 1, 2, 1.0);
		assertEquals(-2.0, c[0], EPS);
		assertEquals(0.0, c[1], EPS);
		assertEquals(-2.0, c[2], EPS);
		assertEquals(90.0, c[3], EPS);
	}

	@Test
	void rotationPreservesDistanceToCentre() {
		ObstacleBox b = box(5, -3, 20, 0, 0, 0, 45);
		double fx = 6.3, fz = -1.1;
		double[] c = b.carry(fx, 1.2, fz, 0.4);
		assertEquals(18.0, c[3], EPS);
		assertEquals(0.0, c[1], EPS);
		double before = Math.hypot(fx - 5, fz + 3);
		double after = Math.hypot(fx + c[0] - 5, fz + c[2] + 3);
		assertEquals(before, after, 1e-9);
		assertTrue(Math.abs(c[0]) + Math.abs(c[2]) > 0.1, "the feet actually move");
	}

	@Test
	void rotationAndTranslationAdd() {
		double[] c = box(0, 0, 0, 1, 0, 0, 90).carry(0, 1, 2, 1.0);
		assertEquals(-1.0, c[0], EPS);
		assertEquals(-2.0, c[2], EPS);
		assertEquals(90.0, c[3], EPS);
	}
}
