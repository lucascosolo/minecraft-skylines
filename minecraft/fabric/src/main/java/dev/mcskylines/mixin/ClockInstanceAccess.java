package dev.mcskylines.mixin;

import net.minecraft.world.clock.ServerClockManager;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

/** Puts a city-driven clock back after a time command moved it (CityClockMixin). */
@Mixin(ServerClockManager.ServerClockInstance.class)
public interface ClockInstanceAccess {
	@Accessor("totalTicks")
	void mcskylines$setTotalTicks(long ticks);
}
