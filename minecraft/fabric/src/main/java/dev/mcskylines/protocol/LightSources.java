package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;
import java.util.ArrayList;
import java.util.List;

/** 0x0180, host to guest (minor 8): the lamps near the player as light blocks to place; replaces the previous set. */
public record LightSources(List<Light> lights) {
	public static final int MAX_LEVEL = 15;

	/** Block position (Minecraft coordinates) and light level 1..15. */
	public record Light(int x, int y, int z, int level) {
	}

	public byte[] encode() {
		PayloadWriter w = new PayloadWriter().u16(lights.size());
		for (Light l : lights) {
			w.i32(l.x()).i32(l.y()).i32(l.z()).u8(l.level());
		}
		return w.toByteArray();
	}

	public static LightSources decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int n = r.u16();
		List<Light> list = new ArrayList<>(n);
		for (int i = 0; i < n; i++) {
			Light l = new Light(r.i32(), r.i32(), r.i32(), r.u8());
			if (l.level() < 1 || l.level() > MAX_LEVEL) {
				throw new ProtocolException("light level out of range: " + l.level());
			}
			list.add(l);
		}
		return new LightSources(List.copyOf(list));
	}
}
