package dev.mcskylines.protocol;

/** 0x0133, guest to host: drop every section. No fields. */
public record SectionsClear() {
	public byte[] encode() {
		return new byte[0];
	}

	public static SectionsClear decode(byte[] payload) {
		return new SectionsClear();
	}
}
