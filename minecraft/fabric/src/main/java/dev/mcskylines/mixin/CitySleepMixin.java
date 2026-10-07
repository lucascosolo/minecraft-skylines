package dev.mcskylines.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.mcskylines.world.CityClock;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.gamerules.GameRule;
import net.minecraft.world.level.gamerules.GameRules;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/**
 * The city world never advances time itself (ADVANCE_TIME false), which also stops sleeping from skipping the night.
 * On the city's clock sleeping moves to the wake-up time anyway; CityClockMixin sends that to the city as TIME_SET.
 */
@Mixin(ServerLevel.class)
abstract class CitySleepMixin {
	@WrapOperation(method = "tick", at = @At(value = "INVOKE",
		target = "Lnet/minecraft/world/level/gamerules/GameRules;get(Lnet/minecraft/world/level/gamerules/GameRule;)Ljava/lang/Object;"))
	private Object mcskylines$sleepMovesCity(GameRules rules, GameRule<?> rule, Operation<Object> original) {
		Object value = original.call(rules, rule);
		if (rule != GameRules.ADVANCE_TIME || Boolean.TRUE.equals(value)) return value;
		ServerLevel level = (ServerLevel) (Object) this;
		boolean city = level.dimensionType().defaultClock().map(c -> CityClock.drives(level.getServer(), c)).orElse(false);
		return city ? Boolean.TRUE : value;
	}
}
