package dev.mcskylines.mixin;

import dev.mcskylines.world.Growth;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.Level;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Growth catch-up random-ticks a block as at a past time of day: the sky darkening is that moment's, not now's. */
@Mixin(Level.class)
abstract class GrowthSkyMixin {
	@Inject(method = "getSkyDarken", at = @At("HEAD"), cancellable = true)
	private void mcskylines$pastSky(CallbackInfoReturnable<Integer> cir) {
		int override = Growth.skyOverride();
		if (override >= 0 && (Object) this instanceof ServerLevel) {
			cir.setReturnValue(override);
		}
	}
}
