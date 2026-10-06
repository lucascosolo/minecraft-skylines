package dev.mcskylines.mixin;

import dev.mcskylines.world.CityEdits;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.level.ServerPlayerGameMode;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.BlockHitResult;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Hoes, shovels and bone meal aimed at CS1 ground act on the shadow ground block under the invisible grass. */
@Mixin(ServerPlayerGameMode.class)
abstract class GroundToolMixin {
	private static boolean mcskylines$redirecting;

	@Inject(method = "useItemOn", at = @At("HEAD"), cancellable = true)
	private void mcskylines$groundBlock(ServerPlayer player, Level level, ItemStack stack, InteractionHand hand, BlockHitResult hit,
			CallbackInfoReturnable<InteractionResult> cir) {
		if (mcskylines$redirecting) {
			return;
		}
		BlockHitResult ground = CityEdits.groundUse(level, stack, hit);
		if (ground == null) {
			return;
		}
		mcskylines$redirecting = true;
		try {
			cir.setReturnValue(((ServerPlayerGameMode) (Object) this).useItemOn(player, level, stack, hand, ground));
		} finally {
			mcskylines$redirecting = false;
		}
	}
}
