package dev.mcskylines.protocol;

/** protocol/minecraft-skylines-v1.md. */
public final class AppProtocol {
	public static final String NAME = "minecraft-skylines";
	public static final int MAJOR = 1;
	public static final int MINOR = 21;
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
	public static final int TIME_SET = 0x0161; // minor 16: a time command moves the city's clock (guest to host)
	public static final int DYNAMIC_OBSTACLES = 0x0170;
	/** Host to guest (minor 17): obstacles with turn rate and height profile. */
	public static final int SHAPED_OBSTACLES = 0x0171;
	public static final int LIGHT_SOURCES = 0x0180;
	public static final int SKY_STATE = 0x0190;
	public static final int SKY_TEXTURES = 0x0191;
	public static final int WATER_SURFACE = 0x01A0;
	public static final int PLAYER_DATA = 0x01B0; // minor 11: the joiner's saved player data (guest to host, host to guest)
	public static final int RESPAWN_REQUEST = 0x01B1; // minor 11: a dead joiner without a spawn asks the host for one
	public static final int TREES = 0x01C0; // minor 12: the trees the host draws in a collision region (host to guest)
	public static final int TREE_FELLED = 0x01C1; // minor 12: the player felled the tree placed for an id (guest to host)
	public static final int TREE_GROWN = 0x01C2; // minor 15: a sapling grew into a tree (guest to host)
	public static final int CITIZEN_EVENTS = 0x01D2; // minor 19: mobs panicked, killed or converted citizens (guest to host)
	public static final int CITY_CONDITIONS = 0x0210; // minor 21: the city's problems and resources around the simulated area (host to guest)
	public static final int ORE_MINED = 0x0211; // minor 21: ore the player broke, per resource cell (guest to host)
	// minor 14: entities (13 is reserved for another branch)
	public static final int ENTITY_MODEL = 0x01E0; // a model of textured box parts (guest to host)
	public static final int ENTITY_TEXTURE = 0x01E1; // a PNG texture (guest to host)
	public static final int ENTITY_STATES = 0x01E2; // the complete set of entities and their pose (guest to host)
	public static final int CITY_ENTITIES = 0x01D0; // minor 18: the city's entities (both directions)
	public static final int CITY_FOCUS = 0x01D1; // minor 18: where the city view's camera looks (host to guest)
	public static final int DEBUG_COMMAND = 0x01F0;

	private AppProtocol() {
	}
}
