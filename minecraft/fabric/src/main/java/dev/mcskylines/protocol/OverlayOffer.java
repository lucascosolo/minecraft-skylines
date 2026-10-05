package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x0141, guest to host: the shared-memory overlay file is ready. */
public record OverlayOffer(String path, int maxWidth, int maxHeight, int slotCount, long generation) {
	public byte[] encode() {
		return new PayloadWriter().string(path).u32(maxWidth & 0xFFFFFFFFL).u32(maxHeight & 0xFFFFFFFFL)
			.u32(slotCount & 0xFFFFFFFFL).u64(generation).toByteArray();
	}

	public static OverlayOffer decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		return new OverlayOffer(r.string(), (int) r.u32(), (int) r.u32(), (int) r.u32(), r.u64());
	}
}
