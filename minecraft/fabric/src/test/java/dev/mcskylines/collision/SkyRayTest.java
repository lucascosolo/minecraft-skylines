/*
 * Ported from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
 * See minecraft/THIRD-PARTY-NOTICES.md. Adapted for Minecraft Skylines.
 */
package dev.mcskylines.collision;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.Test;

class SkyRayTest {
	private static SkyTri tri(double... v) {
		float[] f = new float[9];
		for (int i = 0; i < 9; i++) {
			f[i] = (float) v[i];
		}
		return new SkyTri(f, 0, SkyTri.TERRAIN);
	}

	/** Flat ground at height y covering [-10, 10] in x and z. */
	private static List<SkyTri> ground(double y) {
		return ramp(y, 0.0);
	}

	/** Plane y = y0 + slope * x covering [-10, 10] in x and z. */
	private static List<SkyTri> ramp(double y0, double slope) {
		double ya = y0 - 10 * slope;
		double yb = y0 + 10 * slope;
		List<SkyTri> out = new ArrayList<>();
		out.add(tri(-10, ya, -10, 10, yb, -10, 10, yb, 10));
		out.add(tri(-10, ya, -10, 10, yb, 10, -10, ya, 10));
		return out;
	}

	private static int[] offset(int face) {
		return switch (face) {
			case 0 -> new int[] { 0, -1, 0 };
			case 1 -> new int[] { 0, 1, 0 };
			case 2 -> new int[] { 0, 0, -1 };
			case 3 -> new int[] { 0, 0, 1 };
			case 4 -> new int[] { -1, 0, 0 };
			default -> new int[] { 1, 0, 0 };
		};
	}

	@Test
	void hitsGroundFromAbove() {
		SkyRay.Hit hit = SkyRay.cast(ground(10.3), 0.5, 12.0, 0.5, 0.5, 8.0, 0.5);
		assertNotNull(hit);
		assertEquals(10.3, hit.y(), 1e-5);
		assertEquals(1.0, hit.ny(), 1e-9);
		assertTrue(hit.t() > 0.0 && hit.t() < 1.0);
		assertNotNull(hit.tri());
	}

	@Test
	void missesWhenSegmentStopsShort() {
		assertNull(SkyRay.cast(ground(10.3), 0.5, 12.0, 0.5, 0.5, 11.0, 0.5));
	}

	@Test
	void normalFacesRayFromBelow() {
		SkyRay.Hit hit = SkyRay.cast(ground(10.3), 0.5, 8.0, 0.5, 0.5, 12.0, 0.5);
		assertNotNull(hit);
		assertEquals(-1.0, hit.ny(), 1e-9);
	}

	@Test
	void picksNearestOfSeveralSurfaces() {
		List<SkyTri> tris = ground(10.0);
		tris.addAll(ground(5.0));
		SkyRay.Hit hit = SkyRay.cast(tris, 0.5, 12.0, 0.5, 0.5, 0.0, 0.5);
		assertNotNull(hit);
		assertEquals(10.0, hit.y(), 1e-5);
	}

	@Test
	void groundFromAboveCells() {
		SkyRay.Hit hit = SkyRay.cast(ground(10.3), 0.5, 12.0, 0.5, 0.5, 8.0, 0.5);
		assertArrayEquals(new int[] { 0, 10, 0 }, SkyRay.surfaceCell(hit));
		assertArrayEquals(new int[] { 0, 11, 0 }, SkyRay.placementCell(hit));
	}

	@Test
	void groundAtExactBoundaryCells() {
		SkyRay.Hit hit = SkyRay.cast(ground(10.0), 0.5, 12.0, 0.5, 0.5, 8.0, 0.5);
		assertArrayEquals(new int[] { 0, 9, 0 }, SkyRay.surfaceCell(hit));
		assertArrayEquals(new int[] { 0, 10, 0 }, SkyRay.placementCell(hit));
	}

	@Test
	void groundFromBelowCells() {
		SkyRay.Hit hit = SkyRay.cast(ground(10.3), 0.5, 8.0, 0.5, 0.5, 12.0, 0.5);
		assertArrayEquals(new int[] { 0, 10, 0 }, SkyRay.surfaceCell(hit));
		assertArrayEquals(new int[] { 0, 9, 0 }, SkyRay.placementCell(hit));
	}

