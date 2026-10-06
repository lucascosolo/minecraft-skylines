package dev.mcskylines.world;

import java.util.SplittableRandom;

/** Pure maths for growth catch-up: the sun's light by city time, deterministic hashing, and random-tick event times. */
public final class GrowthMath {
	public static final int EVENT_CAP = 256;
	private static final int DAY = 24000;
	private static final int[] KEY_TICK = {133, 11867, 13670, 22330, 24133};
	private static final float[] KEY_VALUE = {1f, 1f, 0.26666668f, 0.26666668f, 1f};

	private GrowthMath() {
	}

	/** The overworld day timeline's sky light multiplier (data/minecraft/timeline/day.json, 26.3). */
	public static float skyLightMultiplier(long cityTicks) {
		int t = (int) Math.floorMod(cityTicks, (long) DAY);
		if (t < KEY_TICK[0]) {
			t += DAY;
		}
		for (int i = 0; i < KEY_TICK.length - 1; i++) {
			int a = KEY_TICK[i], b = KEY_TICK[i + 1];
			if (t <= b) {
				float va = KEY_VALUE[i], vb = KEY_VALUE[i + 1];
				return va + ((float) (t - a) / (b - a)) * (vb - va);
			}
		}
		return 1f;
	}

	/** Level.updateSkyBrightness: how much the sky light is darkened at that time. */
	public static int skyDarken(long cityTicks) {
		return (int) (15.0f - 15.0f * skyLightMultiplier(cityTicks));
	}

	public static long mix(long citySeed, long key, long fromTick) {
		long z = citySeed ^ key * 0x9E3779B97F4A7C15L ^ fromTick * 0xC2B2AE3D27D4EB4FL;
		z = (z ^ z >>> 30) * 0xBF58476D1CE4E5B9L;
		z = (z ^ z >>> 27) * 0x94D049BB133111EBL;
		return z ^ z >>> 31;
	}

	/** The ticks in (fromTick, toTick] at which a block gets a random tick; at most {@code cap}, the earliest. */
	public static long[] eventTicks(long fromTick, long toTick, int randomTickSpeed, long seed, int cap) {
		if (toTick <= fromTick || randomTickSpeed <= 0 || cap <= 0) {
			return new long[0];
		}
		double p = Math.min(1.0, randomTickSpeed / 4096.0);
		SplittableRandom rnd = new SplittableRandom(seed);
		long[] out = new long[(int) Math.min(cap, toTick - fromTick)];
		int n = 0;
		long t = fromTick;
		while (n < out.length) {
			long gap = p >= 1.0 ? 1 : 1 + (long) Math.floor(Math.log(1.0 - rnd.nextDouble()) / Math.log1p(-p));
			if (gap < 1 || gap > toTick - t) {
				break;
			}
			t += gap;
			out[n++] = t;
		}
		return n == out.length ? out : java.util.Arrays.copyOf(out, n);
	}
}
