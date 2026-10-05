package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;
import java.util.Arrays;

/** 0x0112, host to guest, once per host frame in player mode. */
public record Input(float yaw, float pitch, Event[] events) {
	public static final int KEY = 1;
	public static final int MOUSE_BUTTON = 2;
	public static final int SCROLL = 3;
	public static final int TEXT = 4;
	public static final int RELEASE_ALL = 5;

	/** {@code action}: 1 press, 0 release. {@code code}: key, button, scroll notches x 120 or code point. */
	public record Event(int kind, int action, int code) {
	}

	public byte[] encode() {
		PayloadWriter w = new PayloadWriter().f32(yaw).f32(pitch).u16(events.length);
		for (Event e : events) {
			w.u8(e.kind).u8(e.action).i32(e.code);
		}
		return w.toByteArray();
	}

	public static Input decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		float yaw = r.f32();
		float pitch = r.f32();
		Event[] events = new Event[r.u16()];
		for (int i = 0; i < events.length; i++) {
			events[i] = new Event(r.u8(), r.u8(), r.i32());
		}
		return new Input(yaw, pitch, events);
	}

	@Override
	public boolean equals(Object o) {
		return o instanceof Input i && yaw == i.yaw && pitch == i.pitch && Arrays.equals(events, i.events);
	}

	@Override
	public int hashCode() {
		return 31 * Arrays.hashCode(events) + Float.hashCode(yaw) + Float.hashCode(pitch);
	}
}
