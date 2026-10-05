// Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
// See minecraft/THIRD-PARTY-NOTICES.md for the full licence text.
package dev.mcskylines.render;

import com.mojang.blaze3d.pipeline.RenderTarget;
import com.mojang.blaze3d.systems.RenderSystem;
import com.mojang.renderpearl.api.GpuFormat;
import com.mojang.renderpearl.api.buffers.GpuBuffer;
import com.mojang.renderpearl.api.buffers.GpuBufferSlice;
import com.mojang.renderpearl.api.textures.GpuTexture;
import dev.mcskylines.bridge.BridgeGuest;
import dev.mcskylines.bridge.overlay.OverlayWriter;
import dev.mcskylines.player.InputReplay;
import dev.mcskylines.protocol.AppProtocol;
import dev.mcskylines.protocol.OverlayOffer;
import dev.mcskylines.protocol.OverlayStop;
import dev.mcskylines.protocol.Viewport;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.file.Files;
import java.nio.file.Path;
import net.minecraft.client.Minecraft;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * The GUI overlay (protocol 1.3): Minecraft renders HUD, hand and screens at the host's viewport size on a transparent
 * clear (LevelRendererMixin skips the world), and each frame's main target is copied to one of three GPU staging
 * buffers. The copy completes asynchronously; a later frame maps the newest finished one and copies it, rows flipped
 * to top-down, into the shared-memory back slot. GUI pipelines blend (SRC_ALPHA, 1-SRC_ALPHA, ONE, 1-SRC_ALPHA) onto
 * (0,0,0,0), which leaves premultiplied RGBA, so no per-pixel conversion is needed. Port of SkyCraft's FrameExporter.
 */
public final class OverlayExporter {
	private static final Logger LOG = LoggerFactory.getLogger("mcskylines");
	private static final String PREFIX = "[MinecraftSkylines] ";
	private static final int MAX_SIZE = 8192;
	private static final int STAGING = 3;
	private static final int FREE = 0;
	private static final int PENDING = 1;
	private static final int READY = 2;
	private static volatile boolean publishing;

	private final BridgeGuest guest;
	private final Staging[] staging = new Staging[STAGING];
	private Viewport viewport;
	private OverlayWriter writer;
	private boolean offered;
	private long generation = System.currentTimeMillis();
	private long nextFrameId = 1;
	private long copyNanos;
	private int copies;
	private boolean loggedFormat;

	private static final class Staging {
		GpuBuffer buffer;
		int width;
		int height;
		volatile int state = FREE;
		long frameId;
	}

	public OverlayExporter(BridgeGuest guest) {
		this.guest = guest;
	}

	/** Read by LevelRendererMixin: the world is drawn by the host while the overlay is published. */
	public static boolean publishing() {
		return publishing;
	}

	/** VIEWPORT: size the (hidden) window, and so the main render target, to the host's screen. */
	public void onViewport(Minecraft mc, Viewport v) {
		int w = Math.min(v.width(), MAX_SIZE);
		int h = Math.min(v.height(), MAX_SIZE);
		if (w < 1 || h < 1) {
			return;
		}
		InputReplay.setHostViewport(v.width(), v.height());
		if (viewport == null || viewport.width() != v.width() || viewport.height() != v.height()) {
			mc.getWindow().setWindowed(w, h);
			LOG.info(PREFIX + "overlay: sizing Minecraft to the host viewport {}x{}", w, h);
		}
		if (v.uiScale() > 0 && mc.options.guiScale().get() != Math.round(v.uiScale())) {
			mc.options.guiScale().set(Math.round(v.uiScale()));
			mc.resizeGui();
		}
		viewport = new Viewport(w, h, v.uiScale());
		if (writer != null && (w > writer.maxWidth() || h > writer.maxHeight())) {
			offered = false; // recreated bigger on the next frame
		}
	}

	/** After GameRenderer.render(): offer the file once in a world, publish while in player mode. */
	public void frame(Minecraft mc, long sessionId, boolean playerMode) {
		if (mc.level == null) {
			if (offered && guest.send(AppProtocol.OVERLAY_STOP, new OverlayStop().encode())) {
				offered = false;
				LOG.info(PREFIX + "overlay: stopped (left the world)");
			}
			stopPublishing();
			return;
		}
		if (viewport == null) {
			return;
		}
		if (!offered && !offer(sessionId)) {
			return;
		}
		if (!playerMode) {
			stopPublishing();
			return;
		}
		publishing = true;
		shipReadyFrame();
		capture(mc);
	}

	/** Link lost: nothing to tell the host; the next link starts from a fresh VIEWPORT. */
	public void linkDown() {
		offered = false;
		viewport = null;
		stopPublishing();
	}

	private void stopPublishing() {
		publishing = false;
		for (Staging s : staging) {
			if (s != null && s.state == READY) {
				s.state = FREE;
			}
		}
	}

