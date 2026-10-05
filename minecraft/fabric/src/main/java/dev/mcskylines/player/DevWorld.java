// Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
// See minecraft/THIRD-PARTY-NOTICES.md for the full licence text.
package dev.mcskylines.player;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.world.Difficulty;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.LevelSettings;
import net.minecraft.world.level.WorldDataConfiguration;
import net.minecraft.world.level.gamerules.GameRules;
import net.minecraft.world.level.levelgen.WorldOptions;
import net.minecraft.world.level.levelgen.presets.WorldPreset;
import net.minecraft.world.level.storage.LevelResource;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/** Opens (or creates) the dedicated void singleplayer world "skylines-dev". Port of SkyCraft's MirrorWorld. Never opens any other world. */
public final class DevWorld {
	public static final String NAME = "skylines-dev";
	private static final Logger LOG = LoggerFactory.getLogger("mcskylines");
	private static final ResourceKey<WorldPreset> PRESET =
		ResourceKey.create(Registries.WORLD_PRESET, Identifier.fromNamespaceAndPath("mcskylines", "void"));
	private static boolean attempted;

	private DevWorld() {
	}

	/** Called when ENTER_PLAYER_MODE arrives with no world loaded: allow one more attempt. */
	static void request() {
		attempted = false;
	}

	/** Every frame while a world is wanted: waits for the title screen, then opens or creates the world once. */
	static void openWhenReady(Minecraft mc) {
		if (attempted || mc.level != null || mc.gui.overlay() != null) {
			return;
		}
		if (!(mc.gui.screen() instanceof TitleScreen title)) {
			if (mc.gui.screen() == null || mc.gui.screen().getClass().getName().contains("Onboarding")) {
				mc.gui.setScreen(new TitleScreen());
			}
			return;
		}
		attempted = true;
		if (mc.getLevelSource().levelExists(NAME)) {
			LOG.info("[MinecraftSkylines] opening world {}", NAME);
			mc.createWorldOpenFlows().openWorld(NAME, () -> mc.gui.setScreen(title));
			return;
		}
		LOG.info("[MinecraftSkylines] creating void world {}", NAME);
		LevelSettings settings = new LevelSettings(NAME, GameType.SURVIVAL,
			new LevelSettings.DifficultySettings(Difficulty.PEACEFUL, false, false), true, WorldDataConfiguration.DEFAULT);
		mc.createWorldOpenFlows().createFreshLevel(NAME, settings, new WorldOptions(0L, false, false),
			registries -> registries.lookupOrThrow(Registries.WORLD_PRESET).getOrThrow(PRESET).value().createWorldDimensions(),
			title);
	}

	/** The integrated server just started: if it is our world (by folder), CS1 drives time, weather and spawning. */
	public static void configureIfOurs(MinecraftServer server) {
		if (!NAME.equals(server.getWorldPath(LevelResource.ROOT).toAbsolutePath().normalize().getFileName().toString())) {
			return;
		}
		GameRules rules = server.getGameRules();
		rules.set(GameRules.ADVANCE_TIME, false, server);
		rules.set(GameRules.ADVANCE_WEATHER, false, server);
		rules.set(GameRules.SPAWN_MOBS, false, server);
		rules.set(GameRules.SPAWN_MONSTERS, false, server);
		rules.set(GameRules.PLAYER_MOVEMENT_CHECK, false, server);
		rules.set(GameRules.KEEP_INVENTORY, true, server);
		rules.set(GameRules.IMMEDIATE_RESPAWN, true, server);
		LOG.info("[MinecraftSkylines] configured world {}", NAME);
	}
}
