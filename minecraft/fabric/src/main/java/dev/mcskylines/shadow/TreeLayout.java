package dev.mcskylines.shadow;

/** A Minecraft tree standing where CS1 draws one: a log trunk and a leaf canopy scaled to the CS1 tree. */
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

	public static void blocks(int kind, double x, double y, double z, double height, double radius, CellSink sink) {
		int bx = (int) Math.floor(x), bz = (int) Math.floor(z), y0 = ShadowColumn.solidTop(y) + 1;
		boolean bush = isBush(kind, height);
		String wood = bush || kind < 0 || kind >= WOOD.length ? "oak" : WOOD[kind];
		String leaves = "minecraft:" + wood + "_leaves[distance=1,persistent=false]";
		int trunk = bush ? 0 : trunkHeight(height);
		for (int i = 0; i < trunk; i++) {
			sink.accept(bx, y0 + i, bz, "minecraft:" + wood + "_log[axis=y]");
		}
		double r = Math.max(1, Math.min(8, radius));
		int top = bush ? y0 + Math.max(0, (int) Math.ceil(height) - 1) : Math.min(y0 + (int) Math.ceil(height), y0 + trunk + 1);
		double ry = bush ? Math.max(0.5, height / 2) : Math.max(1.5, Math.min(r, (top - y0) * 0.5));
		double cy = bush ? y0 + Math.max(0, height / 2 - 0.5) : top - ry + 0.5;
		int ri = (int) Math.ceil(r);
		for (int ly = Math.max(y0, (int) Math.floor(cy - ry)); ly <= top; ly++) {
			double dy = (ly - cy) / ry;
			for (int dx = -ri; dx <= ri; dx++) {
				for (int dz = -ri; dz <= ri; dz++) {
					boolean log = dx == 0 && dz == 0 && ly < y0 + trunk;
					if (!log && (dx * dx + dz * dz) / (r * r) + dy * dy <= 1.0001) {
						sink.accept(bx + dx, ly, bz + dz, leaves);
					}
				}
			}
		}
	}
}
