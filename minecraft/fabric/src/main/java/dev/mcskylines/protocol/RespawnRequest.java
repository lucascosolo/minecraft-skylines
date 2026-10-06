package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x01B1 RESPAWN_REQUEST (guest to host), minor 11: a joiner without a spawn point asks the host for one. */
public record RespawnRequest(int openSeq) {
	public byte[] encode() {
		return new PayloadWriter().u32(Integer.toUnsignedLong(openSeq)).toByteArray();
	}

	public static RespawnRequest decode(byte[] payload) throws ProtocolException {
		return new RespawnRequest((int) new PayloadReader(payload).u32());
	}
}
