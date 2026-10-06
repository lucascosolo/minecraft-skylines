package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;

/** 0x0151, both directions (minor 5): block edits with a per-batch palette of block-state strings. */
public record BlockEdits(int openSeq, int flags, List<String> palette, List<Edit> edits) {
	public static final int LAST = 1;
	public static final int MAX_EDITS = 65536;

	public record Edit(int x, int y, int z, int state) {
	}

	public BlockEdits {
		palette = List.copyOf(palette);
		edits = List.copyOf(edits);
	}

	public boolean last() {
		return (flags & LAST) != 0;
	}

	public byte[] encode() {
		String problem = problem(palette, edits.size());
		if (problem == null) {
			for (Edit e : edits) {
				if (e.state < 0 || e.state >= palette.size()) {
					problem = "state index " + e.state + " out of range";
					break;
				}
			}
		}
		if (problem != null) {
			throw new IllegalStateException(problem);
		}
		PayloadWriter w = new PayloadWriter().u32(Integer.toUnsignedLong(openSeq)).u8(flags).u16(palette.size());
		for (String s : palette) {
			w.string(s);
		}
		w.u32(edits.size());
		for (Edit e : edits) {
			w.i32(e.x).i32(e.y).i32(e.z).u16(e.state);
		}
		return w.toByteArray();
	}

	public static BlockEdits decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int openSeq = (int) r.u32();
		int flags = r.u8();
		int paletteCount = r.u16();
		List<String> palette = new ArrayList<>(paletteCount);
		for (int i = 0; i < paletteCount; i++) {
			palette.add(r.string());
		}
		long count = r.u32();
		String problem = problem(palette, count);
		if (problem != null) {
			throw new ProtocolException(problem);
		}
		List<Edit> edits = new ArrayList<>((int) count);
		for (long i = 0; i < count; i++) {
			Edit e = new Edit(r.i32(), r.i32(), r.i32(), r.u16());
			if (e.state >= paletteCount) {
				throw new ProtocolException("state index " + e.state + " >= paletteCount " + paletteCount);
			}
			edits.add(e);
		}
		return new BlockEdits(openSeq, flags, palette, edits);
	}

	private static String problem(List<String> palette, long editCount) {
		if (palette.size() > 0xFFFF) {
			return "palette longer than 65535";
		}
		if (new HashSet<>(palette).size() != palette.size()) {
			return "palette has duplicates";
		}
		if (editCount > MAX_EDITS) {
			return "editCount " + editCount + " > " + MAX_EDITS;
		}
		return null;
	}
}
