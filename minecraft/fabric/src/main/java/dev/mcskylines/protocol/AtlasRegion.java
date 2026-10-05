package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;
import java.util.Arrays;

/** 0x0131, guest to host: the current frame of an animated sprite, RGBA8 top row first. */
public record AtlasRegion(int x, int y, int width, int height, byte[] rgba) {
	private static final int HEADER_BYTES = 16;

	public byte[] encode() {
		if ((long) width * height * 4 != rgba.length) {
			throw new IllegalStateException("rgba must hold width*height*4 bytes");
		}
		byte[] head = new PayloadWriter().u32(Integer.toUnsignedLong(x)).u32(Integer.toUnsignedLong(y))
				.u32(Integer.toUnsignedLong(width)).u32(Integer.toUnsignedLong(height)).toByteArray();
		byte[] out = Arrays.copyOf(head, HEADER_BYTES + rgba.length);
		System.arraycopy(rgba, 0, out, HEADER_BYTES, rgba.length);
		return out;
	}

	public static AtlasRegion decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		long x = r.u32(), y = r.u32(), w = r.u32(), h = r.u32();
		if (w * h * 4 > payload.length - HEADER_BYTES) {
			throw new ProtocolException("payload truncated");
		}
		return new AtlasRegion((int) x, (int) y, (int) w, (int) h,
				Arrays.copyOfRange(payload, HEADER_BYTES, HEADER_BYTES + (int) (w * h * 4)));
	}

	@Override
	public boolean equals(Object o) {
		return o instanceof AtlasRegion a && x == a.x && y == a.y && width == a.width && height == a.height
				&& Arrays.equals(rgba, a.rgba);
	}

	@Override
	public int hashCode() {
		return Arrays.hashCode(rgba) * 31 + x * 7 + y * 13 + width * 17 + height;
	}
}
