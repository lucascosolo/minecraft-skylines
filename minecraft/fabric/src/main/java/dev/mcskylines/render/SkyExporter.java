package dev.mcskylines.render;

import dev.mcskylines.bridge.BridgeGuest;
import dev.mcskylines.client.mixin.TextureAtlasAccessor;
import dev.mcskylines.protocol.AppProtocol;
import dev.mcskylines.protocol.SkyState;
import dev.mcskylines.protocol.SkyTextures;
import java.io.IOException;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import net.minecraft.client.CloudStatus;
import net.minecraft.client.Minecraft;
import net.minecraft.client.multiplayer.ClientLevel;
import net.minecraft.data.AtlasIds;
import net.minecraft.resources.Identifier;
import net.minecraft.server.packs.resources.Resource;
import net.minecraft.world.attribute.EnvironmentAttributeProbe;
import net.minecraft.world.attribute.EnvironmentAttributes;
import net.minecraft.world.level.dimension.DimensionType;
import org.joml.Vector3fc;
import org.joml.Vector4fc;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * Sends the values Minecraft's sky is drawn with (SKY_STATE, minor 9) four times a second while a world is loaded, and
 * the sun, moon and cloud images (SKY_TEXTURES) once per session and after every resource reload. Client thread only.
 */
public final class SkyExporter {
	private static final Logger LOG = LoggerFactory.getLogger("mcskylines");
	private static final String PREFIX = "[MinecraftSkylines] ";
	private static final long INTERVAL_MS = 250;

	private final BridgeGuest guest;
	private long lastSentMs = Long.MIN_VALUE;
	private boolean texturesSent;
	private Object atlasSprites; // identity of the block atlas's sprite map; changes on resource reload
	private int cloudWidth = 256;

	public SkyExporter(BridgeGuest guest) {
		this.guest = guest;
	}

	/** A new session: the host holds no textures. */
	public void linkUp() {
		texturesSent = false;
		lastSentMs = Long.MIN_VALUE;
	}

	public void frame(Minecraft mc) {
		ClientLevel level = mc.level;
		if (level == null) {
			return;
		}
		Object sprites = ((TextureAtlasAccessor) mc.getAtlasManager().getAtlasOrThrow(AtlasIds.BLOCKS)).mcskylines$sprites();
		if (!texturesSent || sprites != atlasSprites) {
			atlasSprites = sprites;
			texturesSent = sendTextures(mc);
		}
		long now = System.currentTimeMillis();
		// lastSentMs starts at Long.MIN_VALUE ("never"); now - MIN_VALUE overflows to a negative number, which read as
		// "too soon" for ever, so no SKY_STATE was ever sent (owner, 2026-10-06: still the vanilla CS1 sky).
		if (lastSentMs != Long.MIN_VALUE && now - lastSentMs < INTERVAL_MS) {
			return;
		}
		lastSentMs = now;
		float partial = mc.getDeltaTracker().getGameTimeDeltaPartialTick(false);
		EnvironmentAttributeProbe probe = mc.gameRenderer.mainCamera().attributeProbe();
		Vector3fc sky = probe.getValue(EnvironmentAttributes.SKY_COLOR, partial);
		Vector3fc fog = probe.getValue(EnvironmentAttributes.FOG_COLOR, partial);
		Vector4fc sunrise = probe.getValue(EnvironmentAttributes.SUNRISE_SUNSET_COLOR, partial);
		Vector4fc cloud = probe.getValue(EnvironmentAttributes.CLOUD_COLOR, partial);
		boolean overworldSky = level.dimensionType().skybox() == DimensionType.Skybox.OVERWORLD;
		int flags = (overworldSky ? SkyState.FLAG_SKY : 0)
				| (overworldSky && mc.options.getCloudStatus() != CloudStatus.OFF ? SkyState.FLAG_CLOUDS : 0);
		SkyState s = new SkyState(flags,
				new float[] {sky.x(), sky.y(), sky.z()},
				new float[] {fog.x(), fog.y(), fog.z()},
				new float[] {sunrise.x(), sunrise.y(), sunrise.z(), sunrise.w()},
				probe.getValue(EnvironmentAttributes.STAR_BRIGHTNESS, partial),
				level.getRainLevel(partial),
				probe.getValue(EnvironmentAttributes.MOON_PHASE, partial).index(),
				new float[] {cloud.x(), cloud.y(), cloud.z(), cloud.w()},
				probe.getValue(EnvironmentAttributes.CLOUD_HEIGHT, partial),
				SkyFormulas.cloudOffset(level.getGameTime(), partial, cloudWidth),
				mc.isPaused() ? 0f : 0.6f);
		guest.sendLatest(AppProtocol.SKY_STATE, s.encode());
	}

	private boolean sendTextures(Minecraft mc) {
		List<SkyTextures.Texture> list = new ArrayList<>();
		add(mc, list, SkyTextures.SUN, 0, SkyFormulas.SUN_TEXTURE);
		for (int phase = 0; phase < SkyState.MOON_PHASES; phase++) {
			add(mc, list, SkyTextures.MOON, phase, SkyFormulas.moonTexture(phase));
		}
		byte[] clouds = add(mc, list, SkyTextures.CLOUDS, 0, SkyFormulas.CLOUDS_TEXTURE);
		if (clouds != null && clouds.length >= 24) {
			// PNG IHDR width, big-endian at offset 16; the cloud scroll wraps at width * 400 ticks.
			int w = ((clouds[16] & 0xFF) << 24) | ((clouds[17] & 0xFF) << 16) | ((clouds[18] & 0xFF) << 8) | (clouds[19] & 0xFF);
			cloudWidth = w > 0 ? w : 256;
		}
		boolean ok = guest.send(AppProtocol.SKY_TEXTURES, new SkyTextures(list).encode());
		LOG.info(PREFIX + "sky textures: {} images {}", list.size(), ok ? "sent" : "NOT sent (no session)");
		return ok;
	}

	private static byte[] add(Minecraft mc, List<SkyTextures.Texture> list, int kind, int phase, String path) {
		Optional<Resource> r = mc.getResourceManager().getResource(Identifier.withDefaultNamespace(path));
		if (r.isEmpty()) {
			LOG.warn(PREFIX + "sky texture {} not found", path);
			return null;
		}
		try (InputStream in = r.get().open()) {
			byte[] data = in.readNBytes(SkyFormulas.MAX_TEXTURE_BYTES + 1);
			if (data.length > SkyFormulas.MAX_TEXTURE_BYTES) {
				LOG.warn(PREFIX + "sky texture {} is over {} bytes; left out", path, SkyFormulas.MAX_TEXTURE_BYTES);
				return null;
			}
			list.add(new SkyTextures.Texture(kind, phase, SkyTextures.PNG, data));
			return data;
		} catch (IOException e) {
			LOG.warn(PREFIX + "sky texture {} unreadable: {}", path, e.getMessage());
			return null;
		}
	}
}
