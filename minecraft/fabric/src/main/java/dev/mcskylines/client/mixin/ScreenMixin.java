// Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
// See minecraft/THIRD-PARTY-NOTICES.md for the full licence text.
package dev.mcskylines.client.mixin;

import dev.mcskylines.player.PlayerMode;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** The city keeps running while a Minecraft screen is open, so the integrated server must too. */
@Mixin(Screen.class)
public abstract class ScreenMixin {
	@Inject(method = "isPauseScreen", at = @At("HEAD"), cancellable = true)
	private void mcskylines$neverPauseWhileLinked(CallbackInfoReturnable<Boolean> cir) {
		if (PlayerMode.linked()) {
			cir.setReturnValue(false);
		}
	}
}
