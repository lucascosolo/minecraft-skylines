// Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
// See minecraft/THIRD-PARTY-NOTICES.md for the full licence text.
package dev.mcskylines.client.mixin;

import dev.mcskylines.render.OverlayExporter;
import net.minecraft.client.renderer.LevelRenderer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** While the overlay is published CS1 draws the world: no sky, fog or terrain, so hand + HUD + screens sit on (0,0,0,0). */
@Mixin(LevelRenderer.class)
public abstract class LevelRendererMixin {
	@Inject(method = "render(Lcom/mojang/blaze3d/resource/GraphicsResourceAllocator;ZLnet/minecraft/client/renderer/state/level/CameraRenderState;Lcom/mojang/renderpearl/api/buffers/GpuBufferSlice;Lorg/joml/Vector4f;ZZ)V",
		at = @At("HEAD"), cancellable = true)
	private void mcskylines$skipLevel(CallbackInfo ci) {
		if (OverlayExporter.publishing()) {
			ci.cancel();
		}
	}
}
