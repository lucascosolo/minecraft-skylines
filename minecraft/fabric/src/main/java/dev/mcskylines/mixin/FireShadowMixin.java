package dev.mcskylines.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.mcskylines.shadow.ShadowCells;
import net.minecraft.core.BlockPos;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.LevelReader;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.FireBlock;
import net.minecraft.world.level.block.state.BlockState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Minecraft fire never burns or spreads through shadow blocks (the city's own ground, grass and tree trunks): CS1 owns
 * its trees and ground and has its own fires, so a burning building (protocol 1.21) must not fell city trees, strip the
 * city's grass or leave scars saved as player edits. Blocks the player placed burn as usual. To fire, a shadow cell is
 * not flammable: it neither burns out nor lets fire spread next to it.
 */
@Mixin(FireBlock.class)
abstract class FireShadowMixin {
	private static boolean mcskylines$shadow(BlockPos pos) {
		return ShadowCells.INSTANCE.contains(pos.getX(), pos.getY(), pos.getZ());
	}

	@Inject(method = "checkBurnOut", at = @At("HEAD"), cancellable = true)
	private void mcskylines$keepShadow(Level level, BlockPos pos, int chance, RandomSource random, int age, CallbackInfo ci) {
		if (mcskylines$shadow(pos)) {
			ci.cancel();
		}
	}

	@WrapOperation(method = "isValidFireLocation", at = @At(value = "INVOKE",
			target = "Lnet/minecraft/world/level/BlockGetter;getBlockState(Lnet/minecraft/core/BlockPos;)Lnet/minecraft/world/level/block/state/BlockState;"))
	private BlockState mcskylines$validIgnoresShadow(BlockGetter level, BlockPos pos, Operation<BlockState> original) {
		return mcskylines$shadow(pos) ? Blocks.AIR.defaultBlockState() : original.call(level, pos);
	}

	@WrapOperation(method = "getIgniteOdds(Lnet/minecraft/world/level/LevelReader;Lnet/minecraft/core/BlockPos;)I", at = @At(value = "INVOKE",
			target = "Lnet/minecraft/world/level/LevelReader;getBlockState(Lnet/minecraft/core/BlockPos;)Lnet/minecraft/world/level/block/state/BlockState;"))
	private BlockState mcskylines$igniteIgnoresShadow(LevelReader level, BlockPos pos, Operation<BlockState> original) {
		return mcskylines$shadow(pos) ? Blocks.AIR.defaultBlockState() : original.call(level, pos);
	}
}
