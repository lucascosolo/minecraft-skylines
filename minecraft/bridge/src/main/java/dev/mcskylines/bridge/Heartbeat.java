package dev.mcskylines.bridge;

public record Heartbeat(long seq, long senderUptimeMs) {
	public static final int TYPE = 0x0003;

	public byte[] encode() {
		return new PayloadWriter().u32(seq).u64(senderUptimeMs).toByteArray();
	}

	public static Heartbeat decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		return new Heartbeat(r.u32(), r.u64());
	}
}
