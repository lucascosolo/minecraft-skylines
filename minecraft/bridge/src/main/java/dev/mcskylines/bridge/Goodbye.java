package dev.mcskylines.bridge;

public record Goodbye(int code, String reason) {
	public static final int TYPE = 0x0004;
	public static final int NORMAL = 0;
	public static final int SHUTTING_DOWN = 1;
	public static final int PROTOCOL_ERROR = 2;
	public static final int TIMEOUT = 3;
	public static final int BACKPRESSURE = 4;

	public byte[] encode() {
		return new PayloadWriter().u16(code).string(reason).toByteArray();
	}

	public static Goodbye decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		return new Goodbye(r.u16(), r.string());
	}
}
