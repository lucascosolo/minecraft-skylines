package dev.mcskylines.protocol;

import dev.mcskylines.bridge.ProtocolException;
import java.io.ByteArrayOutputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayList;
import java.util.List;

/** 0x0191, guest to host (minor 9): the sun, moon phase and cloud images; replaces the previous set. */
public record SkyTextures(List<Texture> textures) {
	public static final int SUN = 0;
	public static final int MOON = 1;
	public static final int CLOUDS = 2;
	public static final int PNG = 1;

	/** kind, moon phase (0 for other kinds), format, encoded image. */
	public record Texture(int kind, int phase, int format, byte[] data) {
	}

	public byte[] encode() {
		ByteArrayOutputStream out = new ByteArrayOutputStream();
		out.write(textures.size());
		for (Texture t : textures) {
			ByteBuffer head = ByteBuffer.allocate(7).order(ByteOrder.LITTLE_ENDIAN);
			head.put((byte) t.kind()).put((byte) t.phase()).put((byte) t.format()).putInt(t.data().length);
			out.writeBytes(head.array());
			out.writeBytes(t.data());
		}
		return out.toByteArray();
	}

	public static SkyTextures decode(byte[] payload) throws ProtocolException {
		ByteBuffer b = ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN);
		int n = take(b, 1).get() & 0xFF;
		List<Texture> list = new ArrayList<>(n);
		for (int i = 0; i < n; i++) {
			ByteBuffer head = take(b, 7);
			int kind = head.get() & 0xFF, phase = head.get() & 0xFF, format = head.get() & 0xFF;
			if (kind == MOON && phase >= SkyState.MOON_PHASES) {
				throw new ProtocolException("moon phase out of range: " + phase);
			}
			long len = Integer.toUnsignedLong(head.getInt());
			if (len > b.remaining()) {
				throw new ProtocolException("payload truncated");
			}
			byte[] data = new byte[(int) len];
			b.get(data);
			list.add(new Texture(kind, phase, format, data));
		}
		return new SkyTextures(List.copyOf(list));
	}

	private static ByteBuffer take(ByteBuffer b, int n) throws ProtocolException {
		if (b.remaining() < n) {
			throw new ProtocolException("payload truncated");
		}
		ByteBuffer slice = b.slice(b.position(), n).order(ByteOrder.LITTLE_ENDIAN);
		b.position(b.position() + n);
		return slice;
	}
}
