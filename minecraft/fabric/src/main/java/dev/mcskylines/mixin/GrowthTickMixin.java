package dev.mcskylines.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.mcskylines.world.CityEdits;
import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.level.block.state.BlockState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/** Owned growing blocks of the open city grow only by the city clock (Growth), never by vanilla's random ticks. */
@Mixin(ServerLevel.class)
abstract class GrowthTickMixin {
	@WrapOperation(method = "tickChunk", at = @At(value = "INVOKE",
		target = "Lnet/minecraft/world/level/block/state/BlockState;randomTick(Lnet/minecraft/server/level/ServerLevel;Lnet/minecraft/core/BlockPos;Lnet/minecraft/util/RandomSource;)V"))
	private void mcskylines$cityClockOnly(BlockState state, ServerLevel level, BlockPos pos, RandomSource random, Operation<Void> original) {
		if (!CityEdits.growthOwns(level, pos)) {
			original.call(state, level, pos, random);
		}
	}
}
