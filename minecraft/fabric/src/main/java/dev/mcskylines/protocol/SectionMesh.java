package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.Arrays;

/**
 * 0x0132, guest to host: one section's triangle list. {@code vertices} is the wire vertex array, {@link #VERTEX_BYTES}
 * per vertex little-endian: f32 x y z u v, u32 color (bytes R,G,B,A), u32 light, u32 flags.
 */
public record SectionMesh(int sx, int sy, int sz, byte[] vertices) {
	public static final int VERTEX_BYTES = 32;
	public static final int CUTOUT = 1;
	public static final int TRANSLUCENT = 2;
	private static final int HEADER_BYTES = 16;

	public SectionMesh {
		if (vertices.length % (3 * VERTEX_BYTES) != 0) {
			throw new IllegalArgumentException("vertices must hold whole triangles of " + VERTEX_BYTES + "-byte vertices");
		}
	}

	public static SectionMesh empty(int sx, int sy, int sz) {
		return new SectionMesh(sx, sy, sz, new byte[0]);
	}

	public int vertexCount() {
		return vertices.length / VERTEX_BYTES;
	}

	private ByteBuffer le() {
		return ByteBuffer.wrap(vertices).order(ByteOrder.LITTLE_ENDIAN);
	}

	public float x(int i) {
		return le().getFloat(i * VERTEX_BYTES);
	}

	public float y(int i) {
		return le().getFloat(i * VERTEX_BYTES + 4);
	}

	public float z(int i) {
		return le().getFloat(i * VERTEX_BYTES + 8);
	}

	public float u(int i) {
		return le().getFloat(i * VERTEX_BYTES + 12);
	}

	public float v(int i) {
		return le().getFloat(i * VERTEX_BYTES + 16);
	}

	public int color(int i) {
		return le().getInt(i * VERTEX_BYTES + 20);
	}

	public int light(int i) {
		return le().getInt(i * VERTEX_BYTES + 24);
	}

	public int flags(int i) {
		return le().getInt(i * VERTEX_BYTES + 28);
	}

	public byte[] encode() {
		byte[] head = new PayloadWriter().i32(sx).i32(sy).i32(sz).u32(vertexCount()).toByteArray();
		byte[] out = Arrays.copyOf(head, HEADER_BYTES + vertices.length);
		System.arraycopy(vertices, 0, out, HEADER_BYTES, vertices.length);
		return out;
	}

	public static SectionMesh decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int sx = r.i32(), sy = r.i32(), sz = r.i32();
		long n = r.u32();
		if (n % 3 != 0) {
			throw new ProtocolException("vertex count not a multiple of 3");
		}
		if (n > (payload.length - HEADER_BYTES) / VERTEX_BYTES) {
			throw new ProtocolException("payload truncated");
		}
		return new SectionMesh(sx, sy, sz, Arrays.copyOfRange(payload, HEADER_BYTES, HEADER_BYTES + (int) n * VERTEX_BYTES));
	}

	@Override
	public boolean equals(Object o) {
		return o instanceof SectionMesh m && sx == m.sx && sy == m.sy && sz == m.sz && Arrays.equals(vertices, m.vertices);
	}

	@Override
	public int hashCode() {
		return Arrays.hashCode(vertices) * 31 + sx * 7 + sy * 13 + sz * 17;
	}
}
