package dev.mcskylines;

import static dev.mcskylines.MinecraftSkylinesClient.LOG;
import static dev.mcskylines.MinecraftSkylinesClient.PREFIX;

import dev.mcskylines.bridge.BridgeEvent;
import dev.mcskylines.bridge.BridgeGuest;
import dev.mcskylines.bridge.BridgeState;
import dev.mcskylines.bridge.DisconnectCause;
import dev.mcskylines.bridge.Goodbye;
import dev.mcskylines.bridge.ProtocolException;
import dev.mcskylines.bridge.Welcome;
import dev.mcskylines.collision.CollisionStore;
import dev.mcskylines.collision.DynamicObstacleStore;
import dev.mcskylines.protocol.AppProtocol;
import dev.mcskylines.protocol.CollisionRegion;
import dev.mcskylines.protocol.CollisionReset;
import dev.mcskylines.protocol.DynamicObstacles;
import dev.mcskylines.protocol.DebugCommand;
import dev.mcskylines.protocol.EnterPlayerMode;
import dev.mcskylines.protocol.ExitPlayerMode;
import dev.mcskylines.protocol.GuestStatus;
import dev.mcskylines.protocol.HostStatus;
import dev.mcskylines.protocol.Input;
import dev.mcskylines.protocol.PlayerState;
import dev.mcskylines.protocol.WaterSurface;
import dev.mcskylines.protocol.WorldTime;
import dev.mcskylines.world.HostWater;
import dev.mcskylines.player.DevWorld;
import dev.mcskylines.player.PlayerMode;
import dev.mcskylines.protocol.Viewport;
import dev.mcskylines.render.OverlayExporter;
import dev.mcskylines.render.SectionExporter;
import dev.mcskylines.render.SelectionExporter;
import dev.mcskylines.render.SkyExporter;
import dev.mcskylines.world.CityClock;
import dev.mcskylines.world.CityEdits;
import dev.mcskylines.shadow.ShadowWorld;
import dev.mcskylines.protocol.Trees;
import java.util.List;
import net.minecraft.client.Minecraft;
import net.minecraft.client.multiplayer.ServerData;
import net.minecraft.client.server.IntegratedServer;
import net.minecraft.network.chat.Component;

/** Owns the link as seen from the client thread: drains bridge events each frame, keeps GUEST_STATUS current, publishes PLAYER_STATE. */
final class LinkController {
	private final BridgeGuest guest;
	private BridgeState state = BridgeState.DISCONNECTED;
	private Welcome peer;
	private HostStatus hostStatus;
	private GuestStatus sentStatus;
	private String lastDisconnect = "none";
	private final PlayerMode playerMode = new PlayerMode();
	private final SectionExporter sections;
	private final OverlayExporter overlay;
	private final SelectionExporter selection;
	private final SkyExporter sky;
	private int skyErrors;
	private final CityEdits city;
	private final CityClock clock = new CityClock();
	private int exportErrors;
	private int overlayErrors;
	private static final boolean DEBUG_COMMANDS = Boolean.getBoolean("mcskylines.debugCommands");

	// Minecraft only exists here to play inside Cities: Skylines, so when CS1 shuts down (GOODBYE
	// SHUTTING_DOWN) and does not come back within QUIT_AFTER_MS, Minecraft saves and quits the normal
	// way (as SkyCraft does with Skyrim). -Dmcskylines.quitWithHost=false keeps it running.
	private static final boolean QUIT_WITH_HOST = Boolean.parseBoolean(System.getProperty("mcskylines.quitWithHost", "true"));
	private static final long QUIT_AFTER_MS = 10_000;
	private long hostGoneSinceMs = -1;
	// A hidden Minecraft nobody can see or use: if its host vanished without a goodbye (crash, kill) and
	// stays away this long after having been connected, save and quit too (SkyCraft quits when Skyrim's
	// process is gone). Owner's run 2026-10-05 left a hidden client running after the game was killed.
	private static final boolean STARTED_HIDDEN = Boolean.getBoolean("mcskylines.startHidden");
	private static final long HIDDEN_ORPHAN_QUIT_MS = 60_000;
	private boolean everConnected;
	private long linkDownSinceMs = -1;

	LinkController(BridgeGuest guest, CityEdits city) {
		this.guest = guest;
		this.city = city;
		this.sections = new SectionExporter(guest);
		this.overlay = new OverlayExporter(guest);
		this.selection = new SelectionExporter(guest);
		this.sky = new SkyExporter(guest);
	}

