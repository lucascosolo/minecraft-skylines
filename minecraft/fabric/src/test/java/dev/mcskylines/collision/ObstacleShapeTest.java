package dev.mcskylines.collision;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.Test;

class ObstacleShapeTest {
	private static final double EPS = 1e-6;

	/** Box at the origin, yaw 0 (length along +Z), bottom at y = 0. */
	private static ObstacleBox box(int kind, double halfHeight, double halfLength, byte[] profile) {
		return new ObstacleBox(kind, 1, 0, halfHeight, 0, 0, 0.9, halfHeight, halfLength, 0, 0, 0, 0, profile);
	}

	private static byte[] p(int... v) {
		byte[] b = new byte[v.length];
		for (int i = 0; i < v.length; i++) {
			b[i] = (byte) v[i];
		}
		return b;
	}

	@Test
	void stepConstant() {
		assertEquals(0.5, ObstacleBox.STEP, 0.0);
	}

	@Test
	void rawProfileScalesByFullHeightOver255() {
		double[] t = box(2, 1, 3, p(255, 128, 0)).sliceTops();
		assertArrayEquals(new double[] {2.0, 128 * 2.0 / 255, 0.0}, t, EPS);
	}

	@Test
	void stairRuleLimitsAdjacentSlicesAndEndsForCars() {
		double[] t = box(1, 0.75, 2, p(255, 255, 255, 255)).sliceTops();
		assertArrayEquals(new double[] {0.5, 1.0, 1.0, 0.5}, t, EPS);
	}

	@Test
	void stairRuleLeavesCompliantProfilesAlone() {
		double[] t = box(1, 0.5, 2, p(127, 200, 127)).sliceTops(); // 0.498, 0.784, 0.498 already stair-legal
		assertArrayEquals(new double[] {127 / 255.0, 200 / 255.0, 127 / 255.0}, t, EPS);
	}

	@Test
	void stairRuleNotAppliedToTallVehiclesOrCitizens() {
		assertArrayEquals(new double[] {3.2, 3.2}, box(1, 1.6, 6, p(255, 255)).sliceTops(), EPS);
		assertArrayEquals(new double[] {1.8, 1.8}, box(2, 0.9, 0.3, p(255, 255)).sliceTops(), EPS);
	}

	@Test
	void syntheticCarProfileHasLowHoodAndBootAndStairLegalShape() {
		double[] t = box(1, 0.75, 2.25, new byte[0]).sliceTops();
		assertEquals(18, t.length);
		assertEquals(0.5, t[0], EPS);
		assertEquals(0.825, t[1], EPS);
		assertEquals(0.825, t[2], EPS);
		assertEquals(1.325, t[3], EPS);
		assertEquals(1.5, t[4], EPS);
		assertEquals(1.5, t[9], EPS);
		for (int i = 0; i < t.length; i++) {
			assertEquals(t[i], t[t.length - 1 - i], EPS, "symmetric at " + i);
			if (i > 0) {
				assertTrue(Math.abs(t[i] - t[i - 1]) <= ObstacleBox.STEP + EPS);
			}
		}
	}

	@Test
	void shortOrTallOrCitizenWithoutProfileIsOneFullSlice() {
		assertArrayEquals(new double[] {1.5}, box(1, 0.75, 1.5, new byte[0]).sliceTops(), EPS);
		assertArrayEquals(new double[] {3.2}, box(1, 1.6, 6, new byte[0]).sliceTops(), EPS);
		assertArrayEquals(new double[] {1.8}, box(2, 0.9, 3, new byte[0]).sliceTops(), EPS);
	}

	@Test
	void trianglesStayWithinFootprintAndBelowTheirSliceTop() {
		double yaw = 30, cx = 3, cz = -4, hl = 2.25, hw = 0.9;
		ObstacleBox b = new ObstacleBox(1, 1, cx, 0.75, cz, yaw, hw, 0.75, hl, 0, 0, 0, 0, new byte[0]);
		double[] tops = b.sliceTops();
		double r = Math.toRadians(yaw), wx = Math.cos(r), wz = Math.sin(r), lx = -Math.sin(r), lz = Math.cos(r);
		List<SkyTri> tris = new ArrayList<>();
		b.triangles(tris);
		assertTrue(tris.size() > 12);
		double slice = 2 * hl / tops.length;
		for (SkyTri t : tris) {
			double[][] vs = {{t.ax, t.ay, t.az}, {t.bx, t.by, t.bz}, {t.cx, t.cy, t.cz}};
			for (double[] v : vs) {
				double w = (v[0] - cx) * wx + (v[2] - cz) * wz, l = (v[0] - cx) * lx + (v[2] - cz) * lz;
				assertTrue(Math.abs(w) <= hw + 1e-4 && Math.abs(l) <= hl + 1e-4, "inside footprint");
				assertTrue(v[1] >= -1e-4, "above the bottom");
				double allowed = 0;
				for (int i = 0; i < tops.length; i++) {
					double lo = -hl + i * slice, hi = lo + slice;
					if (l >= lo - 1e-4 && l <= hi + 1e-4) {
						allowed = Math.max(allowed, tops[i]);
					}
				}
				assertTrue(v[1] <= allowed + 1e-4, "y " + v[1] + " above slice top " + allowed + " at l " + l);
			}
		}
	}

