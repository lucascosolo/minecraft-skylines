package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x0111, host to guest. */
public record ExitPlayerMode(String reason) {
	public byte[] encode() {
		return new PayloadWriter().string(reason).toByteArray();
	}

	public static ExitPlayerMode decode(byte[] payload) throws ProtocolException {
		return new ExitPlayerMode(new PayloadReader(payload).string());
	}
}