	/** Start of every frame: input and look arrive at host frame rate, not tick rate. */
	void frame(Minecraft mc) {
		guest.poll(e -> onEvent(mc, e));
		playerMode.beginFrame(mc);
	}

	/** After the frame is rendered. One PLAYER_STATE per frame; sendLatest keeps at most one unsent. */
	void rendered(Minecraft mc) {
		if (state != BridgeState.CONNECTED || peer == null || peer.appMinor() < 1) {
			return;
		}
		PlayerState s = playerMode.frameState(mc);
		if (s != null) {
			guest.sendLatest(AppProtocol.PLAYER_STATE, s.encode());
		}
		if (peer.appMinor() >= 2) {
			try {
				sections.frame(mc);
			} catch (RuntimeException e) {
				if (exportErrors++ < 5) {
					LOG.error(PREFIX + "block export failed", e);
				}
			}
		}
		if (peer.appMinor() >= 3) {
			try {
				overlay.frame(mc, peer.sessionId(), playerMode.active());
			} catch (RuntimeException e) {
				if (overlayErrors++ < 5) {
					LOG.error(PREFIX + "overlay export failed", e);
				}
			}
		}
		if (peer.appMinor() >= 4) {
			selection.frame(mc, playerMode.active());
		}
		if (peer.appMinor() >= 9) {
			try {
				sky.frame(mc);
			} catch (RuntimeException e) {
				if (skyErrors++ < 5) {
					LOG.error(PREFIX + "sky export failed", e);
				}
			}
		}
	}

	void tick(Minecraft mc) {
		playerMode.clientTick(mc);
		city.clientTick();
		clock.tick(mc);
		if (QUIT_WITH_HOST && STARTED_HIDDEN && everConnected && state != BridgeState.CONNECTED) {
			long now = System.currentTimeMillis();
			if (linkDownSinceMs < 0) {
				linkDownSinceMs = now;
			} else if (now - linkDownSinceMs > HIDDEN_ORPHAN_QUIT_MS) {
				linkDownSinceMs = -1;
				LOG.info(PREFIX + "hidden and Cities: Skylines has been gone for {} s; saving and quitting", HIDDEN_ORPHAN_QUIT_MS / 1000);
				mc.stop();
				return;
			}
		} else {
			linkDownSinceMs = -1;
		}
		if (QUIT_WITH_HOST && hostGoneSinceMs >= 0 && System.currentTimeMillis() - hostGoneSinceMs > QUIT_AFTER_MS) {
			hostGoneSinceMs = -1;
			LOG.info(PREFIX + "Cities: Skylines shut down {} s ago and has not come back; saving and quitting", QUIT_AFTER_MS / 1000);
			mc.stop();
			return;
		}
		if (state == BridgeState.CONNECTED) {
			GuestStatus now = currentStatus(mc);
			if (!now.equals(sentStatus) && guest.send(AppProtocol.GUEST_STATUS, now.encode())) {
				sentStatus = now;
			}
		}
	}

	void stop() {
		guest.shutdown(Goodbye.SHUTTING_DOWN, "Minecraft is exiting");
		try {
			// Bounded wait so the GOODBYE leaves before the JVM exits; bridge threads are daemons.
			guest.awaitStopped(1000);
		} catch (InterruptedException e) {
			Thread.currentThread().interrupt();
		}
	}

