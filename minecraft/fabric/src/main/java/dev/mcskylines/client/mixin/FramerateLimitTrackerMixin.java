// Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
// See minecraft/THIRD-PARTY-NOTICES.md for the full licence text.
package dev.mcskylines.client.mixin;

import com.mojang.blaze3d.platform.FramerateLimitTracker;
import dev.mcskylines.player.PlayerMode;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** No hidden-window or menu throttling while linked: 260 is Minecraft's "unlimited". */
@Mixin(FramerateLimitTracker.class)
public abstract class FramerateLimitTrackerMixin {
	@Inject(method = "getFramerateLimit", at = @At("HEAD"), cancellable = true)
	private void mcskylines$unlimited(CallbackInfoReturnable<Integer> cir) {
		if (PlayerMode.linked()) {
			cir.setReturnValue(260);
		}
	}
}
