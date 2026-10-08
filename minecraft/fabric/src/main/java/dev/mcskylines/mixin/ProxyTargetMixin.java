package dev.mcskylines.mixin;

import dev.mcskylines.world.CitizenProxies;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.Mob;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** A mob that targets a citizen's proxy panics the citizen in CS1 (CITIZEN_EVENTS PANIC). */
@Mixin(Mob.class)
public abstract class ProxyTargetMixin {
	@Inject(method = "setTarget", at = @At("HEAD"))
	private void mcskylines$targetsCitizen(LivingEntity target, CallbackInfo ci) {
		if (!((Mob) (Object) this).level().isClientSide()) {
			CitizenProxies.targeted(target);
		}
	}
}
