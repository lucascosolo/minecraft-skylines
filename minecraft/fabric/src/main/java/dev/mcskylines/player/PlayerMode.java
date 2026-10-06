// Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
// See minecraft/THIRD-PARTY-NOTICES.md for the full licence text.
package dev.mcskylines.player;

import dev.mcskylines.client.mixin.CameraAccessor;
import dev.mcskylines.collision.CollisionStore;
import dev.mcskylines.protocol.EnterPlayerMode;
import dev.mcskylines.protocol.Input;
import dev.mcskylines.protocol.PlayerState;
import net.minecraft.client.Camera;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.sounds.SoundSource;
import net.minecraft.world.phys.Vec3;
import org.lwjgl.sdl.SDLVideo;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * The guest side of player mode, on the render thread: input replay, look, teleport and hold, freeze, and the
 * per-frame PLAYER_STATE. Ports SkyCraft's SkyClient (beginFrame, requestTeleport, holdUntilReady,
 * freezeWhileUnlinked, publishTick/afterRender).
 */
public final class PlayerMode {
	private static final Logger LOG = LoggerFactory.getLogger("mcskylines");
	private static final boolean START_HIDDEN = Boolean.getBoolean("mcskylines.startHidden");
	private static volatile boolean linked;

	private final TeleportHold hold = new TeleportHold();
	private boolean active;
	private boolean wantWorld;
	private boolean hiddenOnce;
	private Vec3 freezePos;
	private float yaw, pitch;
	private boolean haveLook;
	private int tickSeq;

	/** Read by the focus, grab, pause and frame-rate mixins. */
	public static boolean linked() {
		return linked;
	}

	public boolean active() {
		return active;
	}

	public void onLinkUp(Minecraft mc) {
		linked = true;
		mc.options.pauseOnLostFocus = false;
	}

	public void onLinkDown(Minecraft mc) {
		linked = false;
		leave(mc);
	}

	public void onEnter(Minecraft mc, EnterPlayerMode e) {
		LOG.info("[MinecraftSkylines] ENTER_PLAYER_MODE seq={} at {} {} {}", Integer.toUnsignedString(e.teleportSeq()), e.x(), e.y(), e.z());
		active = true;
		freezePos = null;
		yaw = e.yaw();
		pitch = e.pitch();
		haveLook = true;
		hold.request(new TeleportHold.Target(e.teleportSeq(), e.x(), e.y(), e.z(), e.yaw(), e.pitch()));
		if (mc.level == null) {
			wantWorld = true;
			DevWorld.request();
		}
	}

	/** CS1 has a city open: open or create the city world in the background if none is loaded (window stays as it is). */
	public void prewarmWorld(Minecraft mc) {
		if (mc.level == null && !wantWorld) {
			LOG.info("[MinecraftSkylines] city open in Cities: Skylines; opening world {} ahead of player mode", DevWorld.NAME);
			wantWorld = true;
			DevWorld.request();
		}
	}

	public void onExit(Minecraft mc, String reason) {
		LOG.info("[MinecraftSkylines] EXIT_PLAYER_MODE ({})", reason);
		leave(mc);
	}

	public void onInput(Minecraft mc, Input input) {
		if (!active) {
			return;
		}
		yaw = input.yaw();
		pitch = input.pitch();
		haveLook = true;
		for (Input.Event e : input.events()) {
			InputReplay.dispatch(mc, e);
		}
	}

	/** Exit or link loss: lift every key, stop any hold without acking, and pin the player where it is. */
	private void leave(Minecraft mc) {
		if (!active) {
			return;
		}
		active = false;
		wantWorld = false;
		haveLook = false;
		hold.cancel();
		InputReplay.releaseAll(mc);
		LocalPlayer player = mc.player;
		freezePos = player != null ? player.position() : null;
	}

