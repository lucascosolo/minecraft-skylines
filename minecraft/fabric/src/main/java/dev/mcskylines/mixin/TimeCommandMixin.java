package dev.mcskylines.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import com.llamalad7.mixinextras.sugar.Local;
import dev.mcskylines.world.CityClock;
import dev.mcskylines.world.CityTime;
import net.minecraft.commands.CommandSourceStack;
import net.minecraft.core.Holder;
import net.minecraft.server.commands.TimeCommand;
import net.minecraft.world.clock.ServerClockManager;
import net.minecraft.world.clock.WorldClock;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/** On the city's clock, {@code /time set <ticks>} counts from the start of the shown day (protocol 1.16 TIME_SET). */
@Mixin(TimeCommand.class)
abstract class TimeCommandMixin {
	@WrapOperation(method = "setTotalTicks", at = @At(value = "INVOKE",
		target = "Lnet/minecraft/world/clock/ServerClockManager;setTotalTicks(Lnet/minecraft/core/Holder;J)V"))
	private static void mcskylines$fromShownDay(ServerClockManager clocks, Holder<WorldClock> clock, long ticks, Operation<Void> original,
			@Local(argsOnly = true) CommandSourceStack source) {
		long value = CityClock.drives(source.getServer(), clock) ? CityTime.rebase(clocks.getInstance(clock).totalTicks(), ticks) : ticks;
		original.call(clocks, clock, value);
	}
}
