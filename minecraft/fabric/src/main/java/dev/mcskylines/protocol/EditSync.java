package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x0153 EDIT_SYNC (host to guest) and 0x0154 EDIT_SYNC_ACK (guest to host), minor 5: the save barrier. Same layout. */
public record EditSync(int openSeq, int token) {
	public byte[] encode() {
		return new PayloadWriter().u32(Integer.toUnsignedLong(openSeq)).u32(Integer.toUnsignedLong(token)).toByteArray();
	}

	public static EditSync decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		return new EditSync((int) r.u32(), (int) r.u32());
	}
}
