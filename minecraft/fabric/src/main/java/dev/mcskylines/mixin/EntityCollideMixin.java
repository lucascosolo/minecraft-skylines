/*
 * Ported from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
 * See minecraft/THIRD-PARTY-NOTICES.md. Adapted for Minecraft Skylines.
 */
package dev.mcskylines.mixin;

import dev.mcskylines.collision.PlayerCollider;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.phys.Vec3;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** After vanilla collides the local player's movement with blocks, collide it with the city's exact triangles. */
@Mixin(Entity.class)
public abstract class EntityCollideMixin {
	@Inject(method = "collide", at = @At("RETURN"), cancellable = true)
	private void mcskylines$collideTriangles(Vec3 movement, CallbackInfoReturnable<Vec3> cir) {
		if ((Object) this instanceof LocalPlayer player && PlayerCollider.active() && !player.noPhysics) {
			cir.setReturnValue(PlayerCollider.collide(player, cir.getReturnValue()));
		}
	}
}
