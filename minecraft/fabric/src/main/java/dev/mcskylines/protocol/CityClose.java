package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x0152, host to guest (minor 5): the open city was unloaded. */
public record CityClose(int openSeq) {
	public byte[] encode() {
		return new PayloadWriter().u32(Integer.toUnsignedLong(openSeq)).toByteArray();
	}

	public static CityClose decode(byte[] payload) throws ProtocolException {
		return new CityClose((int) new PayloadReader(payload).u32());
	}
}
