// Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
// See minecraft/THIRD-PARTY-NOTICES.md for the full licence text.
package dev.mcskylines.client.mixin;

import com.mojang.blaze3d.platform.Window;
import dev.mcskylines.player.PlayerMode;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Cities: Skylines has the real focus while linked; Minecraft acts as if its (hidden) window had it. */
@Mixin(Window.class)
public abstract class WindowMixin {
	@Inject(method = "isFocused", at = @At("HEAD"), cancellable = true)
	private void mcskylines$focused(CallbackInfoReturnable<Boolean> cir) {
		if (PlayerMode.linked()) {
			cir.setReturnValue(true);
		}
	}

	@Inject(method = "isIconified", at = @At("HEAD"), cancellable = true)
	private void mcskylines$notIconified(CallbackInfoReturnable<Boolean> cir) {
		if (PlayerMode.linked()) {
			cir.setReturnValue(false);
		}
	}
}
