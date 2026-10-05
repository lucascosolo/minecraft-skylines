package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x0120, guest to host, once per guest render frame. {@code flags}, {@code teleportAck}, {@code tickSeq} are u32. */
public record PlayerState(int flags, int teleportAck, double x, double y, double z, double eyeX, double eyeY,
		double eyeZ, float yaw, float pitch, float fovDeg, int tickSeq, double prevX, double prevY, double prevZ,
		double curX, double curY, double curZ, float prevEyeHeight, float curEyeHeight, float partialTick,
		float tickMs) {
	public static final int IN_WORLD = 1;
	public static final int ON_GROUND = 1 << 1;
	public static final int SNEAKING = 1 << 2;
	public static final int SPRINTING = 1 << 3;
	public static final int SWIMMING = 1 << 4;
	public static final int FLYING = 1 << 5;
	public static final int DEAD = 1 << 6;
	public static final int HELD = 1 << 7;

	public boolean has(int flag) {
		return (flags & flag) == flag;
	}

	public byte[] encode() {
		return new PayloadWriter().u32(Integer.toUnsignedLong(flags)).u32(Integer.toUnsignedLong(teleportAck))
				.f64(x).f64(y).f64(z).f64(eyeX).f64(eyeY).f64(eyeZ).f32(yaw).f32(pitch).f32(fovDeg)
				.u32(Integer.toUnsignedLong(tickSeq)).f64(prevX).f64(prevY).f64(prevZ).f64(curX).f64(curY).f64(curZ)
				.f32(prevEyeHeight).f32(curEyeHeight).f32(partialTick).f32(tickMs).toByteArray();
	}

	public static PlayerState decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		return new PlayerState((int) r.u32(), (int) r.u32(), r.f64(), r.f64(), r.f64(), r.f64(), r.f64(), r.f64(),
				r.f32(), r.f32(), r.f32(), (int) r.u32(), r.f64(), r.f64(), r.f64(), r.f64(), r.f64(), r.f64(),
				r.f32(), r.f32(), r.f32(), r.f32());
	}
}
