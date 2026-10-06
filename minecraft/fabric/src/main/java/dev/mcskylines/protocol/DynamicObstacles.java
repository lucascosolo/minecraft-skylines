package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;
import java.util.ArrayList;
import java.util.List;

/** 0x0170, host to guest (minor 7): every vehicle and citizen near the player as upright boxes; replaces the previous set. */
public record DynamicObstacles(List<Obstacle> obstacles) {
	public static final int VEHICLE = 1;
	public static final int CITIZEN = 2;

	/** Centre (Minecraft coordinates), yaw of the length axis, half extents and velocity (m/s); {@code id} is the u32 bit pattern. */
	public record Obstacle(int kind, int id, float x, float y, float z, float yaw, float halfWidth, float halfHeight,
			float halfLength, float vx, float vy, float vz) {
	}

	public byte[] encode() {
		PayloadWriter w = new PayloadWriter().u16(obstacles.size());
		for (Obstacle o : obstacles) {
			w.u8(o.kind()).u32(Integer.toUnsignedLong(o.id())).f32(o.x()).f32(o.y()).f32(o.z()).f32(o.yaw())
					.f32(o.halfWidth()).f32(o.halfHeight()).f32(o.halfLength()).f32(o.vx()).f32(o.vy()).f32(o.vz());
		}
		return w.toByteArray();
	}

	public static DynamicObstacles decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int n = r.u16();
		List<Obstacle> list = new ArrayList<>(n);
		for (int i = 0; i < n; i++) {
			list.add(new Obstacle(r.u8(), (int) r.u32(), r.f32(), r.f32(), r.f32(), r.f32(), r.f32(), r.f32(), r.f32(),
					r.f32(), r.f32(), r.f32()));
		}
		return new DynamicObstacles(List.copyOf(list));
	}
}
