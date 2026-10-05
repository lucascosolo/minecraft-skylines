package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;
import java.util.UUID;

public record GuestStatus(int flags, String worldName, UUID pairedSaveId) {
	public static final int IN_WORLD = 1;
	public static final int SCREEN_OPEN = 2;
	public static final UUID NO_SAVE = new UUID(0, 0);

	public boolean has(int flag) {
		return (flags & flag) == flag;
	}

	public byte[] encode() {
		return new PayloadWriter().u32(Integer.toUnsignedLong(flags)).string(worldName).uuid(pairedSaveId).toByteArray();
	}

	public static GuestStatus decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		return new GuestStatus((int) r.u32(), r.string(), r.uuid());
	}
}
