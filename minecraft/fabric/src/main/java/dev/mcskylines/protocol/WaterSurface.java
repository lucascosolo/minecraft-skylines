package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/**
 * 0x01A0, host to guest (minor 10): the water surface and the ground under it (Minecraft y) over the block columns
 * around the player; column (originX + dx, originZ + dz) is index dz * size + dx. Replaces the previous grid.
 */
public record WaterSurface(int originX, int originZ, int size, float[] surface, float[] bottom) {
	public static final int MAX_SIZE = 128;

	public byte[] encode() {
		PayloadWriter w = new PayloadWriter().i32(originX).i32(originZ).u16(size);
		for (int i = 0; i < size * size; i++) {
			w.f32(surface[i]).f32(bottom[i]);
		}
		return w.toByteArray();
	}

	public static WaterSurface decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int ox = r.i32(), oz = r.i32(), size = r.u16();
		if (size > MAX_SIZE) {
			throw new ProtocolException("water grid size " + size + " above " + MAX_SIZE);
		}
		float[] surface = new float[size * size], bottom = new float[size * size];
		for (int i = 0; i < surface.length; i++) {
			surface[i] = r.f32();
			bottom[i] = r.f32();
		}
		return new WaterSurface(ox, oz, size, surface, bottom);
	}
}
