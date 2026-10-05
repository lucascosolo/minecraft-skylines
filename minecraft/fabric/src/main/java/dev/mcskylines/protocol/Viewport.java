package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x0140, host to guest: the host's screen size; {@code uiScale} 0 lets the guest choose its GUI scale. */
public record Viewport(int width, int height, float uiScale) {
	public byte[] encode() {
		return new PayloadWriter().u32(width & 0xFFFFFFFFL).u32(height & 0xFFFFFFFFL).f32(uiScale).toByteArray();
	}

	public static Viewport decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		return new Viewport((int) r.u32(), (int) r.u32(), r.f32());
	}
}
