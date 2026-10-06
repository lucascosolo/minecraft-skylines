package dev.mcskylines.collision;

import dev.mcskylines.protocol.CollisionRegion;
import dev.mcskylines.protocol.CollisionReset;
import java.util.List;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Collision triangles streamed from the host, one list per 16x16 column region. Written from the client
 * thread (link events), read from the client thread (collision mixin); safe from any thread.
 */
public final class CollisionStore {
	public static final CollisionStore INSTANCE = new CollisionStore();
	private static final int REGION_SIZE = 16;
	private static final SkyTri[] NONE = new SkyTri[0];

	private final ConcurrentHashMap<Long, SkyTri[]> regions = new ConcurrentHashMap<>();
	private final Object writeLock = new Object();
	private volatile int epoch;
	/** Furthest any stored triangle reaches outside its own region; widens queries so none is missed. */
	private volatile double overhang;

	public static long key(int regionX, int regionZ) {
		return ((long) regionX << 32) | (regionZ & 0xFFFFFFFFL);
	}

	/** Replaces the region's triangles. Returns false when the region is older than the current epoch. */
	public boolean accept(CollisionRegion region) {
		SkyTri[] tris = new SkyTri[region.triangleCount()];
		double reach = 0;
		double minX = region.regionX() * (double) REGION_SIZE, minZ = region.regionZ() * (double) REGION_SIZE;
		for (int i = 0; i < tris.length; i++) {
			SkyTri t = new SkyTri(region.vertices(), 9 * i, Short.toUnsignedInt(region.flags()[i]));
			tris[i] = t;
			reach = Math.max(reach, Math.max(Math.max(minX - t.minX, t.maxX - (minX + REGION_SIZE)),
					Math.max(minZ - t.minZ, t.maxZ - (minZ + REGION_SIZE))));
		}
		synchronized (writeLock) {
			if (Integer.compareUnsigned(region.epoch(), epoch) < 0) {
				return false;
			}
			regions.put(key(region.regionX(), region.regionZ()), tris.length == 0 ? NONE : tris);
			overhang = Math.max(overhang, reach);
			return true;
		}
	}

	/** Drops every region and accepts only regions with epoch at or above {@code reset.epoch()} from now on. */
	public void accept(CollisionReset reset) {
		synchronized (writeLock) {
			epoch = reset.epoch();
			regions.clear();
			overhang = 0;
		}
	}

	public int epoch() {
		return epoch;
	}

	/** Keys ({@link #key}) of every region held now. */
	public java.util.Set<Long> regionKeys() {
		return java.util.Set.copyOf(regions.keySet());
	}

	public int regionCount() {
		return regions.size();
	}

	public boolean isLoaded(int regionX, int regionZ) {
		return regions.containsKey(key(regionX, regionZ));
	}

	/** True when every region within {@code radiusRegions} (square) of the block position (x, z) is loaded. */
	public boolean regionsLoadedAround(double x, double z, int radiusRegions) {
		int cx = Math.floorDiv((int) Math.floor(x), REGION_SIZE), cz = Math.floorDiv((int) Math.floor(z), REGION_SIZE);
		for (int rx = cx - radiusRegions; rx <= cx + radiusRegions; rx++) {
			for (int rz = cz - radiusRegions; rz <= cz + radiusRegions; rz++) {
				if (!isLoaded(rx, rz)) {
					return false;
				}
			}
		}
		return true;
	}

	/** Adds every triangle whose bounds overlap the box. */
	public void trianglesNear(double minX, double minY, double minZ, double maxX, double maxY, double maxZ, List<SkyTri> out) {
		if (regions.isEmpty()) {
			return;
		}
		double m = overhang;
		int rx0 = Math.floorDiv((int) Math.floor(minX - m), REGION_SIZE), rx1 = Math.floorDiv((int) Math.floor(maxX + m), REGION_SIZE);
		int rz0 = Math.floorDiv((int) Math.floor(minZ - m), REGION_SIZE), rz1 = Math.floorDiv((int) Math.floor(maxZ + m), REGION_SIZE);
		for (int rx = rx0; rx <= rx1; rx++) {
			for (int rz = rz0; rz <= rz1; rz++) {
				SkyTri[] tris = regions.get(key(rx, rz));
				if (tris == null) {
					continue;
				}
				for (SkyTri t : tris) {
					if (t.maxX >= minX && t.minX <= maxX && t.maxY >= minY && t.minY <= maxY && t.maxZ >= minZ && t.minZ <= maxZ) {
						out.add(t);
					}
				}
			}
		}
	}
}
