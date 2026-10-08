package dev.mcskylines.mixin;

import dev.mcskylines.world.CitizenProxies;
import net.minecraft.world.entity.LivingEntity;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** A citizen's proxy is never under the crosshair, so the player cannot hit or trade with it and mines straight past it. */
@Mixin(LivingEntity.class)
public abstract class ProxyPickMixin {
	@Inject(method = "isPickable", at = @At("HEAD"), cancellable = true)
	private void mcskylines$proxyNotPickable(CallbackInfoReturnable<Boolean> cir) {
		if (CitizenProxies.isProxy((LivingEntity) (Object) this)) {
			cir.setReturnValue(false);
		}
	}
}
