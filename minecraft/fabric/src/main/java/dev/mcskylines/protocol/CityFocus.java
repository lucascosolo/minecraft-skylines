package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x01D1 CITY_FOCUS, minor 18: where the city view's camera looks (Minecraft frame); flags bit 0 ACTIVE. */
public record CityFocus(float x, float z, int flags) {
	public static final int ACTIVE = 1;

	public boolean active() {
		return (flags & ACTIVE) != 0;
	}

	public byte[] encode() {
		return new PayloadWriter().f32(x).f32(z).u8(flags).toByteArray();
	}

	public static CityFocus decode(byte[] payload) throws ProtocolException {
		if (payload.length != 9) {
			throw new ProtocolException("city focus payload is " + payload.length + " bytes, not 9");
		}
		PayloadReader r = new PayloadReader(payload);
		CityFocus f = new CityFocus(r.f32(), r.f32(), r.u8());
		if (f.active() && !(Float.isFinite(f.x) && Float.isFinite(f.z))) {
			throw new ProtocolException("active city focus is not finite");
		}
		return f;
	}
}
