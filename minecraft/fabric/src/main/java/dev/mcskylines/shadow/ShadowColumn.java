package dev.mcskylines.shadow;

import dev.mcskylines.collision.SkyTri;
import java.util.List;

/** The host surfaces over one block column, sampled with a vertical ray through the streamed triangles. */
public final class ShadowColumn {
	private static final int ROAD = SkyTri.ROAD_SURFACE | SkyTri.BRIDGE_DECK;

	private ShadowColumn() {
	}

	/** Highest terrain, road (or bridge deck) and building surface at the column; NaN where absent. */
	public record Sample(double terrain, double road, double building, double terrainNy) {
	}

	public static Sample sample(List<SkyTri> tris, double x, double z) {
		double terrain = Double.NaN, road = Double.NaN, building = Double.NaN, ny = Double.NaN;
		for (SkyTri t : tris) {
			if ((t.flags & (SkyTri.TERRAIN | SkyTri.DUG_SURFACE | ROAD | SkyTri.BUILDING)) == 0) {
				continue;
			}
			double h = t.heightAt(x, z);
			if (Double.isNaN(h)) {
				continue;
			}
			if ((t.flags & (SkyTri.TERRAIN | SkyTri.DUG_SURFACE)) != 0 && !(h <= terrain)) {
				terrain = h;
				ny = Math.abs(t.ny);
			}
			if ((t.flags & ROAD) != 0 && !(h <= road)) {
				road = h;
			}
			if ((t.flags & SkyTri.BUILDING) != 0 && !(h <= building)) {
				building = h;
			}
		}
		return new Sample(terrain, road, building, ny);
	}

	/** Highest cell whose centre lies strictly below the surface: cells count as solid when their centre is under it. */
	public static int solidTop(double surface) {
		return (int) Math.ceil(surface - 0.5) - 1;
	}
}
