package dev.mcskylines.mixin;

import dev.mcskylines.world.CityView;
import net.minecraft.world.Difficulty;
import net.minecraft.world.entity.Mob;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Protocol 1.18: mobs in the city view's simulated area are not despawned for being far from the player. */
@Mixin(Mob.class)
abstract class CityViewDespawnMixin {
	@Inject(method = "checkDespawn", at = @At("HEAD"), cancellable = true)
	private void mcskylines$keepInCityView(CallbackInfo ci) {
		Mob self = (Mob) (Object) this;
		if (self.level().getDifficulty() != Difficulty.PEACEFUL && CityView.keepsFromDespawn(self)) {
			ci.cancel();
		}
	}
}
