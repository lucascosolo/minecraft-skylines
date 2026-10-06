package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x0190, guest to host (minor 9): the values Minecraft draws its sky with; the newest replaces the previous. */
public record SkyState(int flags, float[] skyColor, float[] fogColor, float[] sunriseColor, float starBrightness,
		float rainLevel, int moonPhase, float[] cloudColor, float cloudHeight, float cloudOffset, float cloudSpeed) {
	public static final int FLAG_SKY = 1;
	public static final int FLAG_CLOUDS = 2;
	public static final int MOON_PHASES = 8;

	public byte[] encode() {
		PayloadWriter w = new PayloadWriter().u8(flags);
		f32s(w, skyColor, 3);
		f32s(w, fogColor, 3);
		f32s(w, sunriseColor, 4);
		w.f32(starBrightness).f32(rainLevel).u8(moonPhase);
		f32s(w, cloudColor, 4);
		return w.f32(cloudHeight).f32(cloudOffset).f32(cloudSpeed).toByteArray();
	}

	public static SkyState decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int flags = r.u8();
		float[] sky = f32s(r, 3), fog = f32s(r, 3), sunrise = f32s(r, 4);
		float stars = r.f32(), rain = r.f32();
		int phase = r.u8();
		if (phase >= MOON_PHASES) {
			throw new ProtocolException("moon phase out of range: " + phase);
		}
		float[] cloud = f32s(r, 4);
		return new SkyState(flags, sky, fog, sunrise, stars, rain, phase, cloud, r.f32(), r.f32(), r.f32());
	}

	private static void f32s(PayloadWriter w, float[] v, int n) {
		for (int i = 0; i < n; i++) {
			w.f32(v[i]);
		}
	}

	private static float[] f32s(PayloadReader r, int n) throws ProtocolException {
		float[] v = new float[n];
		for (int i = 0; i < n; i++) {
			v[i] = r.f32();
		}
		return v;
	}
}
