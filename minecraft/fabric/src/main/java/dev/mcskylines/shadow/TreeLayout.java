package dev.mcskylines.shadow;

/**
 * The Minecraft side of a CS1 tree: a column of logs scaled to the tree's height, standing where CS1 draws its trunk.
 * There are no leaf blocks (owner, 2026-10-06: "i should be able to just punch the normal CS1 tree model and it can be
 * wood blocks making up the trunk on the minecraft side, roughly calculated by the height of the tree in CS1, but it
 * shouldn't pop up leaves blocks"); the canopy CS1 draws counts only for the felled tree's leaf loot ({@link #leafCount}).
 */
public final class TreeLayout {
	public static final int BUSH = 6;
	private static final String[] WOOD = {"oak", "spruce", "birch", "jungle", "acacia", "dark_oak"};

	private TreeLayout() {
	}

	public static int trunkHeight(double height) {
		return (int) Math.max(2, Math.min(24, Math.round(height * 0.6)));
	}

	public static boolean isBush(int kind, double height) {
		return kind == BUSH || height < 2.5;
	}

	static String wood(int kind, double height) {
		return isBush(kind, height) || kind < 0 || kind >= WOOD.length ? "oak" : WOOD[kind];
	}

	/** The leaf block whose loot a felled tree of this kind yields, once per {@link #leafCount} cell. */
	public static String leaves(int kind, double height) {
		return "minecraft:" + wood(kind, height) + "_leaves[distance=1,persistent=false]";
	}

	/** Logs of the trunk: one for a bush, otherwise {@link #trunkHeight}, from the cell above the ground's solid top. */
	public static void blocks(int kind, double x, double y, double z, double height, double radius, CellSink sink) {
		int bx = (int) Math.floor(x), bz = (int) Math.floor(z), y0 = ShadowColumn.solidTop(y) + 1;
		int trunk = isBush(kind, height) ? 1 : trunkHeight(height);
		String log = "minecraft:" + wood(kind, height) + "_log[axis=y]";
		for (int i = 0; i < trunk; i++) {
			sink.accept(bx, y0 + i, bz, log);
		}
	}

	/** How many leaf cells a canopy the size of the CS1 tree's would hold (an ellipsoid of the tree's radius). */
	public static int leafCount(int kind, double height, double radius) {
		boolean bush = isBush(kind, height);
		int trunk = bush ? 0 : trunkHeight(height);
		double r = Math.max(1, Math.min(8, radius));
		int top = bush ? Math.max(0, (int) Math.ceil(height) - 1) : Math.min((int) Math.ceil(height), trunk + 1);
		double ry = bush ? Math.max(0.5, height / 2) : Math.max(1.5, Math.min(r, top * 0.5));
		double cy = bush ? Math.max(0, height / 2 - 0.5) : top - ry + 0.5;
		int ri = (int) Math.ceil(r), n = 0;
		for (int ly = Math.max(0, (int) Math.floor(cy - ry)); ly <= top; ly++) {
			double dy = (ly - cy) / ry;
			for (int dx = -ri; dx <= ri; dx++) {
				for (int dz = -ri; dz <= ri; dz++) {
					boolean log = dx == 0 && dz == 0 && ly < trunk;
					if (!log && (dx * dx + dz * dz) / (r * r) + dy * dy <= 1.0001) {
						n++;
					}
				}
			}
		}
		return n;
	}
}
