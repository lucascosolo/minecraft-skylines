package dev.mcskylines.client.mixin;

import java.util.Map;
import net.minecraft.client.renderer.rendertype.RenderSetup;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

@Mixin(RenderSetup.class)
public interface RenderSetupAccessor {
	/** Values are RenderSetup.TextureBinding (package-private); read them through {@link TextureBindingAccessor}. */
	@Accessor("textures")
	Map<String, ?> mcskylines$textures();
}