	@Test
	void emptyMiddleOfProfileProducesNoGeometry() {
		List<SkyTri> tris = new ArrayList<>();
		box(2, 1, 3, p(255, 0, 0, 255)).triangles(tris);
		assertFalse(tris.isEmpty());
		for (SkyTri t : tris) {
			for (double z : new double[] {t.az, t.bz, t.cz}) {
				assertTrue(Math.abs(z) >= 1.5 - 1e-4, "vertex in the empty middle at z " + z);
			}
		}
	}

	@Test
	void flatProfileIsAnOutwardWoundBox() {
		List<SkyTri> tris = new ArrayList<>();
		box(2, 1, 2, p(255)).triangles(tris);
		assertEquals(12, tris.size());
		for (SkyTri t : tris) {
			double ux = t.bx - t.ax, uy = t.by - t.ay, uz = t.bz - t.az, wx = t.cx - t.ax, wy = t.cy - t.ay, wz = t.cz - t.az;
			double qx = uy * wz - uz * wy, qy = uz * wx - ux * wz, qz = ux * wy - uy * wx;
			double mx = (t.ax + t.bx + t.cx) / 3, my = (t.ay + t.by + t.cy) / 3 - 1, mz = (t.az + t.bz + t.cz) / 3;
			assertTrue(mx * qx + my * qy + mz * qz > 0, "normal points into the box");
		}
	}

	@Test
	void emptySliceOnlyProfileProducesNothing() {
		List<SkyTri> tris = new ArrayList<>();
		box(2, 1, 2, p(0)).triangles(tris);
		assertTrue(tris.isEmpty());
	}

	@Test
	void carHasLowLevelNearEachEnd() {
		List<SkyTri> tris = new ArrayList<>();
		box(1, 0.75, 2.25, new byte[0]).triangles(tris);
		boolean lowFront = false, lowBack = false;
		for (SkyTri t : tris) {
			for (double[] v : new double[][] {{t.ay, t.az}, {t.by, t.bz}, {t.cy, t.cz}}) {
				if (v[0] > 0.1 && v[0] <= 0.5 + 1e-4) {
					lowFront |= v[1] > 2.25 - 0.5;
					lowBack |= v[1] < -2.25 + 0.5;
				}
			}
		}
		assertTrue(lowFront && lowBack);
	}

	@Test
	void supportTopTakesTheHighestOverlappedSlice() {
		ObstacleBox b = box(2, 1, 2, p(255, 128, 0, 255)); // slices of 1 m: [-2,-1] [-1,0] [0,1] [1,2]
		assertEquals(2.0, b.supportTop(0, -1.5, 0.1), EPS);
		assertEquals(128 * 2.0 / 255, b.supportTop(0, -0.5, 0.1), EPS);
		assertTrue(Double.isNaN(b.supportTop(0, 0.5, 0.1))); // only the empty slice
		assertEquals(2.0, b.supportTop(0, 0.95, 0.1), EPS); // overlaps the empty and the tall slice
		assertEquals(2.0, b.supportTop(0.95, -1.5, 0.1), EPS); // within halfWidth + radius
	}

	@Test
	void supportTopIsNaNOutsideTheFootprint() {
		ObstacleBox b = box(2, 1, 2, p(255, 255));
		assertTrue(Double.isNaN(b.supportTop(1.2, 0, 0.1)));
		assertTrue(Double.isNaN(b.supportTop(0, 3, 0.1)));
	}

	@Test
	void blocksOnlyWhereTheSurfaceIsAboveStepHeight() {
		ObstacleBox car = box(1, 0.75, 2.25, new byte[0]); // first slice top 0.5
		assertFalse(car.blocks(0, 0, -2.4, 0.3, 1.8, 0.6)); // beside the first stair
		assertTrue(car.blocks(0, 0, -2.4, 0.3, 1.8, 0.4));
		assertTrue(car.blocks(0, 0, -1.8, 0.3, 1.8, 0.6)); // hood is 0.825 high
		assertFalse(car.blocks(0, 0, -9, 0.3, 1.8, 0.6)); // far away
	}

	@Test
	void fullBoxBlocksFromTheGroundButNotFromItsRoof() {
		ObstacleBox b = box(2, 0.75, 2, new byte[0]); // one 1.5 m slice
		assertTrue(b.blocks(1.1, 0, 0, 0.3, 1.8, 0.6));
		assertFalse(b.blocks(1.1, 0, 0, 0.3, 1.8, 1.6)); // step covers it
		assertFalse(b.blocks(0, 1.5, 0, 0.3, 1.8, 0.6)); // standing on the roof
	}
}
