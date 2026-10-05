// Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
// See minecraft/THIRD-PARTY-NOTICES.md for the full licence text.
package dev.mcskylines.render;

import com.mojang.blaze3d.platform.NativeImage;
import dev.mcskylines.client.mixin.SpriteContentsAccessor;
import dev.mcskylines.client.mixin.TextureAtlasAccessor;
import dev.mcskylines.protocol.AtlasRegion;
import java.util.ArrayList;
import java.util.List;
import java.util.function.Predicate;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.texture.SpriteContents;
import net.minecraft.client.renderer.texture.TextureAtlasSprite;
import net.minecraft.data.AtlasIds;

/**
 * Minecraft's block atlas as one RGBA8 image (top row first), built on the CPU from each sprite's first animation
 * frame, so block-atlas UVs index it unchanged. Port of SkyCraft's SkyAtlas without the item atlas, cracks and arrows.
 */
final class BlockAtlasImage {
	final int width, height;
	final byte[] rgba;
	private final Object sprites; // identity of the atlas's sprite map; changes on resource reload
	private final List<Animation> animations = new ArrayList<>();

	/** An animated sprite (water, lava, fire, ...) where it sits in the atlas, with Minecraft's frame order and timing. */
	private static final class Animation {
		NativeImage image;
		int x, y, w, h, pad;
		int[] index, time;
		int cycle, rowSize;
		boolean interpolate;
		long shown = Long.MIN_VALUE;
	}

	private BlockAtlasImage(TextureAtlasAccessor atlas) {
		this.width = atlas.mcskylines$width();
		this.height = atlas.mcskylines$height();
		this.rgba = new byte[width * height * 4];
		for (TextureAtlasSprite sprite : atlas.mcskylines$sprites().values()) {
			copy(sprite);
		}
		this.sprites = atlas.mcskylines$sprites();
	}

	static BlockAtlasImage build(Minecraft mc) {
		return new BlockAtlasImage(accessor(mc));
	}

	private static TextureAtlasAccessor accessor(Minecraft mc) {
		return (TextureAtlasAccessor) mc.getAtlasManager().getAtlasOrThrow(AtlasIds.BLOCKS);
	}

	/** True if the game's block atlas was rebuilt (resource reload) since this copy was made. */
	boolean stale(Minecraft mc) {
		return accessor(mc).mcskylines$sprites() != sprites;
	}

	int animatedSprites() {
		return animations.size();
	}

	/**
	 * Copies a sprite's first frame to where its UVs point. Sprites sit inside a padded cell; the padding is filled
	 * with the nearest edge pixel, as Minecraft does.
	 */
	private void copy(TextureAtlasSprite sprite) {
		NativeImage image = ((SpriteContentsAccessor) sprite.contents()).mcskylines$originalImage();
		int w = Math.min(sprite.contents().width(), image.getWidth());
		int h = Math.min(sprite.contents().height(), image.getHeight());
		int imageX = Math.round(sprite.getU0() * width), imageY = Math.round(sprite.getV0() * height);
		int pad = Math.max(0, Math.min(imageX - sprite.getX(), imageY - sprite.getY()));
		for (int y = -pad; y < h + pad; y++) {
			int ty = imageY + y;
			if (ty < 0 || ty >= height) {
				continue;
			}
			int sy = Math.clamp(y, 0, h - 1);
			for (int x = -pad; x < w + pad; x++) {
				int tx = imageX + x;
				if (tx >= 0 && tx < width) {
					putArgb(rgba, (ty * width + tx) * 4, image.getPixel(Math.clamp(x, 0, w - 1), sy));
				}
			}
		}
		Animation a = animationOf(sprite.contents());
		if (a != null) {
			a.image = image;
			a.x = imageX;
			a.y = imageY;
			a.w = w;
			a.h = h;
			a.pad = pad;
			animations.add(a);
		}
	}

