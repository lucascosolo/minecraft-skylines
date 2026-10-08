package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

import java.util.ArrayList;
import java.util.List;

/** 0x0211 ORE_MINED (guest to host), minor 21: shadow ore blocks the player broke, per CS1 resource cell. */
public record OreMined(int openSeq, List<Entry> entries) {
	public static final int ORE = 1;
	public static final int OIL = 2;
	public static final int MAX_ENTRIES = 256;
	private static final int GRID_MAX = 511;

	public record Entry(int resource, int cx, int cz, int blocks) {
	}

	public byte[] encode() {
		PayloadWriter w = new PayloadWriter().u32(Integer.toUnsignedLong(openSeq)).u16(entries.size());
		for (Entry e : entries) {
			w.u8(e.resource()).u16(e.cx()).u16(e.cz()).u16(e.blocks());
		}
		return w.toByteArray();
	}

	public static OreMined decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int openSeq = (int) r.u32();
		int n = r.u16();
		if (n > MAX_ENTRIES) throw new ProtocolException("ore entry count " + n + " is above " + MAX_ENTRIES);
		List<Entry> entries = new ArrayList<>(n);
		for (int i = 0; i < n; i++) {
			Entry e = new Entry(r.u8(), r.u16(), r.u16(), r.u16());
			if (e.resource() != ORE && e.resource() != OIL) throw new ProtocolException("ore resource " + e.resource() + " is outside 1..2");
			if (e.cx() > GRID_MAX || e.cz() > GRID_MAX) throw new ProtocolException("ore cell " + e.cx() + "," + e.cz() + " is outside 0..511");
			if (e.blocks() == 0) throw new ProtocolException("ore entry with 0 blocks");
			entries.add(e);
		}
		if (payload.length != 6 + 7 * n) throw new ProtocolException("ore mined payload longer than its count says");
		return new OreMined(openSeq, List.copyOf(entries));
	}
}
