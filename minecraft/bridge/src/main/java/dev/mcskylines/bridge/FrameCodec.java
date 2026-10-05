package dev.mcskylines.bridge;

import java.io.EOFException;
import java.io.IOException;
import java.io.InputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;

/** The 8-byte SKBR header: u32 payloadLength, u16 type, u16 flags (little-endian). */
public final class FrameCodec {
	public static final int HEADER_SIZE = 8;
	public static final int MAX_PAYLOAD = 16 * 1024 * 1024;
	public static final int APP_MIN = 0x0100;

	private FrameCodec() {
	}

	public static byte[] encode(int type, byte[] payload) {
		return ByteBuffer.allocate(HEADER_SIZE + payload.length).order(ByteOrder.LITTLE_ENDIAN)
			.putInt(payload.length).putShort((short) type).putShort((short) 0).put(payload).array();
	}

	public static Frame decode(byte[] bytes) throws ProtocolException {
		if (bytes.length < HEADER_SIZE) {
			throw new ProtocolException("frame shorter than its header");
		}
		Header h = header(bytes);
		if (bytes.length - HEADER_SIZE < h.length) {
			throw new ProtocolException("frame shorter than its payloadLength");
		}
		byte[] payload = new byte[(int) h.length];
		System.arraycopy(bytes, HEADER_SIZE, payload, 0, payload.length);
		return new Frame(h.type, 0, payload);
	}

	/** Reads one frame; returns null on a clean end of stream at a frame boundary. */
	public static Frame read(InputStream in) throws IOException {
		byte[] head = new byte[HEADER_SIZE];
		int n = in.readNBytes(head, 0, HEADER_SIZE);
		if (n == 0) {
			return null;
		}
		if (n < HEADER_SIZE) {
			throw new EOFException("connection closed inside a frame header");
		}
		Header h = header(head);
		byte[] payload = in.readNBytes((int) h.length);
		if (payload.length < h.length) {
			throw new EOFException("connection closed inside a frame payload");
		}
		return new Frame(h.type, 0, payload);
	}

	private record Header(long length, int type) {
	}

	private static Header header(byte[] b) throws ProtocolException {
		ByteBuffer buf = ByteBuffer.wrap(b, 0, HEADER_SIZE).order(ByteOrder.LITTLE_ENDIAN);
		long length = Integer.toUnsignedLong(buf.getInt());
		int type = Short.toUnsignedInt(buf.getShort());
		int flags = Short.toUnsignedInt(buf.getShort());
		if (flags != 0) {
			throw new ProtocolException("non-zero flags 0x" + Integer.toHexString(flags));
		}
		if (length > MAX_PAYLOAD) {
			throw new ProtocolException("payloadLength " + length + " exceeds 16 MiB");
		}
		if (type < APP_MIN && (type < Hello.TYPE || type > Goodbye.TYPE)) {
			throw new ProtocolException("unknown bridge frame type 0x" + Integer.toHexString(type));
		}
		return new Header(length, type);
	}
}