	/** Start of Minecraft.runTick, after the bridge was drained. */
	public void beginFrame(Minecraft mc) {
		if (START_HIDDEN && !hiddenOnce) {
			hiddenOnce = true;
			SDLVideo.SDL_HideWindow(mc.getWindow().handle());
			mc.options.getSoundSourceOptionInstance(SoundSource.MUSIC).set(0.0);
			mc.getMusicManager().stopPlaying();
			LOG.info("[MinecraftSkylines] window hidden (-Dmcskylines.startHidden=true)");
		}
		if (wantWorld && mc.level == null) {
			DevWorld.openWhenReady(mc);
		} else {
			wantWorld = false;
		}
		LocalPlayer player = mc.player;
		if (!active || player == null || mc.gui.screen() != null) {
			return;
		}
		if (linked && !mc.mouseHandler.isMouseGrabbed()) {
			mc.mouseHandler.grabMouse(); // the real grab is suppressed by InputConstantsMixin; this only sets MC's state
		}
		if (haveLook) {
			player.setYRot(yaw);
			player.setXRot(pitch);
			player.yRotO = yaw;
			player.xRotO = pitch;
		}
	}

	/** End of every client tick. */
	public void clientTick(Minecraft mc) {
		LocalPlayer player = mc.player;
		if (player == null || mc.level == null) {
			return;
		}
		tickSeq++;
		long now = System.currentTimeMillis();
		TeleportHold.Target t = hold.takePending(now);
		if (t != null) {
			teleport(mc, player, t);
		}
		TeleportHold.Target h = hold.holdTarget();
		if (h != null) {
			boolean ready = CollisionStore.INSTANCE.regionsLoadedAround(h.x(), h.z(), 1);
			if (hold.update(now, ready)) {
				pin(player, new Vec3(h.x(), h.y(), h.z()));
			} else {
				LOG.info("[MinecraftSkylines] released hold, teleportAck={}", Integer.toUnsignedString(hold.ack()));
			}
		} else if (!active && freezePos != null) {
			pin(player, freezePos);
		}
	}

	private static void pin(LocalPlayer player, Vec3 pos) {
		player.setDeltaMovement(Vec3.ZERO);
		player.setPos(pos.x, pos.y, pos.z);
		player.xo = pos.x;
		player.yo = pos.y;
		player.zo = pos.z;
		player.resetFallDistance();
	}

	private static void teleport(Minecraft mc, LocalPlayer player, TeleportHold.Target t) {
		pin(player, new Vec3(t.x(), t.y(), t.z()));
		player.setYRot(t.yaw());
		player.setXRot(t.pitch());
		var server = mc.getSingleplayerServer();
		if (server != null) {
			var uuid = player.getUUID();
			server.execute(() -> {
				ServerPlayer sp = server.getPlayerList().getPlayer(uuid);
				if (sp != null) {
					sp.teleportTo(t.x(), t.y(), t.z());
					sp.setYRot(t.yaw());
					sp.setXRot(t.pitch());
					sp.resetFallDistance();
				}
			});
		}
	}

	/** After GameRenderer.render(): this frame's state, or null when no world is loaded. */
	public PlayerState frameState(Minecraft mc) {
		LocalPlayer player = mc.player;
		if (player == null || mc.level == null) {
			return null;
		}
		float partial = mc.getDeltaTracker().getGameTimeDeltaPartialTick(false);
		Vec3 feet = player.getPosition(partial);
		Camera camera = mc.gameRenderer.mainCamera();
		Vec3 eye = camera.isDetached() ? player.getEyePosition(partial) : camera.position();
		CameraAccessor eyes = (CameraAccessor) camera;
		int flags = PlayerState.IN_WORLD
			| (player.onGround() ? PlayerState.ON_GROUND : 0)
			| (player.isShiftKeyDown() ? PlayerState.SNEAKING : 0)
			| (player.isSprinting() ? PlayerState.SPRINTING : 0)
			| (player.isSwimming() ? PlayerState.SWIMMING : 0)
			| (player.getAbilities().flying ? PlayerState.FLYING : 0)
			| (player.isDeadOrDying() ? PlayerState.DEAD : 0)
			| (hold.held() ? PlayerState.HELD : 0);
		return new PlayerState(flags, hold.ack(), feet.x, feet.y, feet.z, eye.x, eye.y, eye.z,
			player.getYRot(), player.getXRot(), camera.getFov(), tickSeq,
			player.xo, player.yo, player.zo, player.getX(), player.getY(), player.getZ(),
			eyes.mcskylines$eyeHeightOld(), eyes.mcskylines$eyeHeight(), partial,
			mc.level.tickRateManager().millisecondsPerTick());
	}
}
