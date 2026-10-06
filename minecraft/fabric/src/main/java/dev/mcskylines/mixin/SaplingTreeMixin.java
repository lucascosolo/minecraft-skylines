package dev.mcskylines.mixin;

import dev.mcskylines.world.CityEdits;
import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.block.SaplingBlock;
import net.minecraft.world.level.block.state.BlockState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** A sapling the player owns grows into a CS1 tree (TREE_GROWN) instead of Minecraft's generated one. */
@Mixin(SaplingBlock.class)
abstract class SaplingTreeMixin {
	@Inject(method = "advanceTree", at = @At("HEAD"), cancellable = true)
	private void mcskylines$cityTree(ServerLevel level, BlockPos pos, BlockState state, RandomSource random, CallbackInfo ci) {
		if (CityEdits.saplingGrows(level, pos, state)) {
			ci.cancel();
		}
	}
}
