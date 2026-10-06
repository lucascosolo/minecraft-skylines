package dev.mcskylines.mixin;

import dev.mcskylines.shadow.ShadowCells;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.level.block.state.BlockBehaviour;
import net.minecraft.world.phys.AABB;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Shadow blocks never suffocate anything and never black out the view (owner, 2026-10-06: "standing on a porch, I
 * started taking suffocation damage" — the player's head was inside a building's barrier fill). Entity.isInWall and the
 * in-block screen effect both go through these two predicates; path-finding and spawning do not.
 */
@Mixin(BlockBehaviour.BlockStateBase.class)
public abstract class ShadowSuffocationMixin {
	@Inject(method = "isSuffocating", at = @At("HEAD"), cancellable = true)
	private void mcskylines$shadowNeverSuffocates(BlockGetter level, BlockPos pos, CallbackInfoReturnable<Boolean> cir) {
		if (ShadowCells.INSTANCE.contains(pos.getX(), pos.getY(), pos.getZ())) {
			cir.setReturnValue(false);
		}
	}

	@Inject(method = "isViewBlocking", at = @At("HEAD"), cancellable = true)
	private void mcskylines$shadowNeverBlocksView(BlockGetter level, BlockPos pos, AABB box, CallbackInfoReturnable<Boolean> cir) {
		if (ShadowCells.INSTANCE.contains(pos.getX(), pos.getY(), pos.getZ())) {
			cir.setReturnValue(false);
		}
	}
}
