package dev.mcskylines.collision;

import static dev.mcskylines.collision.FluidGround.NONE;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import dev.mcskylines.protocol.CollisionRegion;
import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.Test;

class FluidGroundTest {
	private static final float EPS = 1e-4f;

	private static SkyTri tri(double... v) {
		float[] f = new float[9];
		for (int i = 0; i < 9; i++) {
			f[i] = (float) v[i];
		}
		return new SkyTri(f, 0, SkyTri.TERRAIN);
	}

	/** Large horizontal triangle at height y covering x+z <= 5 near the origin (so it covers cell 0,0 and its neighbours). */
	private static SkyTri flat(double y) {
		return tri(-5, y, -5, -5, y, 10, 10, y, -5);
	}

	private static List<SkyTri> list(SkyTri... t) {
		return new ArrayList<>(List.of(t));
	}

	@Test
	void noTrianglesIsNone() {
		assertEquals(NONE, FluidGround.groundTop(new ArrayList<SkyTri>(), 0, 0, 0));
	}

	@Test
	void flatTerrainInsideCellGivesItsFraction() {
		assertEquals(0.4f, FluidGround.groundTop(list(flat(0.4)), 0, 0, 0), EPS);
	}

	@Test
	void sameTriangleDoesNotReachCellsAboveOrBelow() {
		assertEquals(NONE, FluidGround.groundTop(list(flat(0.4)), 0, 1, 0));
		assertEquals(NONE, FluidGround.groundTop(list(flat(0.4)), 0, -1, 0));
	}

	@Test
	void planeTouchingCellBoundaryDoesNotCount() {
		assertEquals(NONE, FluidGround.groundTop(list(flat(1.0)), 0, 0, 0), "plane on the top face of the cell");
		assertEquals(NONE, FluidGround.groundTop(list(flat(1.0)), 0, 1, 0), "plane on the bottom face of the cell");
		assertEquals(0.999f, FluidGround.groundTop(list(flat(0.999)), 0, 0, 0), EPS);
	}

	@Test
	void boundsOverlapWithoutSurfaceOverlapIsNone() {
		// Hypotenuse runs along x + z = -1: the bounding box covers the cell, the triangle does not.
		SkyTri t = tri(-11, 0.4, -11, -11, 0.4, 10, 10, 0.4, -11);
		assertEquals(NONE, FluidGround.groundTop(list(t), 0, 0, 0));
	}

	@Test
	void verticalWallCrossingCellIsFull() {
		SkyTri wall = tri(0.5, -1, -1, 0.5, 3, -1, 0.5, -1, 3);
		assertEquals(1f, FluidGround.groundTop(list(wall), 0, 0, 0), EPS);
	}

	@Test
	void slopeRisingAboveCellTopClampsToOne() {
		// height = 0.5 + 2x: 0.5 at x=0, 2.5 at x=1.
		SkyTri ramp = tri(-5, -9.5, -5, -5, -9.5, 10, 10, 20.5, -5);
		assertEquals(1f, FluidGround.groundTop(list(ramp), 0, 0, 0), EPS);
	}

	@Test
	void smallTriangleMissingEverySampleUsesItsHeight() {
		SkyTri small = tri(0.2, 0.4, 0.2, 0.4, 0.4, 0.2, 0.2, 0.4, 0.4);
		assertEquals(0.4f, FluidGround.groundTop(list(small), 0, 0, 0), EPS);
	}

	@Test
	void topIsTheMaxOverCrossingTriangles() {
		assertEquals(0.7f, FluidGround.groundTop(list(flat(0.3), flat(0.7)), 0, 0, 0), EPS);
	}

	@Test
	void cellOffsetsAndNegativeCoordinates() {
		assertEquals(0.4f, FluidGround.groundTop(list(flat(2.4)), 0, 2, 0), EPS);
		assertEquals(NONE, FluidGround.groundTop(list(flat(0.4)), 0, 2, 0));
		SkyTri t = tri(-20, 5.25, -20, -20, 5.25, 10, 10, 5.25, -20);
		assertEquals(0.25f, FluidGround.groundTop(list(t), -4, 5, -8), EPS);
	}

	private static CollisionStore storeWith(CollisionRegion... regions) {
		CollisionStore s = new CollisionStore();
		for (CollisionRegion r : regions) {
			s.accept(r);
		}
		return s;
	}

	private static CollisionRegion region(int rx, int rz, double x0, double z0, double s, double y) {
		float[] v = { (float) x0, (float) y, (float) z0, (float) x0, (float) y, (float) (z0 + s), (float) (x0 + s), (float) y, (float) z0 };
		return new CollisionRegion(0, rx, rz, v, new short[] { SkyTri.TERRAIN });
	}

	@Test
	void storeBackedGroundTop() {
		CollisionStore s = storeWith(region(0, 0, 2, 2, 4, 5.4));
		assertEquals(0.4f, FluidGround.groundTop(s, 3, 5, 3), EPS);
		assertEquals(NONE, FluidGround.groundTop(s, 3, 6, 3));
		assertEquals(NONE, FluidGround.groundTop(s, 10, 5, 10));
	}

	@Test
	void isKnownFollowsLoadedRegionsWithFloorDiv() {
		CollisionStore s = storeWith(region(0, 0, 2, 2, 4, 5), region(-1, -1, -10, -10, 2, 0));
		assertTrue(FluidGround.isKnown(s, 0, 15));
		assertTrue(FluidGround.isKnown(s, 15, 0));
		assertFalse(FluidGround.isKnown(s, 16, 0));
		assertTrue(FluidGround.isKnown(s, -1, -1));
		assertTrue(FluidGround.isKnown(s, -16, -16));
		assertFalse(FluidGround.isKnown(s, -17, -1));
		assertFalse(FluidGround.isKnown(s, 0, -1));
	}
}
