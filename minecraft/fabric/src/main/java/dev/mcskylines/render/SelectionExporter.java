package dev.mcskylines.render;

import dev.mcskylines.bridge.BridgeGuest;
import dev.mcskylines.collision.SkyClip;
import dev.mcskylines.protocol.AppProtocol;
import dev.mcskylines.protocol.BlockSelection;
import net.minecraft.client.Minecraft;
import net.minecraft.core.BlockPos;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.HitResult;
import net.minecraft.world.phys.shapes.VoxelShape;

/** Sends BLOCK_SELECTION whenever the outlined box changes (minor 4). */
public final class SelectionExporter {
	private final BridgeGuest guest;
	private BlockSelection sent;

	public SelectionExporter(BridgeGuest guest) {
		this.guest = guest;
	}

	public void linkUp() {
		sent = null;
	}

	public void frame(Minecraft mc, boolean playerMode) {
		BlockSelection now = playerMode ? current(mc) : BlockSelection.HIDDEN;
		if (!now.equals(sent) && guest.sendLatest(AppProtocol.BLOCK_SELECTION, now.encode())) {
			sent = now;
		}
	}

	/** The targeted block's shape bounds, or the virtual cell behind a city surface, or hidden. */
	static BlockSelection current(Minecraft mc) {
		if (mc.level == null || mc.gui.screen() != null || !(mc.hitResult instanceof BlockHitResult hit) || hit.getType() != HitResult.Type.BLOCK) {
			return BlockSelection.HIDDEN;
		}
		if (hit instanceof SkyClip.HostHitResult host) {
			BlockPos p = host.outlinePos;
			return new BlockSelection(true, p.getX(), p.getY(), p.getZ(), p.getX() + 1, p.getY() + 1, p.getZ() + 1, BlockSelection.KIND_PLACEMENT);
		}
		BlockPos pos = hit.getBlockPos();
		VoxelShape shape = mc.level.getBlockState(pos).getShape(mc.level, pos);
		if (shape.isEmpty()) {
			return BlockSelection.HIDDEN;
		}
		AABB b = shape.bounds().move(pos);
		return new BlockSelection(true, (float) b.minX, (float) b.minY, (float) b.minZ, (float) b.maxX, (float) b.maxY, (float) b.maxZ,
			BlockSelection.KIND_BLOCK);
	}
}
