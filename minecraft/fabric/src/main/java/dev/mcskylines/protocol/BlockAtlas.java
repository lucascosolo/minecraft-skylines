package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;
import java.util.Arrays;

/** 0x0130, guest to host: the encoded block atlas that SECTION_MESH UVs refer to. */
public record BlockAtlas(int width, int height, int format, byte[] data) {
	public static final int PNG = 1;
	private static final int HEADER_BYTES = 13;

	public byte[] encode() {
		byte[] head = new PayloadWriter().u32(Integer.toUnsignedLong(width)).u32(Integer.toUnsignedLong(height)).u8(format)
				.u32(data.length).toByteArray();
		byte[] out = Arrays.copyOf(head, head.length + data.length);
		System.arraycopy(data, 0, out, head.length, data.length);
		return out;
	}

	public static BlockAtlas decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int w = (int) r.u32();
		int h = (int) r.u32();
		int format = r.u8();
		long n = r.u32();
		if (n > payload.length - HEADER_BYTES) {
			throw new ProtocolException("payload truncated");
		}
		return new BlockAtlas(w, h, format, Arrays.copyOfRange(payload, HEADER_BYTES, HEADER_BYTES + (int) n));
	}

	@Override
	public boolean equals(Object o) {
		return o instanceof BlockAtlas b && width == b.width && height == b.height && format == b.format
				&& Arrays.equals(data, b.data);
	}

	@Override
	public int hashCode() {
		return Arrays.hashCode(data) * 31 + width * 7 + height * 13 + format;
	}
}