	private static void putArgb(byte[] out, int o, int argb) {
		out[o] = (byte) (argb >> 16);
		out[o + 1] = (byte) (argb >> 8);
		out[o + 2] = (byte) argb;
		out[o + 3] = (byte) (argb >>> 24);
	}

	/** Minecraft's frame list for an animated sprite (private in SpriteContents), or null. */
	private static Animation animationOf(SpriteContents contents) {
		try {
			var field = SpriteContents.class.getDeclaredField("animatedTexture");
			field.setAccessible(true);
			Object animated = field.get(contents);
			if (animated == null) {
				return null;
			}
			Class<?> type = animated.getClass();
			var framesField = type.getDeclaredField("frames");
			var rowField = type.getDeclaredField("frameRowSize");
			var interpolateField = type.getDeclaredField("interpolateFrames");
			framesField.setAccessible(true);
			rowField.setAccessible(true);
			interpolateField.setAccessible(true);
			List<?> frames = (List<?>) framesField.get(animated);
			if (frames.size() < 2) {
				return null;
			}
			Animation a = new Animation();
			a.index = new int[frames.size()];
			a.time = new int[frames.size()];
			for (int k = 0; k < frames.size(); k++) {
				Object frame = frames.get(k);
				var indexField = frame.getClass().getDeclaredField("index");
				var timeField = frame.getClass().getDeclaredField("time");
				indexField.setAccessible(true);
				timeField.setAccessible(true);
				a.index[k] = indexField.getInt(frame);
				a.time[k] = Math.max(1, timeField.getInt(frame));
				a.cycle += a.time[k];
			}
			a.rowSize = Math.max(1, rowField.getInt(animated));
			a.interpolate = interpolateField.getBoolean(animated);
			return a;
		} catch (ReflectiveOperationException | RuntimeException e) {
			return null;
		}
	}

	/**
	 * Brings animated sprites to Minecraft's frame for this game tick (blending where the sprite interpolates) and hands
	 * each changed one to {@code send}; false from it means "not delivered, try again next time".
	 */
	void animate(long tick, Predicate<AtlasRegion> send) {
		for (Animation a : animations) {
			long t = Math.floorMod(tick, (long) a.cycle);
			int i = 0;
			while (t >= a.time[i]) {
				t -= a.time[i];
				i++;
			}
			long key = a.interpolate ? ((long) i << 20) | t : i;
			if (key == a.shown) {
				continue;
			}
			int next = (i + 1) % a.index.length;
			float blend = a.interpolate ? (float) t / a.time[i] : 0.0F;
			int pw = a.w + 2 * a.pad, ph = a.h + 2 * a.pad;
			byte[] out = new byte[pw * ph * 4];
			int fx0 = (a.index[i] % a.rowSize) * a.w, fy0 = (a.index[i] / a.rowSize) * a.h;
			int fx1 = (a.index[next] % a.rowSize) * a.w, fy1 = (a.index[next] / a.rowSize) * a.h;
			int o = 0;
			for (int y = -a.pad; y < a.h + a.pad; y++) {
				int sy = Math.clamp(y, 0, a.h - 1);
				for (int x = -a.pad; x < a.w + a.pad; x++, o += 4) {
					int sx = Math.clamp(x, 0, a.w - 1);
					int c = a.image.getPixel(fx0 + sx, fy0 + sy);
					if (blend > 0.0F) {
						c = mix(c, a.image.getPixel(fx1 + sx, fy1 + sy), blend);
					}
					putArgb(out, o, c);
				}
			}
			if (send.test(new AtlasRegion(a.x - a.pad, a.y - a.pad, pw, ph, out))) {
				a.shown = key;
			}
		}
	}

	private static int mix(int a, int b, float t) {
		int r = 0;
		for (int shift = 0; shift < 32; shift += 8) {
			int ca = (a >>> shift) & 0xFF, cb = (b >>> shift) & 0xFF;
			r |= (Math.round(ca + (cb - ca) * t) & 0xFF) << shift;
		}
		return r;
	}
}
