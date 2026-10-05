/*
 * Ported from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
 * See minecraft/THIRD-PARTY-NOTICES.md. Adapted for Minecraft Skylines.
 */
package dev.mcskylines.mixin;

import dev.mcskylines.collision.PlayerCollider;
import net.minecraft.server.MinecraftServer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * The player stands on city geometry the integrated server knows nothing about; to it they would hover and be
 * kicked for flying. The client keeps them on the ground.
 */
@Mixin(MinecraftServer.class)
public abstract class MinecraftServerFlightMixin {
	@Inject(method = "allowFlight", at = @At("HEAD"), cancellable = true)
	private void mcskylines$standOnCityGeometry(CallbackInfoReturnable<Boolean> cir) {
		if (PlayerCollider.active()) {
			cir.setReturnValue(true);
		}
	}
}
