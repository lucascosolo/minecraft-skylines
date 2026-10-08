package dev.mcskylines;

import dev.mcskylines.bridge.BridgeGuest;
import dev.mcskylines.protocol.AppProtocol;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.command.v2.ClientCommandRegistrationCallback;
import net.fabricmc.fabric.api.client.command.v2.ClientCommands;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientLifecycleEvents;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.event.player.PlayerBlockBreakEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerChunkEvents;
import net.fabricmc.fabric.api.entity.event.v1.ServerPlayerEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerEntityEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.fabricmc.fabric.api.networking.v1.ServerPlayConnectionEvents;
import dev.mcskylines.player.DevWorld;
import dev.mcskylines.world.CitizenProxies;
import dev.mcskylines.world.CitizenRules;
import dev.mcskylines.world.CityEdits;
import net.fabricmc.fabric.api.entity.event.v1.ServerLivingEntityEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerEntityEvents;
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
		dev.mcskylines.world.TreeFeller.register(city);
		MinecraftSkylinesClient.link = link;
		ServerLifecycleEvents.SERVER_STARTED.register(DevWorld::configureIfOurs);
		ServerLifecycleEvents.SERVER_STARTED.register(city::attach);
		ServerLifecycleEvents.SERVER_STOPPING.register(city::detach);
		ServerLifecycleEvents.BEFORE_SAVE.register((server, flush, force) -> city.beforeSave(server));
		ServerLifecycleEvents.AFTER_SAVE.register((server, flush, force) -> city.afterSave(server, flush));
		ServerChunkEvents.CHUNK_UNLOAD.register((level, chunk) -> city.chunkUnloading(level));
		ServerTickEvents.END_SERVER_TICK.register(city::serverTick);
		ServerEntityEvents.ENTITY_LOAD.register(CityEdits::entityLoaded);
		ServerEntityEvents.ENTITY_UNLOAD.register(CityEdits::entityUnloaded);
		ServerTickEvents.END_SERVER_TICK.register(dev.mcskylines.world.AnimalSpawner::tick);
		CitizenProxies.register(city);
		ServerTickEvents.END_SERVER_TICK.register(CitizenProxies::tick);
		ServerLivingEntityEvents.ALLOW_DAMAGE.register((entity, source, amount) -> CitizenProxies.allowDamage(entity, source));
		ServerEntityEvents.ENTITY_LOAD.register((entity, level) -> CitizenProxies.loaded(entity));
		PlayerBlockBreakEvents.BEFORE.register((level, player, pos, state, be) -> !CityEdits.refusesBreak(level, pos));
		// The city's player (survival unless its own data says otherwise; creative only via DEBUG_COMMAND /gamemode).
		ServerPlayConnectionEvents.JOIN.register((handler, sender, server) -> city.playerJoined(server));
		ServerPlayerEvents.AFTER_RESPAWN.register((oldPlayer, newPlayer, alive) -> city.respawned(newPlayer, alive));

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
			})).then(ClientCommands.literal("rules")
				.then(rule("citizens", city, (r, on) -> new CitizenRules(on, r.conversion())))
				.then(rule("conversion", city, (r, on) -> new CitizenRules(r.citizens(), on)))
				.executes(c -> {
					CitizenRules r = city.rules();
					c.getSource().sendFeedback(Component.literal("citizens as villagers: " + onOff(r.citizens())
						+ ", zombie conversion: " + onOff(r.conversion())));
					return 1;
				}))));
	}

	// "/skylines rules <name> on|off": a per-city rule, saved with the city's player data (protocol 1.19).
	private static com.mojang.brigadier.builder.LiteralArgumentBuilder<net.fabricmc.fabric.api.client.command.v2.FabricClientCommandSource> rule(
			String name, CityEdits city, java.util.function.BiFunction<CitizenRules, Boolean, CitizenRules> set) {
		var node = ClientCommands.literal(name);
		for (boolean on : new boolean[] {true, false}) {
			node.then(ClientCommands.literal(onOff(on)).executes(c -> {
				city.setRules(set.apply(city.rules(), on));
				c.getSource().sendFeedback(Component.literal(name + " " + onOff(on) + " for this city"));
				return 1;
			}));
		}
		return node;
	}

	private static String onOff(boolean on) {
		return on ? "on" : "off";
	}

	private static String version(String modId) {
		return FabricLoader.getInstance().getModContainer(modId)
			.map(c -> c.getMetadata().getVersion().getFriendlyString()).orElse("unknown");
	}
}
