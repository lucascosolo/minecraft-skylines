package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x0110, host to guest. {@code teleportSeq} and {@code collisionEpoch} are u32 held in an int. */
public record EnterPlayerMode(int teleportSeq, double x, double y, double z, float yaw, float pitch,
		int collisionEpoch) {
	public byte[] encode() {
		return new PayloadWriter().u32(Integer.toUnsignedLong(teleportSeq)).f64(x).f64(y).f64(z).f32(yaw).f32(pitch)
				.u32(Integer.toUnsignedLong(collisionEpoch)).toByteArray();
	}

	public static EnterPlayerMode decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		return new EnterPlayerMode((int) r.u32(), r.f64(), r.f64(), r.f64(), r.f32(), r.f32(), (int) r.u32());
	}
}
