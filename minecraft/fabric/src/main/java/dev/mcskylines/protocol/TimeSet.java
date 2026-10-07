package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x0161 TIME_SET (guest to host), minor 16: move the city's clock forward to the next hour, then days further. */
public record TimeSet(float hour, int days) {
	public byte[] encode() {
		return new PayloadWriter().f32(hour).u16(days).toByteArray();
	}

	public static TimeSet decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		float hour = r.f32();
		int days = r.u16();
		if (!(hour >= 0f && hour < 24f)) throw new ProtocolException("TIME_SET hour " + hour + " outside [0, 24)");
		return new TimeSet(hour, days);
	}
}
