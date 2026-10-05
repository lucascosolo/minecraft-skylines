package dev.mcskylines.protocol;

/** protocol/minecraft-skylines-v1.md. */
public final class AppProtocol {
	public static final String NAME = "minecraft-skylines";
	public static final int MAJOR = 1;
	public static final int MINOR = 0;
	public static final int HOST_STATUS = 0x0100;
	public static final int GUEST_STATUS = 0x0101;

	private AppProtocol() {
	}
}
