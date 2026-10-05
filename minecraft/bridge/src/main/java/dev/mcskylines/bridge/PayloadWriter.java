package dev.mcskylines.bridge;

import java.io.ByteArrayOutputStream;
import java.nio.charset.StandardCharsets;
import java.util.UUID;

public final class PayloadWriter {
	private final ByteArrayOutputStream out = new ByteArrayOutputStream();

	public PayloadWriter u8(int v) {
		out.write(v);
		return this;
	}

	public PayloadWriter u16(int v) {
		return le(v, 2);
	}

	public PayloadWriter u32(long v) {
		return le(v, 4);
	}

	public PayloadWriter i32(int v) {
		return le(v, 4);
	}

	public PayloadWriter u64(long v) {
		return le(v, 8);
	}

	public PayloadWriter f32(float v) {
		return le(Float.floatToIntBits(v), 4);
	}

	public PayloadWriter f64(double v) {
		return le(Double.doubleToLongBits(v), 8);
	}

	public PayloadWriter bool(boolean v) {
		return u8(v ? 1 : 0);
	}

	public PayloadWriter string(String s) {
		byte[] b = s.getBytes(StandardCharsets.UTF_8);
		if (b.length > 0xFFFF) {
			throw new IllegalArgumentException("string longer than 65535 UTF-8 bytes");
		}
		u16(b.length);
		out.writeBytes(b);
		return this;
	}

	public PayloadWriter uuid(UUID u) {
		be(u.getMostSignificantBits());
		be(u.getLeastSignificantBits());
		return this;
	}

	public byte[] toByteArray() {
		return out.toByteArray();
	}

	private PayloadWriter le(long v, int bytes) {
		for (int i = 0; i < bytes; i++) {
			out.write((int) (v >>> (8 * i)));
		}
		return this;
	}

	private void be(long v) {
		for (int i = 7; i >= 0; i--) {
			out.write((int) (v >>> (8 * i)));
		}
	}
}
