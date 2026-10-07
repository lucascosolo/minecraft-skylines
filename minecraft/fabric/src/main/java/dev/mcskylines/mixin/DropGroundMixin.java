package dev.mcskylines.mixin;

import dev.mcskylines.collision.CollisionStore;
import dev.mcskylines.collision.PlayerCollider;
import dev.mcskylines.world.DropGround;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.ExperienceOrb;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.Vec3;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** After an item's or orb's tick (both sides), a drop that ended under CS1's ground is put back on it ({@link DropGround}). */
@Mixin({ItemEntity.class, ExperienceOrb.class})
public abstract class DropGroundMixin {
	@Unique
	private boolean mcskylines$wasOnGround;

	@Inject(method = "tick", at = @At("TAIL"))
	private void mcskylines$settle(CallbackInfo ci) {
		Entity self = (Entity) (Object) this;
		if (self.isRemoved() || self.noPhysics || self.level().dimension() != Level.OVERWORLD || !PlayerCollider.active()
				|| !CollisionStore.INSTANCE.regionsLoadedAround(self.getX(), self.getZ(), 0)) {
			return;
		}
		double surface = DropGround.surfaceAt(self.getX(), self.getY(), self.getZ());
		if (surface > self.getY() + 1e-3) {
			self.setPos(self.getX(), surface, self.getZ());
			Vec3 v = self.getDeltaMovement();
			if (v.y < 0) {
				self.setDeltaMovement(v.x, 0, v.z);
			}
			self.setOnGround(true);
		}
		if (self.onGround() && !mcskylines$wasOnGround && !self.level().isClientSide()) {
			DropGround.landed(self instanceof ItemEntity ? "item" : "orb", self.getId(), self.getX(), self.getY(), self.getZ());
		}
		mcskylines$wasOnGround = self.onGround();
	}
}