	private boolean offer(long sessionId) {
		try {
			int maxW = Math.max(viewport.width(), writer == null ? 0 : writer.maxWidth());
			int maxH = Math.max(viewport.height(), writer == null ? 0 : writer.maxHeight());
			if (writer != null) {
				writer.close();
			}
			// One file per user, reused across sessions: /dev/shm is RAM and nothing here may delete
			// files, so a per-session name would pile up until reboot. The generation (seeded from the
			// clock, so a restarted Minecraft never repeats one) tells the host to remap.
			writer = OverlayWriter.open(overlayDir().resolve("mcskylines-overlay-" + System.getProperty("user.name", "player")),
				maxW, maxH, ++generation);
		} catch (IOException | RuntimeException e) {
			LOG.error(PREFIX + "overlay: cannot create the shared-memory file", e);
			writer = null;
			viewport = null; // retried on the next VIEWPORT
			return false;
		}
		OverlayOffer o = new OverlayOffer(writer.path().toString(), writer.maxWidth(), writer.maxHeight(),
			OverlayWriter.SLOT_COUNT, writer.generation());
		offered = guest.send(AppProtocol.OVERLAY_OFFER, o.encode());
		if (offered) {
			LOG.info(PREFIX + "overlay: offered {} ({}x{}, generation {})", o.path(), o.maxWidth(), o.maxHeight(), o.generation());
		}
		return offered;
	}

	/** /dev/shm, else $XDG_RUNTIME_DIR, else the system temp dir. */
	static Path overlayDir() {
		Path shm = Path.of("/dev/shm");
		if (Files.isDirectory(shm) && Files.isWritable(shm)) {
			return shm;
		}
		String xdg = System.getenv("XDG_RUNTIME_DIR");
		Path dir = xdg != null && !xdg.isEmpty() && Files.isWritable(Path.of(xdg)) ? Path.of(xdg) : Path.of(System.getProperty("java.io.tmpdir"));
		LOG.warn(PREFIX + "overlay: /dev/shm is not writable; using {}", dir);
		return dir;
	}

	private void capture(Minecraft mc) {
		RenderTarget target = mc.gameRenderer.mainRenderTarget();
		GpuTexture color = target.getColorTexture();
		int width = target.width;
		int height = target.height;
		if (color == null || width > writer.maxWidth() || height > writer.maxHeight()) {
			return;
		}
		if (!loggedFormat) {
			loggedFormat = true;
			LOG.info(PREFIX + "overlay: capturing {}x{} format {} on {}", width, height, color.getFormat(),
				RenderSystem.getDevice().getDeviceInfo().backendName());
		}
		if (color.getFormat() != GpuFormat.RGBA8_UNORM) {
			return;
		}
		Staging slot = null;
		for (int i = 0; i < STAGING && slot == null; i++) {
			if (staging[i] == null) {
				staging[i] = new Staging();
			}
			if (staging[i].state == FREE) {
				slot = staging[i];
			}
		}
		if (slot == null) {
			return; // all three still in flight: skip this frame rather than wait on the GPU
		}
		if (slot.buffer == null || slot.width != width || slot.height != height) {
			if (slot.buffer != null) {
				slot.buffer.close();
			}
			slot.buffer = RenderSystem.getDevice().createBuffer(() -> "mcskylines overlay readback",
				GpuBuffer.USAGE_MAP_READ | GpuBuffer.USAGE_COPY_DST, (long) width * height * 4L);
			slot.width = width;
			slot.height = height;
		}
		Staging captured = slot;
		captured.state = PENDING;
		captured.frameId = nextFrameId++;
		RenderSystem.getDevice().createCommandEncoder().copyTextureToBuffer(color, captured.buffer, 0L, () -> captured.state = READY, 0);
	}

	/** Maps the newest finished readback and publishes it; older finished ones are dropped. */
	private void shipReadyFrame() {
		Staging newest = null;
		for (Staging s : staging) {
			if (s != null && s.state == READY && (newest == null || s.frameId > newest.frameId)) {
				newest = s;
			}
		}
		if (newest == null) {
			return;
		}
		if (newest.width <= writer.maxWidth() && newest.height <= writer.maxHeight()) {
			long t0 = System.nanoTime();
			int row = newest.width * 4;
			ByteBuffer dst = writer.backPixels();
			try (GpuBufferSlice.MappedView view = newest.buffer.map(true, false)) {
				ByteBuffer src = view.data();
				for (int y = 0; y < newest.height; y++) {
					dst.put(y * row, src, (newest.height - 1 - y) * row, row); // GPU readback is bottom-up
				}
			}
			writer.publish(newest.width, newest.height, 0, newest.frameId);
			copyNanos += System.nanoTime() - t0;
			if (++copies == 600) {
				LOG.info(PREFIX + "overlay: {}x{}, average copy {} ms over {} frames", newest.width, newest.height,
					String.format("%.2f", copyNanos / 1e6 / copies), copies);
				copies = 0;
				copyNanos = 0;
			}
		}
		for (Staging s : staging) {
			if (s != null && s.state == READY && s.frameId <= newest.frameId) {
				s.state = FREE;
			}
		}
	}
}
