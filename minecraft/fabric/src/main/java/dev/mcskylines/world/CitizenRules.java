package dev.mcskylines.world;

/** Per-city rules for citizens as villagers (protocol 1.19), saved in the city's player blob. Both on by default. */
public record CitizenRules(boolean citizens, boolean conversion) {
	public static final CitizenRules DEFAULT = new CitizenRules(true, true);

	/** Proxies exist only while a Minecraft-enabled city is open and ready, the host speaks minor 19, and the rule is on. */
	public boolean proxiesWanted(boolean cityReady, int appMinor) {
		return citizens && cityReady && appMinor >= 19;
	}
}
