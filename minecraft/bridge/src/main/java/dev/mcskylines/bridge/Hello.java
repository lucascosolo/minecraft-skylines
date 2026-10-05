package dev.mcskylines.bridge;

public record Hello(int magic, int bridgeVersion, String appProtocol, int appMajor, int appMinor,
		String peerName, String peerVersion, long sessionNonce) {
	public static final int TYPE = 0x0001;
	public static final int MAGIC = 0x52424B53;
	public static final int BRIDGE_VERSION = 1;

	public byte[] encode() {
		return new PayloadWriter().u32(Integer.toUnsignedLong(magic)).u16(bridgeVersion).string(appProtocol)
			.u16(appMajor).u16(appMinor).string(peerName).string(peerVersion).u64(sessionNonce).toByteArray();
	}

	public static Hello decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		return new Hello((int) r.u32(), r.u16(), r.string(), r.u16(), r.u16(), r.string(), r.string(), r.u64());
	}
}
