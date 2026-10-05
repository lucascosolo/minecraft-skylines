package dev.mcskylines.collision;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import dev.mcskylines.protocol.CollisionRegion;
import dev.mcskylines.protocol.CollisionReset;
import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.Test;

class CollisionStoreTest {
	/** One upward-facing triangle with corners (x0,y,z0), (x0,y,z0+s), (x0+s,y,z0). */
	private static CollisionRegion region(int epoch, int rx, int rz, double x0, double z0, double s, double y) {
		float[] v = { (float) x0, (float) y, (float) z0, (float) x0, (float) y, (float) (z0 + s), (float) (x0 + s), (float) y, (float) z0 };
		return new CollisionRegion(epoch, rx, rz, v, new short[] { SkyTri.TERRAIN });
	}

	private static List<SkyTri> near(CollisionStore s, double x0, double y0, double z0, double x1, double y1, double z1) {
		List<SkyTri> out = new ArrayList<>();
		s.trianglesNear(x0, y0, z0, x1, y1, z1, out);
		return out;
	}

	@Test
	void storesAndQueriesByBox() {
		CollisionStore s = new CollisionStore();
		assertTrue(s.accept(region(0, 0, 0, 2, 2, 4, 5)));
		assertEquals(1, near(s, 0, 4, 0, 16, 6, 16).size());
		assertEquals(0, near(s, 0, 6, 0, 16, 7, 16).size(), "above in Y");
		assertEquals(0, near(s, 10, 4, 10, 12, 6, 12).size(), "no overlap in XZ");
		assertEquals(1, near(s, -100, -100, -100, 100, 100, 100).size());
	}

	@Test
	void negativeRegionsAreKeptApart() {
		CollisionStore s = new CollisionStore();
		s.accept(region(0, -1, -1, -10, -10, 2, 0));
		s.accept(region(0, 1, 1, 20, 20, 2, 0));
		assertTrue(s.isLoaded(-1, -1));
		assertTrue(s.isLoaded(1, 1));
		assertFalse(s.isLoaded(1, -1));
		assertEquals(1, near(s, -11, -1, -11, -9, 1, -9).size());
	}

	@Test
	void replacesTheRegion() {
		CollisionStore s = new CollisionStore();
		s.accept(region(0, 0, 0, 2, 2, 2, 5));
		s.accept(region(0, 0, 0, 2, 2, 2, 9));
		List<SkyTri> t = near(s, 0, 0, 0, 16, 20, 16);
		assertEquals(1, t.size());
		assertEquals(9, t.get(0).ay, 1e-6);
	}

	@Test
	void emptyRegionCountsAsLoaded() {
		CollisionStore s = new CollisionStore();
		s.accept(new CollisionRegion(0, 3, 4, new float[0], new short[0]));
		assertTrue(s.isLoaded(3, 4));
		assertEquals(0, near(s, 48, -10, 64, 64, 10, 80).size());
	}

	@Test
	void resetDropsAllAndOlderEpochsAreRefused() {
		CollisionStore s = new CollisionStore();
		s.accept(region(0, 0, 0, 2, 2, 2, 5));
		s.accept(new CollisionReset(3));
		assertEquals(0, s.regionCount());
		assertEquals(3, s.epoch());
		assertFalse(s.accept(region(2, 0, 0, 2, 2, 2, 5)));
		assertFalse(s.isLoaded(0, 0));
		assertTrue(s.accept(region(3, 0, 0, 2, 2, 2, 5)));
		assertTrue(s.accept(region(4, 1, 0, 18, 2, 2, 5)));
	}

	@Test
	void epochComparisonIsUnsigned() {
		CollisionStore s = new CollisionStore();
		s.accept(new CollisionReset(0xFFFFFFF0));
		assertFalse(s.accept(region(5, 0, 0, 2, 2, 2, 5)));
		assertTrue(s.accept(region(0xFFFFFFF1, 0, 0, 2, 2, 2, 5)));
	}

	@Test
	void triangleOverhangingItsRegionIsFoundFromTheNeighbour() {
		CollisionStore s = new CollisionStore();
		s.accept(region(0, 0, 0, 12, 2, 8, 5)); // reaches x = 20, filed under region 0
		assertEquals(1, near(s, 18, 4, 3, 19, 6, 4).size());
	}

	@Test
	void regionsLoadedAround() {
		CollisionStore s = new CollisionStore();
		for (int rx = -1; rx <= 1; rx++) {
			for (int rz = -1; rz <= 1; rz++) {
				s.accept(region(0, rx, rz, rx * 16 + 1, rz * 16 + 1, 2, 0));
			}
		}
		assertTrue(s.regionsLoadedAround(5, 5, 1));
		assertTrue(s.regionsLoadedAround(-0.5, -0.5, 0));
		assertFalse(s.regionsLoadedAround(-3, -3, 1));
		assertFalse(s.regionsLoadedAround(5, 5, 2));
		assertFalse(s.regionsLoadedAround(40, 5, 0));
		assertTrue(s.regionsLoadedAround(40, 5, 0) == s.isLoaded(2, 0));
	}

	@Test
	void concurrentReadersSeeWholeRegions() throws Exception {
		CollisionStore s = new CollisionStore();
		Thread writer = new Thread(() -> {
			for (int i = 0; i < 2000; i++) {
				s.accept(region(0, 0, 0, 2, 2, 2, i % 2 == 0 ? 5 : 9));
			}
		});
		writer.start();
		for (int i = 0; i < 2000; i++) {
			List<SkyTri> t = near(s, 0, 0, 0, 16, 20, 16);
			assertTrue(t.size() <= 1);
		}
		writer.join();
	}
}
