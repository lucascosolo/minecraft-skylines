package dev.mcskylines.mixin;

import dev.mcskylines.world.BuriedDrops;
import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.Block;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Drops and experience from blocks under CS1's ground go to the nearest player (see {@link BuriedDrops}). */
@Mixin(Block.class)
public abstract class BuriedDropsMixin {
	@Inject(method = "popResource(Lnet/minecraft/world/level/Level;Lnet/minecraft/core/BlockPos;Lnet/minecraft/world/item/ItemStack;)V",
		at = @At("HEAD"), cancellable = true)
	private static void mcskylines$buriedDrop(Level level, BlockPos pos, ItemStack stack, CallbackInfo ci) {
		if (stack.isEmpty()) {
			return;
		}
		ServerPlayer p = BuriedDrops.receiver(level, pos);
		if (p != null) {
			BuriedDrops.give(p, stack);
			ci.cancel();
		}
	}

	@Inject(method = "popExperience", at = @At("HEAD"), cancellable = true)
	private void mcskylines$buriedExperience(ServerLevel level, BlockPos pos, int amount, CallbackInfo ci) {
		if (amount <= 0) {
			return;
		}
		ServerPlayer p = BuriedDrops.receiver(level, pos);
		if (p != null) {
			p.giveExperiencePoints(amount);
			ci.cancel();
		}
	}
}
