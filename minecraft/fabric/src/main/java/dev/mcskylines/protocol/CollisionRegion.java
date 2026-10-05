package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;
import java.util.Arrays;

/**
 * 0x0113, host to guest. {@code vertices} holds ax ay az bx by bz cx cy cz per triangle (9 floats each),
 * {@code flags} one u16 per triangle.
 */
public record CollisionRegion(int epoch, int regionX, int regionZ, float[] vertices, short[] flags) {
	public static final int TERRAIN = 1;
	public static final int ROAD_SURFACE = 2;
	public static final int BRIDGE_DECK = 4;
	public static final int BUILDING = 8;
	private static final int BYTES_PER_TRIANGLE = 9 * 4 + 2;
	private static final int HEADER_BYTES = 16;

	public int triangleCount() {
		return flags.length;
	}

	public byte[] encode() {
		if (vertices.length != 9 * flags.length) {
			throw new IllegalStateException("vertices must hold 9 floats per flags entry");
		}
		PayloadWriter w = new PayloadWriter().u32(Integer.toUnsignedLong(epoch)).i32(regionX).i32(regionZ)
				.u32(flags.length);
		for (int t = 0; t < flags.length; t++) {
			for (int i = 0; i < 9; i++) {
				w.f32(vertices[9 * t + i]);
			}
			w.u16(Short.toUnsignedInt(flags[t]));
		}
		return w.toByteArray();
	}

	public static CollisionRegion decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int epoch = (int) r.u32();
		int rx = r.i32();
		int rz = r.i32();
		long n = r.u32();
		if (n > (payload.length - HEADER_BYTES) / BYTES_PER_TRIANGLE) {
			throw new ProtocolException("payload truncated");
		}
		float[] vertices = new float[9 * (int) n];
		short[] flags = new short[(int) n];
		for (int t = 0; t < flags.length; t++) {
			for (int i = 0; i < 9; i++) {
				vertices[9 * t + i] = r.f32();
			}
			flags[t] = (short) r.u16();
		}
		return new CollisionRegion(epoch, rx, rz, vertices, flags);
	}

	@Override
	public boolean equals(Object o) {
		return o instanceof CollisionRegion c && epoch == c.epoch && regionX == c.regionX && regionZ == c.regionZ
				&& Arrays.equals(vertices, c.vertices) && Arrays.equals(flags, c.flags);
	}

	@Override
	public int hashCode() {
		return Arrays.hashCode(vertices) * 31 + Arrays.hashCode(flags) + epoch + regionX * 7 + regionZ * 13;
	}
}
