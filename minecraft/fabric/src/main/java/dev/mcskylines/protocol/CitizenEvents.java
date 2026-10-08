package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

import java.util.ArrayList;
import java.util.List;

/** 0x01D2 CITIZEN_EVENTS (guest to host), minor 19: mobs panicked, killed or converted citizens' villager proxies. */
public record CitizenEvents(int openSeq, List<Event> events) {
	public static final int PANIC = 1;
	public static final int KILLED = 2;
	public static final int CONVERTED = 3;
	public static final int MAX_EVENTS = 1024;

	/** {@code id}: the citizen's obstacle id (u32 bit pattern); x, y, z: the proxy's feet, Minecraft frame. */
	public record Event(int kind, int id, float x, float y, float z) {
	}

	public byte[] encode() {
		PayloadWriter w = new PayloadWriter().u32(Integer.toUnsignedLong(openSeq)).u16(events.size());
		for (Event e : events) {
			w.u8(e.kind()).u32(Integer.toUnsignedLong(e.id())).f32(e.x()).f32(e.y()).f32(e.z());
		}
		return w.toByteArray();
	}

	public static CitizenEvents decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int openSeq = (int) r.u32();
		int n = r.u16();
		if (n > MAX_EVENTS) throw new ProtocolException("citizen event count " + n + " is above " + MAX_EVENTS);
		List<Event> events = new ArrayList<>(n);
		for (int i = 0; i < n; i++) {
			Event e = new Event(r.u8(), (int) r.u32(), r.f32(), r.f32(), r.f32());
			if (e.kind() < PANIC || e.kind() > CONVERTED) throw new ProtocolException("citizen event kind " + e.kind() + " is outside 1..3");
			events.add(e);
		}
		if (payload.length != 6 + 17 * n) throw new ProtocolException("citizen events payload longer than its count says");
		return new CitizenEvents(openSeq, List.copyOf(events));
	}
}
