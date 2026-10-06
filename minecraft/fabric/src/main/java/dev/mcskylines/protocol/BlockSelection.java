package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x0134, guest to host (minor 4): the box outlined under the crosshair, in Minecraft coordinates. */
public record BlockSelection(boolean visible, float minX, float minY, float minZ, float maxX, float maxY, float maxZ, int kind) {
	public static final int KIND_BLOCK = 0;
	public static final int KIND_PLACEMENT = 1;
	public static final BlockSelection HIDDEN = new BlockSelection(false, 0, 0, 0, 0, 0, 0, KIND_BLOCK);

	public byte[] encode() {
		return new PayloadWriter().bool(visible).f32(minX).f32(minY).f32(minZ).f32(maxX).f32(maxY).f32(maxZ).u8(kind).toByteArray();
	}

	public static BlockSelection decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		return new BlockSelection(r.bool(), r.f32(), r.f32(), r.f32(), r.f32(), r.f32(), r.f32(), r.u8());
	}
}
