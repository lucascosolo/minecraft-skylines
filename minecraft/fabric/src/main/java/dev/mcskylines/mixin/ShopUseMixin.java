package dev.mcskylines.mixin;

import dev.mcskylines.world.CityEdits;
import net.minecraft.client.multiplayer.MultiPlayerGameMode;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.phys.BlockHitResult;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Using a trading city building opens its shop (SHOP_OPEN) instead of placing or using the held item (protocol 1.20). */
@Mixin(MultiPlayerGameMode.class)
abstract class ShopUseMixin {
	@Inject(method = "useItemOn", at = @At("HEAD"), cancellable = true)
	private void mcskylines$shop(LocalPlayer player, InteractionHand hand, BlockHitResult hit, CallbackInfoReturnable<InteractionResult> cir) {
		if (CityEdits.shopUse(player, hit)) {
			cir.setReturnValue(InteractionResult.SUCCESS);
		}
	}
}
