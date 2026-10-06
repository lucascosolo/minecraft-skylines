package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;
import java.util.UUID;

/** 0x0150, host to guest (minor 5): a paired city opened; its snapshot follows in BLOCK_EDITS batches. */
public record CityOpen(int openSeq, UUID saveId, String cityName, int editCount) {
	public byte[] encode() {
		return new PayloadWriter().u32(Integer.toUnsignedLong(openSeq)).uuid(saveId).string(cityName)
			.u32(Integer.toUnsignedLong(editCount)).toByteArray();
	}

	public static CityOpen decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		return new CityOpen((int) r.u32(), r.uuid(), r.string(), (int) r.u32());
	}
}
