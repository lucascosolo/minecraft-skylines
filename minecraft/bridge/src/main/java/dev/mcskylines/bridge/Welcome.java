package dev.mcskylines.bridge;

public record Welcome(boolean accepted, int rejectCode, String rejectReason, int bridgeVersion, String appProtocol,
		int appMajor, int appMinor, String peerName, String peerVersion, long heartbeatIntervalMs, long peerTimeoutMs,
		long sessionId) {
	public static final int TYPE = 0x0002;
	public static final int REJECT_BRIDGE_VERSION = 1;
	public static final int REJECT_APP_PROTOCOL = 2;
	public static final int REJECT_APP_MAJOR = 3;
	public static final int REJECT_BUSY = 4;
	public static final int REJECT_NOT_READY = 5;

	/** Rejections for 1-3 mean retrying cannot succeed. */
	public boolean rejectionIsFinal() {
		return !accepted && rejectCode >= REJECT_BRIDGE_VERSION && rejectCode <= REJECT_APP_MAJOR;
	}

	public byte[] encode() {
		return new PayloadWriter().bool(accepted).u16(rejectCode).string(rejectReason).u16(bridgeVersion)
			.string(appProtocol).u16(appMajor).u16(appMinor).string(peerName).string(peerVersion)
			.u32(heartbeatIntervalMs).u32(peerTimeoutMs).u64(sessionId).toByteArray();
	}

	public static Welcome decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		return new Welcome(r.bool(), r.u16(), r.string(), r.u16(), r.string(), r.u16(), r.u16(), r.string(),
			r.string(), r.u32(), r.u32(), r.u64());
	}
}
