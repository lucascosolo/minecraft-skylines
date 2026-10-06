package dev.mcskylines;

import dev.mcskylines.bridge.BridgeGuest;
import dev.mcskylines.protocol.AppProtocol;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.command.v2.ClientCommandRegistrationCallback;
import net.fabricmc.fabric.api.client.command.v2.ClientCommands;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientLifecycleEvents;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerChunkEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.fabricmc.fabric.api.networking.v1.ServerPlayConnectionEvents;
import dev.mcskylines.player.DevWorld;
import dev.mcskylines.world.CityEdits;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.Minecraft;
import net.minecraft.network.chat.Component;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public final class MinecraftSkylinesClient implements ClientModInitializer {
	public static final String MOD_ID = "mcskylines";
	static final Logger LOG = LoggerFactory.getLogger(MOD_ID);
	static final String PREFIX = "[MinecraftSkylines] ";
	private static LinkController link;

	/** MinecraftMixin: start of Minecraft.runTick. */
	public static void onFrameStart() {
		if (link != null) {
			link.frame(Minecraft.getInstance());
		}
	}

	/** MinecraftMixin: right after GameRenderer.render(). */
	public static void onFrameRendered() {
		if (link != null) {
			link.rendered(Minecraft.getInstance());
		}
	}

	@Override
	public void onInitializeClient() {
		if (!Boolean.parseBoolean(System.getProperty("mcskylines.enabled", "true"))) {
			LOG.info(PREFIX + "disabled by -Dmcskylines.enabled=false");
			return;
		}
		int port = Integer.getInteger("mcskylines.port", BridgeGuest.DEFAULT_PORT);
		BridgeGuest guest = new BridgeGuest(BridgeGuest.Config.of(AppProtocol.NAME, AppProtocol.MAJOR, AppProtocol.MINOR,
				"Minecraft " + version("minecraft") + " (Fabric)", version(MOD_ID))
			.withPort(port)
			.withLogger(msg -> LOG.info(PREFIX + "bridge: {}", msg)));
		CityEdits city = new CityEdits(guest);
		LinkController link = new LinkController(guest, city);
		MinecraftSkylinesClient.link = link;
		ServerLifecycleEvents.SERVER_STARTED.register(DevWorld::configureIfOurs);
		ServerLifecycleEvents.SERVER_STARTED.register(city::attach);
		ServerLifecycleEvents.SERVER_STOPPING.register(city::detach);
		ServerLifecycleEvents.BEFORE_SAVE.register((server, flush, force) -> city.beforeSave(server));
		ServerLifecycleEvents.AFTER_SAVE.register((server, flush, force) -> city.afterSave(server, flush));
		ServerChunkEvents.CHUNK_UNLOAD.register((level, chunk) -> city.chunkUnloading(level));
		ServerTickEvents.END_SERVER_TICK.register(city::serverTick);
		ServerPlayConnectionEvents.JOIN.register((handler, sender, server) -> DevWorld.creativeIfOurs(handler.player, server));

		ClientLifecycleEvents.CLIENT_STARTED.register(client -> {
			LOG.info(PREFIX + "connecting to Cities: Skylines on 127.0.0.1:{}", port);
			guest.start();
		});
		ClientTickEvents.END_CLIENT_TICK.register(link::tick);
		ClientLifecycleEvents.CLIENT_STOPPING.register(client -> link.stop());
		ClientCommandRegistrationCallback.EVENT.register((dispatcher, context) -> dispatcher.register(
			ClientCommands.literal("skylines").then(ClientCommands.literal("status").executes(c -> {
				for (String line : link.statusLines()) {
					c.getSource().sendFeedback(Component.literal(line));
				}
				return 1;
			}))));
	}

	private static String version(String modId) {
		return FabricLoader.getInstance().getModContainer(modId)
			.map(c -> c.getMetadata().getVersion().getFriendlyString()).orElse("unknown");
	}
}
