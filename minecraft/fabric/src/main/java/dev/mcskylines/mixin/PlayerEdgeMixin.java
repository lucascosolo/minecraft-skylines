/*
 * Ported from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
 * See minecraft/THIRD-PARTY-NOTICES.md. Adapted for Minecraft Skylines.
 */
package dev.mcskylines.mixin;

import dev.mcskylines.collision.PlayerCollider;
import net.minecraft.world.entity.MoverType;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.phys.Vec3;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Crouching does not stop at edges: vanilla looks for block collision under the player to find an edge, and the
 * city ground is triangles, so every direction would look like a drop and crouching would freeze the player.
 */
@Mixin(Player.class)
public abstract class PlayerEdgeMixin {
	@Inject(method = "maybeBackOffFromEdge", at = @At("HEAD"), cancellable = true)
	private void mcskylines$crouchWalkAnywhere(Vec3 delta, MoverType moverType, CallbackInfoReturnable<Vec3> cir) {
		if (PlayerCollider.active()) {
			cir.setReturnValue(delta);
		}
	}
}
