package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;
import java.util.ArrayList;
import java.util.List;

/**
 * 0x0170 DYNAMIC_OBSTACLES (minor 7) and 0x0171 SHAPED_OBSTACLES (minor 17), host to guest: every vehicle and citizen
 * near the player as upright boxes; replaces the previous set. The shaped form adds turn rate and height profile.
 */
public record DynamicObstacles(List<Obstacle> obstacles) {
	public static final int VEHICLE = 1;
	public static final int CITIZEN = 2;

	/**
	 * Centre (Minecraft coordinates), yaw of the length axis, half extents, velocity (m/s), turn rate (deg/s) and profile
	 * (slice tops in 1/255 of the height, from the -length end; empty for none); {@code id} is the u32 bit pattern.
	 */
	public record Obstacle(int kind, int id, float x, float y, float z, float yaw, float halfWidth, float halfHeight,
			float halfLength, float vx, float vy, float vz, float yawRate, byte[] profile) {
		public Obstacle(int kind, int id, float x, float y, float z, float yaw, float halfWidth, float halfHeight,
				float halfLength, float vx, float vy, float vz) {
			this(kind, id, x, y, z, yaw, halfWidth, halfHeight, halfLength, vx, vy, vz, 0f, new byte[0]);
		}
	}

	public byte[] encode() {
		return write(false);
	}

	public byte[] encodeShaped() {
		return write(true);
	}

	private byte[] write(boolean shaped) {
		PayloadWriter w = new PayloadWriter().u16(obstacles.size());
		for (Obstacle o : obstacles) {
			w.u8(o.kind()).u32(Integer.toUnsignedLong(o.id())).f32(o.x()).f32(o.y()).f32(o.z()).f32(o.yaw())
					.f32(o.halfWidth()).f32(o.halfHeight()).f32(o.halfLength()).f32(o.vx()).f32(o.vy()).f32(o.vz());
			if (shaped) {
				byte[] p = o.profile() == null ? new byte[0] : o.profile();
				int n = Math.min(p.length, 255);
				w.f32(o.yawRate()).u8(n);
				for (int i = 0; i < n; i++) {
					w.u8(p[i] & 0xFF);
				}
			}
		}
		return w.toByteArray();
	}

	public static DynamicObstacles decode(byte[] payload) throws ProtocolException {
		return read(payload, false);
	}

	public static DynamicObstacles decodeShaped(byte[] payload) throws ProtocolException {
		return read(payload, true);
	}

	private static DynamicObstacles read(byte[] payload, boolean shaped) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int n = r.u16();
		List<Obstacle> list = new ArrayList<>(n);
		for (int i = 0; i < n; i++) {
			int kind = r.u8(), id = (int) r.u32();
			float x = r.f32(), y = r.f32(), z = r.f32(), yaw = r.f32(), hw = r.f32(), hh = r.f32(), hl = r.f32();
			float vx = r.f32(), vy = r.f32(), vz = r.f32();
			float yawRate = 0;
			byte[] profile = new byte[0];
			if (shaped) {
				yawRate = r.f32();
				profile = new byte[r.u8()];
				for (int k = 0; k < profile.length; k++) {
					profile[k] = (byte) r.u8();
				}
			}
			list.add(new Obstacle(kind, id, x, y, z, yaw, hw, hh, hl, vx, vy, vz, yawRate, profile));
		}
		return new DynamicObstacles(List.copyOf(list));
	}
}
