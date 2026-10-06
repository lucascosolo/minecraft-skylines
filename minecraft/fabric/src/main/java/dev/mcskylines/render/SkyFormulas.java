package dev.mcskylines.render;

/** Minecraft 26.3's sky resource paths and cloud scroll (CloudRenderer.prepare), free of Minecraft classes for tests. */
public final class SkyFormulas {
	public static final String SUN_TEXTURE = "textures/environment/celestial/sun.png";
	public static final String CLOUDS_TEXTURE = "textures/environment/clouds.png";
	public static final int MAX_TEXTURE_BYTES = 2 * 1024 * 1024;
	private static final String[] MOON = {
		"full_moon", "waning_gibbous", "third_quarter", "waning_crescent", "new_moon", "waxing_crescent", "first_quarter", "waxing_gibbous",
	};

	private SkyFormulas() {
	}

	/** Blocks the clouds have moved along +x: ((gameTime mod (width * 400)) + partialTick) * 0.03, as CloudRenderer.prepare. */
	public static float cloudOffset(long gameTime, float partialTick, int cloudTextureWidth) {
		return (float) ((Math.floorMod(gameTime, cloudTextureWidth * 400L) + (double) partialTick) * 0.03);
	}

	/** Resource path of a moon phase's texture, phase as MoonPhase.index(). */
	public static String moonTexture(int phase) {
		if (phase < 0 || phase >= MOON.length) {
			throw new IllegalArgumentException("moon phase " + phase);
		}
		return "textures/environment/celestial/moon/" + MOON[phase] + ".png";
	}
}
