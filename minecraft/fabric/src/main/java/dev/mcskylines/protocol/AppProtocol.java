package dev.mcskylines.protocol;

/** protocol/minecraft-skylines-v1.md. */
public final class AppProtocol {
	public static final String NAME = "minecraft-skylines";
	public static final int MAJOR = 1;
	public static final int MINOR = 1;
	public static final int HOST_STATUS = 0x0100;
	public static final int GUEST_STATUS = 0x0101;
	public static final int ENTER_PLAYER_MODE = 0x0110;
	public static final int EXIT_PLAYER_MODE = 0x0111;
	public static final int INPUT = 0x0112;
	public static final int COLLISION_REGION = 0x0113;
	public static final int COLLISION_RESET = 0x0114;
	public static final int PLAYER_STATE = 0x0120;

	private AppProtocol() {
	}
}
