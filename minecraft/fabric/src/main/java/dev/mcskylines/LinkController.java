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
import dev.mcskylines.protocol.EnterPlayerMode;
import dev.mcskylines.protocol.ExitPlayerMode;
import dev.mcskylines.protocol.GuestStatus;
import dev.mcskylines.protocol.HostStatus;
import dev.mcskylines.protocol.Input;
import dev.mcskylines.protocol.PlayerState;
import dev.mcskylines.player.PlayerMode;
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

	LinkController(BridgeGuest guest) {
		this.guest = guest;
	}

	/** Start of every frame: input and look arrive at host frame rate, not tick rate. */
	void frame(Minecraft mc) {
		guest.poll(e -> onEvent(mc, e));
		playerMode.beginFrame(mc);
	}

	/** After the frame is rendered. One PLAYER_STATE per frame; see OPEN note on bridge-side coalescing. */
	void rendered(Minecraft mc) {
		if (state != BridgeState.CONNECTED || peer == null || peer.appMinor() < 1) {
			return;
		}
		PlayerState s = playerMode.frameState(mc);
		if (s != null) {
			guest.send(AppProtocol.PLAYER_STATE, s.encode());
		}
	}

	void tick(Minecraft mc) {
		playerMode.clientTick(mc);
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
				LOG.info(PREFIX + "link state {}{}", s.state().wireName(), s.detail().isEmpty() ? "" : " (" + s.detail() + ")");
				if (s.state() == BridgeState.CONNECTED) {
					peer = guest.peer();
					sentStatus = null;
					playerMode.onLinkUp(mc);
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
				} else if (m.type() == AppProtocol.HOST_STATUS) {
					try {
						HostStatus next = HostStatus.decode(m.payload());
						if (!next.equals(hostStatus)) {
							LOG.info(PREFIX + "host status {}", next);
						}
						hostStatus = next;
					} catch (ProtocolException e) {
						LOG.warn(PREFIX + "ignoring malformed HOST_STATUS: {}", e.getMessage());
					}
				}
			}
		}
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
