package dev.mcskylines.mixin;

import dev.mcskylines.collision.CollisionStore;
import dev.mcskylines.shadow.ShadowCells;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.ai.control.MoveControl;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.shapes.Shapes;
import net.minecraft.world.phys.shapes.VoxelShape;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

/**
 * MoveControl jumps whenever the block at the mob's own position has a collision shape whose top is above the mob's feet.
 * A mob standing on CS1's surface in the lower half of the top shadow block always met that, so it jumped in place every
 * tick instead of walking (owner, 2026-10-06: "when I punched it, it just kept jumping in place"). The shadow block is
 * not there for the mob's movement ({@link ShadowCollisionMixin}), so it is not there for this check either; the jump for
 * a path node more than a step above the mob is unchanged.
 */
@Mixin(MoveControl.class)
public abstract class MoveControlShadowMixin {
	@Shadow
	@Final
	protected Mob mob;

	@Redirect(method = "tick", at = @At(value = "INVOKE",
		target = "Lnet/minecraft/world/level/block/state/BlockState;getCollisionShape(Lnet/minecraft/world/level/BlockGetter;Lnet/minecraft/core/BlockPos;)Lnet/minecraft/world/phys/shapes/VoxelShape;"))
	private VoxelShape mcskylines$ownCell(BlockState state, BlockGetter level, BlockPos pos) {
		if (mob.level().dimension() == Level.OVERWORLD && ShadowCells.INSTANCE.ignoresCollision(true, pos.getX(), pos.getY(), pos.getZ(),
				CollisionStore.INSTANCE.isLoaded(pos.getX() >> 4, pos.getZ() >> 4))) {
			return Shapes.empty();
		}
		return state.getCollisionShape(level, pos);
	}
}
