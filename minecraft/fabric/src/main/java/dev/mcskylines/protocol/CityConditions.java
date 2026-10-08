package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

import java.util.ArrayList;
import java.util.List;

/** 0x0210 CITY_CONDITIONS (host to guest), minor 21: CS1 resource cells and burning buildings around the simulated area. */
public record CityConditions(int openSeq, List<Cell> cells, List<Fire> fires) {
	public static final int MAX_CELLS = 256;
	public static final int MAX_FIRES = 256;
	public static final int WORKED = 1;
	private static final int GRID_MAX = 511;

	/** One natural resource cell (33.75 m); bytes as CS1 keeps them, crime 0..100, dead = buildings waiting for a hearse. */
	public record Cell(int cx, int cz, int ore, int oil, int fertility, int forest, int pollution, int flags, int crime, int dead) {
		public boolean worked() {
			return (flags & WORKED) != 0;
		}
	}

	/** A burning building: position (Minecraft frame), footprint half-diagonal, fire intensity 1..255. */
	public record Fire(float x, float y, float z, float radius, int intensity) {
	}

	public byte[] encode() {
		PayloadWriter w = new PayloadWriter().u32(Integer.toUnsignedLong(openSeq)).u16(cells.size());
		for (Cell c : cells) {
			w.u16(c.cx()).u16(c.cz()).u8(c.ore()).u8(c.oil()).u8(c.fertility()).u8(c.forest()).u8(c.pollution()).u8(c.flags())
				.u8(c.crime()).u8(c.dead());
		}
		w.u16(fires.size());
		for (Fire f : fires) {
			w.f32(f.x()).f32(f.y()).f32(f.z()).f32(f.radius()).u8(f.intensity());
		}
		return w.toByteArray();
	}

	public static CityConditions decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int openSeq = (int) r.u32();
		int n = r.u16();
		if (n > MAX_CELLS) throw new ProtocolException("condition cell count " + n + " is above " + MAX_CELLS);
		List<Cell> cells = new ArrayList<>(n);
		for (int i = 0; i < n; i++) {
			Cell c = new Cell(r.u16(), r.u16(), r.u8(), r.u8(), r.u8(), r.u8(), r.u8(), r.u8(), r.u8(), r.u8());
			if (c.cx() > GRID_MAX || c.cz() > GRID_MAX) throw new ProtocolException("condition cell " + c.cx() + "," + c.cz() + " is outside 0..511");
			cells.add(c);
		}
		int m = r.u16();
		if (m > MAX_FIRES) throw new ProtocolException("fire count " + m + " is above " + MAX_FIRES);
		List<Fire> fires = new ArrayList<>(m);
		for (int i = 0; i < m; i++) {
			Fire f = new Fire(r.f32(), r.f32(), r.f32(), r.f32(), r.u8());
			if (!Float.isFinite(f.x()) || !Float.isFinite(f.y()) || !Float.isFinite(f.z()) || !Float.isFinite(f.radius()) || f.radius() < 0) {
				throw new ProtocolException("fire position or radius not finite, or radius below 0");
			}
			if (f.intensity() == 0) throw new ProtocolException("fire intensity 0");
			fires.add(f);
		}
		if (payload.length != 8 + 12 * n + 17 * m) throw new ProtocolException("city conditions payload longer than its counts say");
		return new CityConditions(openSeq, List.copyOf(cells), List.copyOf(fires));
	}
}
