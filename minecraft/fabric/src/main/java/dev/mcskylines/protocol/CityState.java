package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x0155, guest to host (minor 5): where the guest is with an open. */
public record CityState(int openSeq, int state, int appliedCount) {
	public static final int APPLYING = 0;
	public static final int READY = 1;
	public static final int CLOSED = 2;

	public byte[] encode() {
		return new PayloadWriter().u32(Integer.toUnsignedLong(openSeq)).u8(state).u32(Integer.toUnsignedLong(appliedCount))
			.toByteArray();
	}

	public static CityState decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		return new CityState((int) r.u32(), r.u8(), (int) r.u32());
	}
}
