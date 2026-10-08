package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x0200 SHOP_OPEN (guest to host), minor 20: the player used a trading building; eye and hit in Minecraft coordinates. */
public record ShopOpen(int openSeq, int requestId, float eyeX, float eyeY, float eyeZ, float hitX, float hitY, float hitZ) {
	public byte[] encode() {
		return new PayloadWriter().u32(Integer.toUnsignedLong(openSeq)).u32(Integer.toUnsignedLong(requestId))
			.f32(eyeX).f32(eyeY).f32(eyeZ).f32(hitX).f32(hitY).f32(hitZ).toByteArray();
	}

	public static ShopOpen decode(byte[] payload) throws ProtocolException {
		if (payload.length != 32) throw new ProtocolException("shop open payload is " + payload.length + " bytes, not 32");
		PayloadReader r = new PayloadReader(payload);
		ShopOpen m = new ShopOpen((int) r.u32(), (int) r.u32(), r.f32(), r.f32(), r.f32(), r.f32(), r.f32(), r.f32());
		for (float f : new float[] {m.eyeX, m.eyeY, m.eyeZ, m.hitX, m.hitY, m.hitZ}) {
			if (!Float.isFinite(f)) throw new ProtocolException("shop open has a non-finite coordinate");
		}
		return m;
	}
}
