package dev.mcskylines.render;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.Arrays;

/** A growable SECTION_MESH vertex array in wire layout (32 bytes per vertex, little-endian). No Minecraft types. */
public final class MeshVertices {
	private ByteBuffer buf = ByteBuffer.allocate(1 << 16).order(ByteOrder.LITTLE_ENDIAN);
	private int vertices;

	public void reset() {
		buf.clear();
		vertices = 0;
	}

	public int vertexCount() {
		return vertices;
	}

	/** {@code argb} is Minecraft ARGB, written as bytes R,G,B,A; {@code packedLight} is Minecraft's packed light coords. */
	public void put(float x, float y, float z, float u, float v, int argb, int packedLight, int flags) {
		if (buf.remaining() < 32) {
			buf = ByteBuffer.wrap(Arrays.copyOf(buf.array(), buf.capacity() * 2)).order(ByteOrder.LITTLE_ENDIAN).position(buf.position());
		}
		buf.putFloat(x).putFloat(y).putFloat(z).putFloat(u).putFloat(v);
		buf.put((byte) (argb >> 16)).put((byte) (argb >> 8)).put((byte) argb).put((byte) (argb >>> 24));
		buf.putInt(((packedLight >> 4) & 0xF) | ((packedLight >> 20) & 0xF) << 8);
		buf.putInt(flags);
		vertices++;
	}

	public byte[] toBytes() {
		return Arrays.copyOf(buf.array(), vertices * 32);
	}

	/** Takes Minecraft's fixed per-face brightness back out of a colour, leaving tint and ambient occlusion. */
	public static int unshade(int argb, float shade) {
		if (shade >= 0.999F || shade <= 0.0F) {
			return argb;
		}
		int r = Math.min(255, Math.round(((argb >> 16) & 0xFF) / shade));
		int g = Math.min(255, Math.round(((argb >> 8) & 0xFF) / shade));
		int b = Math.min(255, Math.round((argb & 0xFF) / shade));
		return (argb & 0xFF000000) | (r << 16) | (g << 8) | b;
	}
}
