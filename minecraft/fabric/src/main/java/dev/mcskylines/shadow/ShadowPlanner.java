package dev.mcskylines.shadow;

/** Turns one column's host surfaces into shadow cells: ground, paving under roads, invisible fill in buildings, grass. */
public final class ShadowPlanner {
	public static final int CRUST = 6;
	public static final int BUILDING_FILL = 4;
	public static final String PAVED = "minecraft:gray_concrete";
	public static final String SOLID = "minecraft:barrier";
	private static final double STEEP_NY = 0.6;
	private static final double EMBANKMENT = 3;

	private ShadowPlanner() {
	}

	/** Lowest cell filled until the player digs: a crust of {@link #CRUST} cells under the ground. */
	public static int defaultFloor(ShadowColumn.Sample s) {
		if (Double.isNaN(s.terrain())) {
			return Integer.MAX_VALUE;
		}
		return Math.max(ShadowMaterials.BOTTOM_Y, ShadowColumn.solidTop(s.terrain()) - CRUST + 1);
	}

	public static void column(long seed, ShadowColumn.Sample s, int x, int z, boolean waterNear, int floorY, CellSink sink) {
		if (Double.isNaN(s.terrain())) {
			return;
		}
		int tTop = ShadowColumn.solidTop(s.terrain());
		ShadowMaterials.Top top = s.terrainNy() < STEEP_NY ? ShadowMaterials.Top.STONE
			: waterNear ? ShadowMaterials.Top.SAND : ShadowMaterials.Top.GRASS;
		boolean road = !Double.isNaN(s.road()) && s.road() > s.terrain() - 0.25;
		boolean bridge = road && s.road() - s.terrain() > EMBANKMENT;
		int rTop = road ? ShadowColumn.solidTop(s.road()) : Integer.MIN_VALUE;
		int pavedFrom = !road ? Integer.MAX_VALUE : bridge ? rTop : tTop;
		int low = Math.max(floorY, ShadowMaterials.BOTTOM_Y);
		for (int y = low; y <= tTop; y++) {
			if (y < pavedFrom || y > rTop) {
				sink.accept(x, y, z, ShadowMaterials.ground(seed, x, y, z, tTop, top));
			}
		}
		if (road) {
			for (int y = Math.max(pavedFrom, low); y <= rTop; y++) {
				sink.accept(x, y, z, PAVED);
			}
		}
		double ground = road ? Math.max(s.terrain(), s.road()) : s.terrain();
		boolean building = !Double.isNaN(s.building()) && s.building() > ground;
		if (building) {
			int base = ShadowColumn.solidTop(ground) + 1;
			int end = Math.min(ShadowColumn.solidTop(s.building()), base + BUILDING_FILL - 1);
			for (int y = base; y <= end; y++) {
				sink.accept(x, y, z, SOLID);
			}
		}
		if (top == ShadowMaterials.Top.GRASS && !road && !building) {
			String plant = ShadowMaterials.plant(seed, x, z);
			if ("minecraft:tall_grass".equals(plant)) {
				sink.accept(x, tTop + 1, z, "minecraft:tall_grass[half=lower]");
				sink.accept(x, tTop + 2, z, "minecraft:tall_grass[half=upper]");
			} else if (plant != null) {
				sink.accept(x, tTop + 1, z, plant);
			}
		}
	}
}
