package dev.mcskylines.mixin;

import dev.mcskylines.world.CityEdits;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.chunk.LevelChunk;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Every block-state change in a loaded chunk funnels through here (players, fluids, falling blocks, fire, pistons). */
@Mixin(LevelChunk.class)
abstract class LevelChunkMixin {
	@Shadow
	@Final
	private Level level;

	@Inject(method = "setBlockState", at = @At("RETURN"))
	private void mcskylines$recordChange(BlockPos pos, BlockState state, int flags, CallbackInfoReturnable<BlockState> cir) {
		if (cir.getReturnValue() != null) {
			CityEdits.blockChanged(level, pos, state);
		}
	}
}
