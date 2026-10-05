package dev.mcskylines.client.mixin;

import net.minecraft.client.Camera;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

/** Camera's smoothed eye height at the previous and latest tick, for PLAYER_STATE. */
@Mixin(Camera.class)
public interface CameraAccessor {
	@Accessor("eyeHeightOld")
	float mcskylines$eyeHeightOld();

	@Accessor("eyeHeight")
	float mcskylines$eyeHeight();
}
