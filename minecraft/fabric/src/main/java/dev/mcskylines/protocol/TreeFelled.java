package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x01C1 TREE_FELLED (guest to host), minor 12: the player broke every log of the tree placed for treeId. */
public record TreeFelled(int openSeq, int treeId) {
	public byte[] encode() {
		return new PayloadWriter().u32(Integer.toUnsignedLong(openSeq)).u32(Integer.toUnsignedLong(treeId)).toByteArray();
	}

	public static TreeFelled decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		return new TreeFelled((int) r.u32(), (int) r.u32());
	}
}
