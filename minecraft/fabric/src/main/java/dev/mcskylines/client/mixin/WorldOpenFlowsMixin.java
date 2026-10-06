// Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
// See minecraft/THIRD-PARTY-NOTICES.md for the full licence text.
package dev.mcskylines.client.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import dev.mcskylines.player.DevWorld;
import net.minecraft.client.gui.screens.worldselection.WorldOpenFlows;
import net.minecraft.world.level.storage.LevelStorageSource;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/** The void world's custom dimension type counts as experimental; skip the backup prompt for our "skylines-city" only. */
@Mixin(WorldOpenFlows.class)
public abstract class WorldOpenFlowsMixin {
	@WrapOperation(method = "openWorldCheckWorldStemCompatibility", at = @At(value = "INVOKE",
		target = "Lnet/minecraft/client/gui/screens/worldselection/WorldOpenFlows;askForBackup(Lnet/minecraft/world/level/storage/LevelStorageSource$LevelStorageAccess;ZLjava/lang/Runnable;Ljava/lang/Runnable;)V"))
	private void mcskylines$skipBackupPrompt(WorldOpenFlows self, LevelStorageSource.LevelStorageAccess access,
			boolean oldCustomized, Runnable proceed, Runnable cancel, Operation<Void> original) {
		if (DevWorld.NAME.equals(access.getLevelId())) {
			proceed.run();
		} else {
			original.call(self, access, oldCustomized, proceed, cancel);
		}
	}
}
