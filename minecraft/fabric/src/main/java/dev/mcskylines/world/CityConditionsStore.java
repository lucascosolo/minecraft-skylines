package dev.mcskylines.world;

import dev.mcskylines.protocol.CityConditions;
import dev.mcskylines.shadow.ShadowMaterials;
import dev.mcskylines.shadow.ShadowWorld;
import java.util.List;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Protocol 1.21: the newest CITY_CONDITIONS per resource cell for the current open. The shadow world's resources are
 * frozen at each cell's first report in an open (mining must not reshuffle the ores around the player); CS1's later
 * depletion shows from the next open. Written on the client thread, read on the server thread.
 */
public final class CityConditionsStore {
	private static final ConcurrentHashMap<Integer, CityConditions.Cell> LATEST = new ConcurrentHashMap<>();
	private static final ConcurrentHashMap<Integer, ShadowMaterials.Resources> FROZEN = new ConcurrentHashMap<>();
	private static volatile int openSeq;
	private static volatile List<CityConditions.Fire> fires = List.of();

	private CityConditionsStore() {
	}

	public static void accept(CityConditions m) {
		if (m.openSeq() != openSeq) {
			clear();
			openSeq = m.openSeq();
		}
		for (CityConditions.Cell c : m.cells()) {
			int k = c.cx() << 9 | c.cz();
			LATEST.put(k, c);
			if (FROZEN.putIfAbsent(k, new ShadowMaterials.Resources(c.ore(), c.oil(), c.fertility(), c.worked())) == null) {
				// Minecraft x of cell cx spans (cx - 256) * 33.75 ..; z is flipped: cell cz spans -(cz - 255) * 33.75 ..
				double x0 = (c.cx() - 256) * CityHazards.CELL, z1 = -(c.cz() - 256) * CityHazards.CELL;
				ShadowWorld.areaChanged(x0, z1 - CityHazards.CELL, x0 + CityHazards.CELL, z1);
			}
		}
		fires = m.fires();
	}

	public static void clear() {
		LATEST.clear();
		FROZEN.clear();
		fires = List.of();
	}

	/** Conditions for {@code seq} only: another open's are stale. */
	public static boolean currentFor(int seq) {
		return seq == openSeq && !LATEST.isEmpty();
	}

	/** The cell at Minecraft x, z, or null when the host has not reported it. */
	public static CityConditions.Cell at(double x, double z) {
		return LATEST.get(CityHazards.cellX(x) << 9 | CityHazards.cellZ(z));
	}

	public static ShadowMaterials.Resources resources(int x, int z) {
		return FROZEN.getOrDefault(CityHazards.cellX(x + 0.5) << 9 | CityHazards.cellZ(z + 0.5), ShadowMaterials.Resources.NONE);
	}

	public static double growthFactor(double x, double z) {
		CityConditions.Cell c = at(x, z);
		return c == null ? 1.0 : CityHazards.growthFactor(c.pollution(), c.fertility());
	}

	public static List<CityConditions.Fire> fires() {
		return fires;
	}
}