	private void onEvent(Minecraft mc, BridgeEvent event) {
		switch (event) {
			case BridgeEvent.StateChanged s -> {
				state = s.state();
				if (state != BridgeState.CONNECTED && PlayerMode.linked()) {
					playerMode.onLinkDown(mc);
				}
				if (state != BridgeState.CONNECTED) {
					overlay.linkDown();
					city.linkDown();
					clock.linkDown();
				}
				LOG.info(PREFIX + "link state {}{}", s.state().wireName(), s.detail().isEmpty() ? "" : " (" + s.detail() + ")");
				if (s.state() == BridgeState.CONNECTED) {
					hostGoneSinceMs = -1;
					everConnected = true;
					peer = guest.peer();
					sentStatus = null;
					city.linkUp(peer == null ? 0 : peer.appMinor());
					playerMode.onLinkUp(mc);
					sections.linkUp();
					selection.linkUp();
					sky.linkUp();
					if (peer != null) {
						chat(mc, "Connected to " + peer.peerName() + " (" + peer.peerVersion() + ")");
					}
				}
			}
			case BridgeEvent.Disconnected d -> {
				lastDisconnect = d.cause().wireName() + " code=" + d.code() + (d.reason().isEmpty() ? "" : " reason=" + d.reason());
				peer = null;
				hostStatus = null;
				sentStatus = null;
				playerMode.onLinkDown(mc);
				DynamicObstacleStore.INSTANCE.clear();
				HostWater.clear();
				ShadowWorld.clearTrees();
				overlay.linkDown();
				city.linkDown();
				clock.linkDown();
				if (d.cause() == DisconnectCause.PEER_GOODBYE && d.code() == Goodbye.SHUTTING_DOWN) {
					hostGoneSinceMs = System.currentTimeMillis();
				}
				chat(mc, d.cause() == DisconnectCause.REJECTED
					? "Cities: Skylines rejected the link: " + d.reason()
					: "Disconnected from Cities: Skylines (" + lastDisconnect + ")");
			}
			case BridgeEvent.Message m -> {
				if (m.type() == AppProtocol.COLLISION_REGION || m.type() == AppProtocol.COLLISION_RESET
						|| m.type() == AppProtocol.TREES) {
					try {
						if (m.type() == AppProtocol.COLLISION_REGION) {
							CollisionRegion region = CollisionRegion.decode(m.payload());
							if (CollisionStore.INSTANCE.accept(region)) {
								ShadowWorld.regionChanged(region.regionX(), region.regionZ());
							}
						} else if (m.type() == AppProtocol.TREES) {
							if (peer != null && peer.appMinor() >= 12) {
								ShadowWorld.acceptTrees(Trees.decode(m.payload()));
							}
						} else {
							CollisionStore.INSTANCE.accept(CollisionReset.decode(m.payload()));
							ShadowWorld.clearTrees();
						}
					} catch (ProtocolException e) {
						LOG.warn(PREFIX + "ignoring malformed collision message: {}", e.getMessage());
					}
				} else if (m.type() == AppProtocol.INPUT || m.type() == AppProtocol.ENTER_PLAYER_MODE
						|| m.type() == AppProtocol.EXIT_PLAYER_MODE) {
					try {
						switch (m.type()) {
							case AppProtocol.INPUT -> playerMode.onInput(mc, Input.decode(m.payload()));
							case AppProtocol.ENTER_PLAYER_MODE -> playerMode.onEnter(mc, EnterPlayerMode.decode(m.payload()));
							default -> {
								DynamicObstacleStore.INSTANCE.clear();
								HostWater.clear();
								playerMode.onExit(mc, ExitPlayerMode.decode(m.payload()).reason());
							}
						}
					} catch (ProtocolException e) {
						LOG.warn(PREFIX + "ignoring malformed player-mode message: {}", e.getMessage());
					}
				} else if (m.type() == AppProtocol.VIEWPORT) {
					try {
						if (peer != null && peer.appMinor() >= 3) {
							overlay.onViewport(mc, Viewport.decode(m.payload()));
						}
					} catch (ProtocolException e) {
						LOG.warn(PREFIX + "ignoring malformed VIEWPORT: {}", e.getMessage());
					}
				} else if (m.type() == AppProtocol.CITY_OPEN || m.type() == AppProtocol.BLOCK_EDITS
						|| m.type() == AppProtocol.CITY_CLOSE || m.type() == AppProtocol.EDIT_SYNC
						|| m.type() == AppProtocol.LIGHT_SOURCES || m.type() == AppProtocol.PLAYER_DATA) {
					int needs = m.type() == AppProtocol.LIGHT_SOURCES ? 8 : m.type() == AppProtocol.PLAYER_DATA ? 11 : 5;
					if (peer != null && peer.appMinor() >= needs) {
						city.deliver(m.type(), m.payload());
					}
				} else if (m.type() == AppProtocol.DYNAMIC_OBSTACLES) {
					try {
						if (peer != null && peer.appMinor() >= 7) {
							DynamicObstacleStore.INSTANCE.accept(DynamicObstacles.decode(m.payload()), System.nanoTime());
						}
					} catch (ProtocolException e) {
						LOG.warn(PREFIX + "ignoring malformed DYNAMIC_OBSTACLES: {}", e.getMessage());
					}
				} else if (m.type() == AppProtocol.WATER_SURFACE) {
					try {
						if (peer != null && peer.appMinor() >= 10) {
							HostWater.accept(WaterSurface.decode(m.payload()));
						}
					} catch (ProtocolException e) {
						LOG.warn(PREFIX + "ignoring malformed WATER_SURFACE: {}", e.getMessage());
					}
				} else if (m.type() == AppProtocol.WORLD_TIME) {
					try {
						if (peer != null && peer.appMinor() >= 6) {
							clock.deliver(WorldTime.decode(m.payload()));
						}
					} catch (ProtocolException e) {
						LOG.warn(PREFIX + "ignoring malformed WORLD_TIME: {}", e.getMessage());
					}
				} else if (m.type() == AppProtocol.DEBUG_COMMAND) {
					try {
						runDebugCommand(mc, DebugCommand.decode(m.payload()).command());
					} catch (ProtocolException e) {
						LOG.warn(PREFIX + "ignoring malformed DEBUG_COMMAND: {}", e.getMessage());
					}
				} else if (m.type() == AppProtocol.HOST_STATUS) {
					try {
						HostStatus next = HostStatus.decode(m.payload());
						if (!next.equals(hostStatus)) {
							LOG.info(PREFIX + "host status {}", next);
						}
						if (peer != null && shouldOpenWorld(hostStatus, next, peer.appMinor())) {
							playerMode.prewarmWorld(mc);
						}
						hostStatus = next;
					} catch (ProtocolException e) {
						LOG.warn(PREFIX + "ignoring malformed HOST_STATUS: {}", e.getMessage());
					}
				}
			}
		}
	}

