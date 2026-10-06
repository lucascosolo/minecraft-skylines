package dev.mcskylines.protocol;

/** protocol/minecraft-skylines-v1.md. */
public final class AppProtocol {
	public static final String NAME = "minecraft-skylines";
	public static final int MAJOR = 1;
	public static final int MINOR = 7;
	public static final int HOST_STATUS = 0x0100;
	public static final int GUEST_STATUS = 0x0101;
	public static final int ENTER_PLAYER_MODE = 0x0110;
	public static final int EXIT_PLAYER_MODE = 0x0111;
	public static final int INPUT = 0x0112;
	public static final int COLLISION_REGION = 0x0113;
	public static final int COLLISION_RESET = 0x0114;
	public static final int PLAYER_STATE = 0x0120;
	public static final int BLOCK_ATLAS = 0x0130;
	public static final int ATLAS_REGION = 0x0131;
	public static final int SECTION_MESH = 0x0132;
	public static final int SECTIONS_CLEAR = 0x0133;
	public static final int BLOCK_SELECTION = 0x0134;
	public static final int VIEWPORT = 0x0140;
	public static final int OVERLAY_OFFER = 0x0141;
	public static final int OVERLAY_STOP = 0x0142;
	public static final int CITY_OPEN = 0x0150;
	public static final int BLOCK_EDITS = 0x0151;
	public static final int CITY_CLOSE = 0x0152;
	public static final int EDIT_SYNC = 0x0153;
	public static final int EDIT_SYNC_ACK = 0x0154;
	public static final int CITY_STATE = 0x0155;
	public static final int WORLD_TIME = 0x0160;
	public static final int DYNAMIC_OBSTACLES = 0x0170;
	public static final int DEBUG_COMMAND = 0x01F0;

	private AppProtocol() {
	}
}
