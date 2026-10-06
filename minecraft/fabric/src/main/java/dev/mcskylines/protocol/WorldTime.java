package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x0160, host to guest (minor 6): the city's time of day, which the guest shows exactly. */
public record WorldTime(float hour, int day, int flags) {
	/** Flags bit 0: the city has a day/night cycle; when clear the guest shows midday. */
	public static final int DAY_NIGHT = 1;

	public byte[] encode() {
		return new PayloadWriter().f32(hour).u32(Integer.toUnsignedLong(day)).u8(flags).toByteArray();
	}

	public static WorldTime decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		return new WorldTime(r.f32(), (int) r.u32(), r.u8());
	}

	/** Ticks into Minecraft's day (0 = 06:00, 6000 = noon) for a city hour. */
	public static int minecraftDayTicks(float hour) {
		double h = (((hour - 6.0) % 24.0) + 24.0) % 24.0;
		return (int) (h * 1000.0) % 24000;
	}

	/** The overworld clock's total ticks for this time: midday when the city has no day/night cycle. */
	public long totalTicks() {
		int inDay = (flags & DAY_NIGHT) != 0 ? minecraftDayTicks(hour) : 6000;
		return Integer.toUnsignedLong(day) * 24000L + inDay;
	}
}
