package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x01C2 TREE_GROWN (guest to host), minor 15: a sapling the player placed grew into a tree of the given kind (0..6). */
public record TreeGrown(int openSeq, float x, float y, float z, int kind, int seed) {
	public byte[] encode() {
		return new PayloadWriter().u32(Integer.toUnsignedLong(openSeq)).f32(x).f32(y).f32(z).u8(kind)
			.u32(Integer.toUnsignedLong(seed)).toByteArray();
	}

	public static TreeGrown decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int openSeq = (int) r.u32();
		float x = r.f32();
		float y = r.f32();
		float z = r.f32();
		int kind = r.u8();
		int seed = (int) r.u32();
		if (kind > 6) throw new ProtocolException("tree kind " + kind + " is above 6");
		return new TreeGrown(openSeq, x, y, z, kind, seed);
	}
}
