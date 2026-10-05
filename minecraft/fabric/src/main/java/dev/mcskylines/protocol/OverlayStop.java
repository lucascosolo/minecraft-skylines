package dev.mcskylines.protocol;

/** 0x0142, guest to host: the guest stopped publishing the overlay. No fields. */
public record OverlayStop() {
	public byte[] encode() {
		return new byte[0];
	}

	public static OverlayStop decode(byte[] payload) {
		return new OverlayStop();
	}
}
