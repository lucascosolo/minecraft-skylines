package dev.mcskylines.collision;

import static dev.mcskylines.collision.FluidGround.NONE;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;

class FluidRulesTest {
	@Test
	void constants() {
		assertEquals(8f / 9f, FluidRules.FALLING_SURFACE, 1e-6f);
		assertEquals(0.05f, FluidRules.MARGIN, 1e-6f);
		assertEquals(0.13f, FluidRules.FLAT_TOLERANCE, 1e-6f);
		assertEquals(-1f, NONE);
	}

	@Test
	void refuseDownWhenSourceRestsOnGeometry() {
		assertTrue(FluidRules.refuseDown(0f, NONE));
		assertTrue(FluidRules.refuseDown(0.4f, NONE));
		assertTrue(FluidRules.refuseDown(1f, 0.2f));
	}

	@Test
	void refuseDownWhenTargetIsNearlyFilledByGeometry() {
		assertTrue(FluidRules.refuseDown(NONE, 1f));
		assertTrue(FluidRules.refuseDown(NONE, 0.85f));
		assertFalse(FluidRules.refuseDown(NONE, 0.83f));
	}

	@Test
	void allowDownIntoOpenOrLowTarget() {
		assertFalse(FluidRules.refuseDown(NONE, NONE));
		assertFalse(FluidRules.refuseDown(NONE, 0.5f));
		assertFalse(FluidRules.refuseDown(NONE, 0f));
	}

	@Test
	void sidewaysIntoOpenCellOnlyRefusedUnderGroundOrOverhang() {
		assertFalse(FluidRules.refuseSideways(NONE, NONE, NONE, 0.9f));
		assertFalse(FluidRules.refuseSideways(0.3f, NONE, NONE, 0.9f));
		assertTrue(FluidRules.refuseSideways(NONE, NONE, 0.3f, 0.9f));
		assertTrue(FluidRules.refuseSideways(0.3f, NONE, 1f, 0.1f));
	}

	@Test
	void sidewaysFlatOrDownhillIsAllowed() {
		assertFalse(FluidRules.refuseSideways(NONE, 0.1f, NONE, 0.1f), "within tolerance of ground level 0");
		assertFalse(FluidRules.refuseSideways(0.4f, 0.52f, NONE, 0.5f), "within tolerance of source top");
		assertFalse(FluidRules.refuseSideways(0.8f, 0.3f, NONE, 0.3f), "downhill");
	}

	@Test
	void sidewaysUphillRefusedWhenTargetReachesSurface() {
		assertTrue(FluidRules.refuseSideways(NONE, 0.5f, NONE, 0.5f));
		assertTrue(FluidRules.refuseSideways(0.4f, 0.6f, NONE, 0.6f));
		assertTrue(FluidRules.refuseSideways(0.4f, 0.54f, NONE, 0.55f));
	}

	@Test
	void sidewaysUphillAllowedWhenSurfaceIsHigherThanTarget() {
		assertFalse(FluidRules.refuseSideways(NONE, 0.5f, NONE, 0.9f));
		assertFalse(FluidRules.refuseSideways(0.4f, 0.6f, NONE, 0.7f));
	}

	@Test
	void spreadSurfaceSpecialCases() {
		assertEquals(0.8f, FluidRules.spreadSurface(true, false, 5, false), 1e-6f);
		assertEquals(0.8f, FluidRules.spreadSurface(true, true, 5, true), 1e-6f, "empty wins");
		assertEquals(7f / 9f, FluidRules.spreadSurface(false, true, 8, false), 1e-6f);
		assertEquals(7f / 9f, FluidRules.spreadSurface(false, true, 3, true), 1e-6f);
	}

	@Test
	void spreadSurfaceFromAmount() {
		assertEquals(8f / 9f, FluidRules.spreadSurface(false, false, 9, false), 1e-6f);
		assertEquals(3f / 9f, FluidRules.spreadSurface(false, false, 4, false), 1e-6f);
		assertEquals(0f, FluidRules.spreadSurface(false, false, 1, false), 1e-6f);
		assertEquals(0f, FluidRules.spreadSurface(false, false, 0, false), 1e-6f);
		assertEquals(7f / 9f, FluidRules.spreadSurface(false, false, 9, true), 1e-6f);
		assertEquals(0f, FluidRules.spreadSurface(false, false, 2, true), 1e-6f);
		assertEquals(0f, FluidRules.spreadSurface(false, false, 1, true), 1e-6f);
	}
}
