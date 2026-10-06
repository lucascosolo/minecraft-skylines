/*
 * Ported from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
 * See minecraft/THIRD-PARTY-NOTICES.md. Adapted for Minecraft Skylines (host collision triangles instead of voxels).
 */
package dev.mcskylines.mixin;

import dev.mcskylines.collision.CollisionStore;
import dev.mcskylines.collision.FluidGround;
import dev.mcskylines.collision.FluidRules;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.tags.FluidTags;
import net.minecraft.world.level.BlockGetter;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.material.FlowingFluid;
import net.minecraft.world.level.material.FluidState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Water and lava the player pours settle on the city's ground and run over it, never into it or through the void
 * below: they rest on host geometry, flow downhill and over bumps but not uphill, stop against walls, and never enter
 * cells the host has not described yet. Rules in {@link FluidRules}; protocol spec, minor 10 guest notes.
 */
@Mixin(FlowingFluid.class)
public abstract class FlowingFluidMixin {
	@Inject(method = "canPassThroughWall", at = @At("HEAD"), cancellable = true)
	private static void mcskylines$cityWall(
		Direction direction, BlockGetter level, BlockPos sourcePos, BlockState sourceState, BlockPos targetPos, BlockState targetState,
		CallbackInfoReturnable<Boolean> cir
	) {
		CollisionStore store = CollisionStore.INSTANCE;
		if (!targetState.isAir() || direction == Direction.UP || store.regionCount() == 0) {
			return;
		}
		int x = targetPos.getX(), y = targetPos.getY(), z = targetPos.getZ();
		if (!FluidGround.isKnown(store, x, z)) {
			cir.setReturnValue(false);
			return;
		}
		float sourceTop = FluidGround.groundTop(store, sourcePos.getX(), sourcePos.getY(), sourcePos.getZ());
		float targetTop = FluidGround.groundTop(store, x, y, z);
		boolean refuse;
		if (direction == Direction.DOWN) {
			refuse = FluidRules.refuseDown(sourceTop, targetTop);
		} else {
			FluidState fluid = sourceState.getFluidState();
			float surface = FluidRules.spreadSurface(fluid.isEmpty(), !fluid.isEmpty() && fluid.getValue(FlowingFluid.FALLING),
				fluid.isEmpty() ? 0 : fluid.getAmount(), fluid.is(FluidTags.LAVA));
			float aboveTop = targetTop == FluidGround.NONE ? FluidGround.groundTop(store, x, y + 1, z) : FluidGround.NONE;
			refuse = FluidRules.refuseSideways(sourceTop, targetTop, aboveTop, surface);
		}
		if (refuse) {
			cir.setReturnValue(false);
		}
	}
}
