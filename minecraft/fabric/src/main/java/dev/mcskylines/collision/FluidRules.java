/*
 * Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
 * See minecraft/THIRD-PARTY-NOTICES.md. The decisions of SkyCraft's FlowingFluidMixin, as pure functions.
 */
package dev.mcskylines.collision;

/** Where a Minecraft fluid may move among host geometry. Tops are {@link FluidGround#groundTop} values. */
public final class FluidRules {
	/** A falling fluid's surface in its cell. */
	public static final float FALLING_SURFACE = 8.0F / 9.0F;
	/** How far a fluid's surface must clear the ground. */
	public static final float MARGIN = 0.05F;
	/** Ground within this of the ground a fluid leaves counts as level. */
	public static final float FLAT_TOLERANCE = 0.13F;

	private FluidRules() {
	}

	/** A fluid lying on host geometry in its own cell never sinks through it, nor into ground that nearly fills the cell below. */
	public static boolean refuseDown(float sourceTop, float targetTop) {
		return sourceTop != FluidGround.NONE || targetTop >= FALLING_SURFACE - MARGIN;
	}

	/**
	 * Sideways: never into an empty cell right under host geometry (under the ground or an overhang); onto level or
	 * downhill ground always; uphill only where its surface there would clear the ground.
	 */
	public static boolean refuseSideways(float sourceTop, float targetTop, float aboveTargetTop, float surface) {
		if (targetTop == FluidGround.NONE) {
			return aboveTargetTop != FluidGround.NONE;
		}
		boolean flatOrDownhill = targetTop <= Math.max(sourceTop, 0.0F) + FLAT_TOLERANCE;
		return !flatOrDownhill && targetTop >= surface - MARGIN;
	}

	/** The fluid's surface where it spreads to: one level less (lava two), a falling fluid lands at 7, a look-ahead 0.8. */
	public static float spreadSurface(boolean empty, boolean falling, int amount, boolean lava) {
		if (empty) {
			return 0.8F;
		}
		if (falling) {
			return 7.0F / 9.0F;
		}
		return Math.max(0, amount - (lava ? 2 : 1)) / 9.0F;
	}
}