	/** Only with -Dmcskylines.debugCommands=true and only in the city world; runs as the server console, result logged. */
	private static void runDebugCommand(Minecraft mc, String command) {
		IntegratedServer server = mc.getSingleplayerServer();
		if (!DEBUG_COMMANDS || server == null || !DevWorld.isOurs(server)) {
			LOG.info(PREFIX + "ignoring DEBUG_COMMAND '{}' ({})", command,
				!DEBUG_COMMANDS ? "-Dmcskylines.debugCommands=true not set" : "not in world " + DevWorld.NAME);
			return;
		}
		server.execute(() -> {
			LOG.info(PREFIX + "DEBUG_COMMAND /{}", command);
			server.getCommands().performPrefixedCommand(server.createCommandSourceStack().withCallback((success, result) ->
				LOG.info(PREFIX + "DEBUG_COMMAND /{} -> {} (result {})", command, success ? "success" : "failure", result)), command);
		});
	}

	/** A city just became open in CS1 (and the host speaks app minor 1+): open the city world now so ENTER_PLAYER_MODE only teleports. */
	static boolean shouldOpenWorld(HostStatus previous, HostStatus next, int appMinor) {
		return appMinor >= 1 && next.has(HostStatus.IN_CITY) && (previous == null || !previous.has(HostStatus.IN_CITY));
	}

	List<String> statusLines() {
		Welcome p = peer;
		return List.of(
			"Skylines link: " + state.wireName(),
			"Peer: " + (p == null ? "none" : p.peerName() + " " + p.peerVersion()),
			"Session: " + (p == null ? "none" : Long.toUnsignedString(p.sessionId())),
			"Host status: " + (hostStatus == null ? "none" : hostStatus),
			"Last disconnect: " + lastDisconnect);
	}

	private GuestStatus currentStatus(Minecraft mc) {
		boolean inWorld = mc.level != null;
		int flags = (inWorld ? GuestStatus.IN_WORLD : 0) | (mc.gui.screen() != null ? GuestStatus.SCREEN_OPEN : 0);
		return new GuestStatus(flags, inWorld ? worldName(mc) : "", city.pairedSaveId());
	}

	private static String worldName(Minecraft mc) {
		IntegratedServer server = mc.getSingleplayerServer();
		if (server != null) {
			return server.getWorldData().getLevelName();
		}
		ServerData remote = mc.getCurrentServer();
		return remote == null ? "" : remote.name;
	}

	private static void chat(Minecraft mc, String text) {
		if (mc.player != null) {
			mc.player.sendSystemMessage(Component.literal("[Skylines] " + text));
		}
	}
}
