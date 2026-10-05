package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;
import java.util.UUID;

public record HostStatus(int flags, String cityName, UUID saveId, String gameVersion) {
	public static final int IN_CITY = 1;
	public static final int LOADING = 2;
	public static final int SIM_PAUSED = 4;
	public static final int PLAYER_MODE = 8;

	public boolean has(int flag) {
		return (flags & flag) == flag;
	}

	public byte[] encode() {
		return new PayloadWriter().u32(Integer.toUnsignedLong(flags)).string(cityName).uuid(saveId).string(gameVersion)
			.toByteArray();
	}

	public static HostStatus decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		return new HostStatus((int) r.u32(), r.string(), r.uuid(), r.string());
	}
}
