package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;
import java.util.ArrayList;
import java.util.List;

/** 0x01E0 ENTITY_MODEL (guest to host), minor 14: a tree of parts with textured quads, 1/16 m model units. */
public record EntityModel(int modelId, String name, List<Part> parts) {
	public static final int MAX_PARTS = 1024;
	public static final int MAX_QUADS = 4096;
	public static final int FLOATS_PER_QUAD = 23;
	public static final int NO_PARENT = 0xFFFF;

	/** parent: an earlier part's index or NO_PARENT; quads: 4 x (x, y, z, u, v) then nx, ny, nz per quad. */
	public record Part(int parent, float[] quads) {
	}

	public byte[] encode() {
		if (parts.size() > MAX_PARTS) {
			throw new IllegalArgumentException(parts.size() + " parts, above " + MAX_PARTS);
		}
		PayloadWriter w = new PayloadWriter().u32(Integer.toUnsignedLong(modelId)).string(name).u16(parts.size());
		for (Part p : parts) {
			if (p.quads().length % FLOATS_PER_QUAD != 0) {
				throw new IllegalArgumentException("quads are not a multiple of " + FLOATS_PER_QUAD + " floats");
			}
			int quads = p.quads().length / FLOATS_PER_QUAD;
			if (quads > MAX_QUADS) {
				throw new IllegalArgumentException(quads + " quads, above " + MAX_QUADS);
			}
			w.u16(p.parent()).u16(quads);
			for (float f : p.quads()) {
				w.f32(f);
			}
		}
		return w.toByteArray();
	}

	public static EntityModel decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int id = (int) r.u32();
		String name = r.string();
		int n = r.u16();
		if (n > MAX_PARTS) {
			throw new ProtocolException("part count " + n + " above " + MAX_PARTS);
		}
		List<Part> parts = new ArrayList<>(n);
		for (int i = 0; i < n; i++) {
			int parent = r.u16();
			if (parent != NO_PARENT && parent >= i) {
				throw new ProtocolException("part " + i + " has parent " + parent + ", which does not come before it");
			}
			int quads = r.u16();
			if (quads > MAX_QUADS) {
				throw new ProtocolException("quad count " + quads + " above " + MAX_QUADS);
			}
			float[] q = new float[quads * FLOATS_PER_QUAD];
			for (int j = 0; j < q.length; j++) {
				q[j] = r.f32();
			}
			parts.add(new Part(parent, q));
		}
		return new EntityModel(id, name, parts);
	}
}
