package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x0114, host to guest. {@code epoch} is a u32 held in an int. */
public record CollisionReset(int epoch) {
	public byte[] encode() {
		return new PayloadWriter().u32(Integer.toUnsignedLong(epoch)).toByteArray();
	}

	public static CollisionReset decode(byte[] payload) throws ProtocolException {
		return new CollisionReset((int) new PayloadReader(payload).u32());
	}
}
