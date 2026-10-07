package dev.mcskylines.mixin;

import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.ExperienceOrb;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.AABB;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

/**
 * ExperienceOrb.tick asks "am I inside a block?" without an entity, so the shadow block under CS1's surface counted: the
 * orb stopped falling and was pushed out sideways (moveTowardsClosestSpace). Asked with the orb as the collision context,
 * the test skips shadow blocks as its movement does ({@link ShadowCollisionMixin}) and the orb rests on the triangles.
 */
@Mixin(ExperienceOrb.class)
public abstract class ExperienceOrbShadowMixin {
	@Redirect(method = "tick", at = @At(value = "INVOKE",
		target = "Lnet/minecraft/world/level/Level;noCollision(Lnet/minecraft/world/phys/AABB;)Z"), require = 2)
	private boolean mcskylines$orbContext(Level level, AABB box) {
		return level.noCollision((Entity) (Object) this, box);
	}
}
