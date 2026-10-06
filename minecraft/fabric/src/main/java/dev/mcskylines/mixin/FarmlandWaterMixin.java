package dev.mcskylines.mixin;

import dev.mcskylines.world.HostWater;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.LevelReader;
import net.minecraft.world.level.block.FarmlandBlock;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** CS1's water (protocol 1.10 grid) hydrates farmland within vanilla's reach (4 blocks across, same level or one up). */
@Mixin(FarmlandBlock.class)
public abstract class FarmlandWaterMixin {
	@Inject(method = "isNearWater", at = @At("RETURN"), cancellable = true)
	private static void mcskylines$hostWater(LevelReader level, BlockPos pos, CallbackInfoReturnable<Boolean> cir) {
		if (!cir.getReturnValueZ() && HostWater.active()
				&& HostWater.anyIn(pos.getX() - 4, pos.getY(), pos.getZ() - 4, pos.getX() + 4, pos.getY() + 1, pos.getZ() + 4)) {
			cir.setReturnValue(true);
		}
	}
}
