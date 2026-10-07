package dev.mcskylines.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import com.llamalad7.mixinextras.sugar.Local;
import dev.mcskylines.world.CityClock;
import java.util.function.Consumer;
import net.minecraft.core.Holder;
import net.minecraft.server.MinecraftServer;
import net.minecraft.world.clock.ServerClockManager;
import net.minecraft.world.clock.WorldClock;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;

/**
 * Every change of a clock (/time set, /time add, time markers, sleeping) runs through modifyClock. On the city's clock
 * the change goes to the city as TIME_SET and the clock is put back before it is broadcast, so it only shows WORLD_TIME.
 */
@Mixin(ServerClockManager.class)
abstract class CityClockMixin {
	@Shadow
	private MinecraftServer server;

	@WrapOperation(method = "modifyClock", at = @At(value = "INVOKE", target = "Ljava/util/function/Consumer;accept(Ljava/lang/Object;)V"))
	private void mcskylines$toCity(Consumer<Object> change, Object instance, Operation<Void> original,
			@Local(argsOnly = true) Holder<WorldClock> clock) {
		ServerClockManager.ServerClockInstance clockInstance = (ServerClockManager.ServerClockInstance) instance;
		long before = clockInstance.totalTicks();
		original.call(change, instance);
		long after = clockInstance.totalTicks();
		if (after != before && CityClock.moved(server, clock, before, after)) {
			((ClockInstanceAccess) instance).mcskylines$setTotalTicks(before);
		}
	}
}
