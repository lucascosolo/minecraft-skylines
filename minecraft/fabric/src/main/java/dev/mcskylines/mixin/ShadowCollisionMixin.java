package dev.mcskylines.mixin;

import dev.mcskylines.collision.CollisionStore;
import dev.mcskylines.shadow.ShadowCells;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.level.CollisionGetter;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.shapes.EntityCollisionContext;
import net.minecraft.world.phys.shapes.Shapes;
import net.minecraft.world.phys.shapes.VoxelShape;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Shadow blocks never take part in an entity's movement collision where the city's triangles are loaded (owner,
 * 2026-10-06): entities stand on CS1's smooth surfaces, not on block tops up to half a block off. Queries without an
 * entity (path-finding node evaluation, spawn checks, fluids) still see the blocks.
 */
@Mixin(EntityCollisionContext.class)
public abstract class ShadowCollisionMixin {
	@Inject(method = "getCollisionShape", at = @At("HEAD"), cancellable = true)
	private void mcskylines$skipShadow(BlockState state, CollisionGetter level, BlockPos pos, CallbackInfoReturnable<VoxelShape> cir) {
		Entity e = ((EntityCollisionContext) (Object) this).getEntity();
		if (e != null && e.level().dimension() == Level.OVERWORLD && ShadowCells.INSTANCE.ignoresCollision(true, pos.getX(), pos.getY(),
				pos.getZ(), CollisionStore.INSTANCE.isLoaded(pos.getX() >> 4, pos.getZ() >> 4))) {
			cir.setReturnValue(Shapes.empty());
		}
	}
}
