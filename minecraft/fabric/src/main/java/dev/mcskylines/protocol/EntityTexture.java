package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x01E1 ENTITY_TEXTURE (guest to host), minor 14: an encoded image (format 1 = PNG), top row first. */
public record EntityTexture(int textureId, int width, int height, int format, byte[] data) {
	public static final int PNG = 1;
	public static final int MAX_LENGTH = 4194304;

	public byte[] encode() {
		if (data.length > MAX_LENGTH) {
			throw new IllegalArgumentException("texture " + data.length + " bytes, above " + MAX_LENGTH);
		}
		PayloadWriter w = new PayloadWriter().u32(Integer.toUnsignedLong(textureId)).u32(Integer.toUnsignedLong(width))
				.u32(Integer.toUnsignedLong(height)).u8(format).u32(data.length);
		for (byte b : data) {
			w.u8(b & 0xFF);
		}
		return w.toByteArray();
	}

	public static EntityTexture decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int id = (int) r.u32(), width = (int) r.u32(), height = (int) r.u32(), format = r.u8();
		long length = r.u32();
		if (length > MAX_LENGTH) {
			throw new ProtocolException("texture length " + length + " above " + MAX_LENGTH);
		}
		byte[] data = new byte[(int) length];
		for (int i = 0; i < data.length; i++) {
			data[i] = (byte) r.u8();
		}
		return new EntityTexture(id, width, height, format, data);
	}
}
