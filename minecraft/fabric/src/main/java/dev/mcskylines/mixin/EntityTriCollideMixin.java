package dev.mcskylines.mixin;

import dev.mcskylines.collision.CollisionStore;
import dev.mcskylines.collision.PlayerCollider;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.Vec3;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Mobs, items, orbs and every other non-player entity in the city world collide with the host's triangles and moving
 * obstacles like the player does (owner, 2026-10-06), wherever the collision region under them is loaded. Both sides:
 * the client simulates its own copy of an item or orb, and with the shadow blocks ignored that copy fell forever (owner,
 * 2026-10-06: "dropped items seem to constantly be falling through the ground"); CS1 draws the client copies.
 */
@Mixin(Entity.class)
public abstract class EntityTriCollideMixin {
	@Inject(method = "collide", at = @At("RETURN"), cancellable = true)
	private void mcskylines$collideTriangles(Vec3 movement, CallbackInfoReturnable<Vec3> cir) {
		Entity self = (Entity) (Object) this;
		if (self instanceof Player || self.noPhysics ||self.level().dimension() != Level.OVERWORLD
				|| !PlayerCollider.active() || !CollisionStore.INSTANCE.regionsLoadedAround(self.getX(), self.getZ(), 0)) {
			return;
		}
		cir.setReturnValue(PlayerCollider.collide(self, cir.getReturnValue()));
	}
}