	@Test
	void verticalWallCells() {
		List<SkyTri> wall = new ArrayList<>();
		wall.add(tri(5.5, 0, -10, 5.5, 0, 10, 5.5, 20, 10));
		wall.add(tri(5.5, 0, -10, 5.5, 20, 10, 5.5, 20, -10));
		SkyRay.Hit hit = SkyRay.cast(wall, 0.5, 10.5, 0.5, 8.5, 10.5, 0.5);
		assertNotNull(hit);
		int[] s = SkyRay.surfaceCell(hit);
		int[] p = SkyRay.placementCell(hit);
		assertEquals(5, s[0]);
		assertEquals(4, p[0]);
		assertEquals(s[1], p[1]);
		assertEquals(s[2], p[2]);
	}

	@Test
	void dominantFaceMatchesMinecraftDirectionOrder() {
		assertEquals(1, SkyRay.dominantFace(0, 1, 0)); // UP
		assertEquals(0, SkyRay.dominantFace(0, -1, 0)); // DOWN
		assertEquals(2, SkyRay.dominantFace(0, 0.2, -0.9)); // NORTH (-Z)
		assertEquals(3, SkyRay.dominantFace(0, 0.2, 0.9)); // SOUTH (+Z)
		assertEquals(4, SkyRay.dominantFace(-0.9, 0.2, 0)); // WEST (-X)
		assertEquals(5, SkyRay.dominantFace(0.9, 0.2, 0)); // EAST (+X)
	}

	@Test
	void dominantFaceTiesPreferYThenXThenZ() {
		double a = Math.sqrt(1.0 / 3.0);
		assertEquals(1, SkyRay.dominantFace(a, a, a));
		assertEquals(0, SkyRay.dominantFace(-a, -a, -a));
		assertEquals(5, SkyRay.dominantFace(a, 0.1, a));
		assertEquals(4, SkyRay.dominantFace(-a, 0.1, -a));
	}

	private static void assertFaceLocationWithinReach(SkyRay.Hit hit) {
		int[] cell = SkyRay.placementCell(hit);
		double[] loc = SkyRay.faceLocation(hit);
		assertEquals(3, loc.length);
		for (int i = 0; i < 3; i++) {
			assertTrue(Math.abs(loc[i] - (cell[i] + 0.5)) < 1.0000001,
					"axis " + i + " loc=" + loc[i] + " cell=" + cell[i]);
		}
	}

	@Test
	void faceLocationOnFlatGroundSweep() {
		for (int i = 0; i <= 20; i++) {
			double y = 10.0 + i * 0.05;
			SkyRay.Hit hit = SkyRay.cast(ground(y), 0.37, 13.0, 0.81, 0.37, 7.0, 0.81);
			assertNotNull(hit);
			assertFaceLocationWithinReach(hit);
		}
	}

	@Test
	void faceLocationReplacesDominantAxisWithSharedPlane() {
		SkyRay.Hit hit = SkyRay.cast(ground(10.3), 0.5, 12.0, 0.5, 0.5, 8.0, 0.5);
		double[] loc = SkyRay.faceLocation(hit);
		assertEquals(0.5, loc[0], 1e-5);
		assertEquals(11.0, loc[1], 1e-9);
		assertEquals(0.5, loc[2], 1e-5);
	}

	@Test
	void faceLocationOnSlopesStaysWithinReach() {
		double[] slopes = { Math.tan(Math.toRadians(30)), Math.tan(Math.toRadians(45)), Math.tan(Math.toRadians(60)) };
		double[] xs = { -6.3, -2.2, 0.0, 0.45, 1.7, 4.9 };
		for (double s : slopes) {
			for (double x : xs) {
				for (double y0 = 10.0; y0 <= 11.0; y0 += 0.25) {
					SkyRay.Hit hit = SkyRay.cast(ramp(y0, s), x, 100, 0.3, x, -100, 0.3);
					assertNotNull(hit);
					assertFaceLocationWithinReach(hit);
				}
			}
		}
	}

	@Test
	void slopedPlacementCellIsNeighbourAcrossDominantFace() {
		double[] slopes = { Math.tan(Math.toRadians(30)), Math.tan(Math.toRadians(45)), Math.tan(Math.toRadians(60)) };
		for (double s : slopes) {
			SkyRay.Hit hit = SkyRay.cast(ramp(10.4, s), 1.7, 100, 0.3, 1.7, -100, 0.3);
			assertNotNull(hit);
			int[] surf = SkyRay.surfaceCell(hit);
			int[] place = SkyRay.placementCell(hit);
			int[] d = offset(SkyRay.dominantFace(hit.nx(), hit.ny(), hit.nz()));
			int changed = 0;
			for (int i = 0; i < 3; i++) {
				assertEquals(surf[i] + d[i], place[i]);
				if (place[i] != surf[i]) {
					changed++;
					assertEquals(1, Math.abs(place[i] - surf[i]));
				}
			}
			assertEquals(1, changed);
		}
	}
}
