package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;
import java.util.ArrayList;
import java.util.List;

/**
 * 0x01C0 TREES (host to guest), minor 12: every tree the host draws in one collision region. Replaces the guest's list
 * for that region. kind: 0 oak, 1 spruce, 2 birch, 3 jungle, 4 acacia, 5 dark oak, 6 bush (leaves only).
 */
public record Trees(int epoch, int regionX, int regionZ, List<Tree> trees) {
	public static final int MAX_COUNT = 4096;
	private static final int HEADER_BYTES = 14;
	private static final int TREE_BYTES = 25;

	/** A tree: trunk base in the Minecraft frame, height and radius in metres. */
	public record Tree(int id, float x, float y, float z, float height, float radius, int kind) {
	}

	public byte[] encode() {
		if (trees.size() > MAX_COUNT) {
			throw new IllegalArgumentException(trees.size() + " trees, above " + MAX_COUNT);
		}
		PayloadWriter w = new PayloadWriter().u32(Integer.toUnsignedLong(epoch)).i32(regionX).i32(regionZ).u16(trees.size());
		for (Tree t : trees) {
			w.u32(Integer.toUnsignedLong(t.id())).f32(t.x()).f32(t.y()).f32(t.z()).f32(t.height()).f32(t.radius()).u8(t.kind());
		}
		return w.toByteArray();
	}

	public static Trees decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int epoch = (int) r.u32(), rx = r.i32(), rz = r.i32(), n = r.u16();
		if (n > MAX_COUNT) {
			throw new ProtocolException("tree count " + n + " above " + MAX_COUNT);
		}
		if (payload.length != HEADER_BYTES + n * TREE_BYTES) {
			throw new ProtocolException("trees payload is " + payload.length + " bytes, expected " + (HEADER_BYTES + n * TREE_BYTES));
		}
		List<Tree> out = new ArrayList<>(n);
		for (int i = 0; i < n; i++) {
			out.add(new Tree((int) r.u32(), r.f32(), r.f32(), r.f32(), r.f32(), r.f32(), r.u8()));
		}
		return new Trees(epoch, rx, rz, out);
	}
}
