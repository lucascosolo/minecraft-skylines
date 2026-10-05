package dev.mcskylines.bridge;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.CharacterCodingException;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.util.UUID;

/** Reads bridge primitives; a payload that ends early is a ProtocolException, trailing bytes are ignored. */
public final class PayloadReader {
	private final ByteBuffer buf;

	public PayloadReader(byte[] payload) {
		buf = ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN);
	}

	private void need(int n) throws ProtocolException {
		if (buf.remaining() < n) {
			throw new ProtocolException("payload truncated");
		}
	}

	public int u8() throws ProtocolException {
		need(1);
		return Byte.toUnsignedInt(buf.get());
	}

	public int u16() throws ProtocolException {
		need(2);
		return Short.toUnsignedInt(buf.getShort());
	}

	public long u32() throws ProtocolException {
		need(4);
		return Integer.toUnsignedLong(buf.getInt());
	}

	public int i32() throws ProtocolException {
		need(4);
		return buf.getInt();
	}

	/** u64 or i64: the raw 64 bits; read a u64 with Long's unsigned helpers. */
	public long u64() throws ProtocolException {
		need(8);
		return buf.getLong();
	}

	public float f32() throws ProtocolException {
		need(4);
		return buf.getFloat();
	}

	public double f64() throws ProtocolException {
		need(8);
		return buf.getDouble();
	}

	public boolean bool() throws ProtocolException {
		int v = u8();
		if (v > 1) {
			throw new ProtocolException("bool out of range: " + v);
		}
		return v == 1;
	}

	public String string() throws ProtocolException {
		int len = u16();
		need(len);
		ByteBuffer slice = buf.slice(buf.position(), len);
		buf.position(buf.position() + len);
		try {
			return StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT)
				.onUnmappableCharacter(CodingErrorAction.REPORT).decode(slice).toString();
		} catch (CharacterCodingException e) {
			throw new ProtocolException("string is not valid UTF-8");
		}
	}

	public UUID uuid() throws ProtocolException {
		need(16);
		ByteBuffer be = buf.slice(buf.position(), 16).order(ByteOrder.BIG_ENDIAN);
		buf.position(buf.position() + 16);
		return new UUID(be.getLong(), be.getLong());
	}
}
