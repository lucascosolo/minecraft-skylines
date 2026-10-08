package dev.mcskylines.mixin;

import dev.mcskylines.world.CitizenProxies;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.monster.zombie.Zombie;
import net.minecraft.world.entity.npc.villager.Villager;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * A zombie's kill of a citizen's proxy: vanilla's rule (26.3 Zombie.killedEntity: Normal on a coin, Hard always) plus
 * the city's conversion switch, then vanilla's own conversion. Entity.killedEntity (the super chain) only returns true.
 */
@Mixin(Zombie.class)
public abstract class ZombieConversionMixin {
	@Inject(method = "killedEntity", at = @At("HEAD"), cancellable = true)
	private void mcskylines$citizenKill(ServerLevel level, LivingEntity victim, DamageSource source, CallbackInfoReturnable<Boolean> cir) {
		if (!(victim instanceof Villager villager) || !CitizenProxies.isProxy(villager)) {
			return;
		}
		Zombie self = (Zombie) (Object) this;
		boolean convert = CitizenProxies.convertsProxy(villager, level.getDifficulty().getId(), self.getRandom().nextBoolean());
		cir.setReturnValue(!(convert && self.convertVillagerToZombieVillager(level, villager)));
	}
}
