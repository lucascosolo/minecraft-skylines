// Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
// See minecraft/THIRD-PARTY-NOTICES.md for the full licence text.
package dev.mcskylines.client.mixin;

import dev.mcskylines.MinecraftSkylinesClient;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Drains the bridge at the start of every frame and publishes PLAYER_STATE once the frame is rendered. */
@Mixin(Minecraft.class)
public abstract class MinecraftMixin {
	@Inject(method = "runTick", at = @At("HEAD"))
	private void mcskylines$beginFrame(boolean advanceGameTime, CallbackInfo ci) {
		MinecraftSkylinesClient.onFrameStart();
	}

	@Inject(method = "renderFrame",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/client/renderer/GameRenderer;render()V", shift = At.Shift.AFTER))
	private void mcskylines$afterRender(boolean advanceGameTime, CallbackInfo ci) {
		MinecraftSkylinesClient.onFrameRendered();
	}
}
