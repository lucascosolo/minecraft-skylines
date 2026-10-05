package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x01F0, host to guest: a server command (no leading slash) for automated tests in the dev world. */
public record DebugCommand(String command) {
	public byte[] encode() {
		return new PayloadWriter().string(command).toByteArray();
	}

	public static DebugCommand decode(byte[] payload) throws ProtocolException {
		return new DebugCommand(new PayloadReader(payload).string());
	}
}
