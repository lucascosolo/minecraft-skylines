/*
 * Ported from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
 * See minecraft/THIRD-PARTY-NOTICES.md. Adapted for Minecraft Skylines.
 */
package dev.mcskylines.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.mcskylines.collision.PlayerCollider;
import dev.mcskylines.collision.SkyClip;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.level.ClipContext;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.BlockHitResult;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/** The local player's crosshair targets the city's surfaces, so blocks can be placed on terrain, roads and buildings. */
@Mixin(Entity.class)
public abstract class EntityPickMixin {
	@WrapOperation(method = "pick", at = @At(value = "INVOKE",
		target = "Lnet/minecraft/world/level/Level;clip(Lnet/minecraft/world/level/ClipContext;)Lnet/minecraft/world/phys/BlockHitResult;"))
	private BlockHitResult mcskylines$pickTriangles(Level level, ClipContext context, Operation<BlockHitResult> original) {
		BlockHitResult vanilla = original.call(level, context);
		if (!((Object) this instanceof LocalPlayer) || !PlayerCollider.active()) {
			return vanilla;
		}
		return SkyClip.pick(level, context.getFrom(), context.getTo(), vanilla);
	}
}
