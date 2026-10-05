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
import dev.mcskylines.protocol.AppProtocol;
import dev.mcskylines.protocol.CollisionRegion;
import dev.mcskylines.protocol.CollisionReset;
import dev.mcskylines.protocol.DebugCommand;
import dev.mcskylines.protocol.EnterPlayerMode;
import dev.mcskylines.protocol.ExitPlayerMode;
import dev.mcskylines.protocol.GuestStatus;
import dev.mcskylines.protocol.HostStatus;
import dev.mcskylines.protocol.Input;
import dev.mcskylines.protocol.PlayerState;
import dev.mcskylines.player.DevWorld;
import dev.mcskylines.player.PlayerMode;
import dev.mcskylines.protocol.Viewport;
import dev.mcskylines.render.OverlayExporter;
import dev.mcskylines.render.SectionExporter;
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
	private int exportErrors;
	private int overlayErrors;
	private static final boolean DEBUG_COMMANDS = Boolean.getBoolean("mcskylines.debugCommands");

	// Minecraft only exists here to play inside Cities: Skylines, so when CS1 shuts down (GOODBYE
	// SHUTTING_DOWN) and does not come back within QUIT_AFTER_MS, Minecraft saves and quits the normal
	// way (as SkyCraft does with Skyrim). -Dmcskylines.quitWithHost=false keeps it running.
	private static final boolean QUIT_WITH_HOST = Boolean.parseBoolean(System.getProperty("mcskylines.quitWithHost", "true"));
	private static final long QUIT_AFTER_MS = 10_000;
	private long hostGoneSinceMs = -1;

	LinkController(BridgeGuest guest) {
		this.guest = guest;
		this.sections = new SectionExporter(guest);
		this.overlay = new OverlayExporter(guest);
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
	}

	void tick(Minecraft mc) {
		playerMode.clientTick(mc);
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
				}
				LOG.info(PREFIX + "link state {}{}", s.state().wireName(), s.detail().isEmpty() ? "" : " (" + s.detail() + ")");
				if (s.state() == BridgeState.CONNECTED) {
					hostGoneSinceMs = -1;
					peer = guest.peer();
					sentStatus = null;
					playerMode.onLinkUp(mc);
					sections.linkUp();
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
				overlay.linkDown();
				if (d.cause() == DisconnectCause.PEER_GOODBYE && d.code() == Goodbye.SHUTTING_DOWN) {
					hostGoneSinceMs = System.currentTimeMillis();
				}
				chat(mc, d.cause() == DisconnectCause.REJECTED
					? "Cities: Skylines rejected the link: " + d.reason()
					: "Disconnected from Cities: Skylines (" + lastDisconnect + ")");
			}
			case BridgeEvent.Message m -> {
				if (m.type() == AppProtocol.COLLISION_REGION || m.type() == AppProtocol.COLLISION_RESET) {
					try {
						if (m.type() == AppProtocol.COLLISION_REGION) {
							CollisionStore.INSTANCE.accept(CollisionRegion.decode(m.payload()));
						} else {
							CollisionStore.INSTANCE.accept(CollisionReset.decode(m.payload()));
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
							default -> playerMode.onExit(mc, ExitPlayerMode.decode(m.payload()).reason());
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

	/** Only with -Dmcskylines.debugCommands=true and only in the dev world; runs as the server console, result logged. */
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

	/** A city just became open in CS1 (and the host speaks app minor 1+): open the dev world now so ENTER_PLAYER_MODE only teleports. */
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

	private static GuestStatus currentStatus(Minecraft mc) {
		boolean inWorld = mc.level != null;
		int flags = (inWorld ? GuestStatus.IN_WORLD : 0) | (mc.gui.screen() != null ? GuestStatus.SCREEN_OPEN : 0);
		return new GuestStatus(flags, inWorld ? worldName(mc) : "", GuestStatus.NO_SAVE);
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
