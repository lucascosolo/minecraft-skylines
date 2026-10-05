// Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
// See minecraft/THIRD-PARTY-NOTICES.md for the full licence text.
package dev.mcskylines.client.mixin;

import com.mojang.blaze3d.platform.InputConstants;
import com.mojang.blaze3d.platform.Window;
import dev.mcskylines.player.InputReplay;
import dev.mcskylines.player.PlayerMode;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** While linked, key state comes from the host's INPUT and Minecraft never grabs or releases the real mouse. */
@Mixin(InputConstants.class)
public abstract class InputConstantsMixin {
	@Inject(method = "isKeyDown", at = @At("HEAD"), cancellable = true)
	private static void mcskylines$isKeyDown(int key, CallbackInfoReturnable<Boolean> cir) {
		if (PlayerMode.linked()) {
			cir.setReturnValue(InputReplay.isKeyDown(key));
		}
	}

	@Inject(method = "grabMouse", at = @At("HEAD"), cancellable = true)
	private static void mcskylines$grabMouse(Window window, double xpos, double ypos, CallbackInfo ci) {
		if (PlayerMode.linked()) {
			ci.cancel();
		}
	}

	@Inject(method = "releaseMouse", at = @At("HEAD"), cancellable = true)
	private static void mcskylines$releaseMouse(Window window, double xpos, double ypos, CallbackInfo ci) {
		if (PlayerMode.linked()) {
			ci.cancel();
		}
	}
}
