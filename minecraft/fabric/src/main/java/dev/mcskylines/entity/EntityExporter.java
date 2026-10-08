package dev.mcskylines.entity;

import com.mojang.blaze3d.platform.NativeImage;
import com.mojang.blaze3d.vertex.PoseStack;
import dev.mcskylines.bridge.BridgeGuest;
import dev.mcskylines.client.mixin.ModelPartAccessor;
import dev.mcskylines.client.mixin.RenderSetupAccessor;
import dev.mcskylines.client.mixin.RenderTypeAccessor;
import dev.mcskylines.client.mixin.SpriteContentsAccessor;
import dev.mcskylines.client.mixin.TextureBindingAccessor;
import dev.mcskylines.protocol.AppProtocol;
import dev.mcskylines.protocol.EntityModel;
import dev.mcskylines.protocol.EntityStates;
import dev.mcskylines.protocol.EntityTexture;
import dev.mcskylines.render.AtlasPng;
import java.io.IOException;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.IdentityHashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import net.minecraft.client.Minecraft;
import net.minecraft.client.model.Model;
import net.minecraft.client.model.geom.ModelPart;
import net.minecraft.client.model.geom.builders.UVPair;
import net.minecraft.client.renderer.entity.EntityRenderDispatcher;
import net.minecraft.client.renderer.entity.state.EntityRenderState;
import net.minecraft.client.renderer.entity.state.LivingEntityRenderState;
import net.minecraft.client.renderer.rendertype.RenderType;
import net.minecraft.client.renderer.state.level.CameraRenderState;
import net.minecraft.client.renderer.texture.TextureAtlasSprite;
import net.minecraft.client.resources.model.geometry.BakedQuad;
import net.minecraft.client.resources.model.geometry.ItemQuads;
import net.minecraft.resources.Identifier;
import net.minecraft.server.packs.resources.Resource;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.phys.Vec3;
import org.joml.Matrix4f;
import org.joml.Quaternionf;
import org.joml.Vector3fc;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * Minor 14: every client tick, the complete set of entities within 96 m (horizontally) of the player, posed by
 * Minecraft's own renderers (captured through {@link EntityCapture}), as ENTITY_STATES; each model and texture is sent
 * once per connection before its first use. Client thread only.
 */
public final class EntityExporter {
	private static final Logger LOG = LoggerFactory.getLogger("mcskylines");
	private static final double RANGE = 96;
	private static final int OVERLAY_HURT_V = 3;

	private final BridgeGuest guest;
	private final EntityCapture capture = new EntityCapture();
	private final Map<Model<?>, ModelInfo> models = new IdentityHashMap<>();
	private final Map<ItemQuads, ModelInfo> itemModels = new IdentityHashMap<>();
	private final Map<Identifier, Integer> textures = new HashMap<>();
	private int nextModel = 1, nextTexture = 1, seq, errors;
	private boolean sentEmpty;

	private record ModelInfo(int id, int texture, List<ModelPart> parts) {
	}

	public EntityExporter(BridgeGuest guest) {
		this.guest = guest;
	}

	public void linkUp() {
		models.clear();
		itemModels.clear();
		textures.clear();
		seq = 0;
		sentEmpty = false;
	}

	public void tick(Minecraft mc) {
		if (mc.level == null || mc.player == null) {
			if (!sentEmpty) {
				sentEmpty = send(List.of());
			}
			return;
		}
		EntityRenderDispatcher dispatcher = mc.getEntityRenderDispatcher();
		CameraRenderState camera = new CameraRenderState();
		camera.pos = mc.player.position();
		camera.orientation = new Quaternionf();
		List<EntityStates.Entity> out = new ArrayList<>();
		Vec3 p = mc.player.position();
		for (Entity e : mc.level.entitiesForRendering()) {
			if (out.size() >= EntityStates.MAX_ENTITIES) {
				break;
			}
			if (e == mc.player || dev.mcskylines.world.CitizenProxies.isProxy(e)) {
				continue;
			}
			double dx = e.getX() - p.x, dz = e.getZ() - p.z;
			if (dx * dx + dz * dz > RANGE * RANGE) {
				continue;
			}
			try {
				EntityStates.Entity s = capture(mc, dispatcher, camera, e);
				if (s != null) {
					out.add(s);
				}
			} catch (RuntimeException ex) {
				if (errors++ < 5) {
					LOG.warn("[MinecraftSkylines] entity capture failed for {}", e.getType(), ex);
				}
			}
		}
		sentEmpty = send(out) && out.isEmpty();
	}

	private boolean send(List<EntityStates.Entity> entities) {
		return guest.sendLatest(AppProtocol.ENTITY_STATES, new EntityStates(seq++, entities).encode());
	}

	private EntityStates.Entity capture(Minecraft mc, EntityRenderDispatcher dispatcher, CameraRenderState camera, Entity e) {
		EntityRenderState state = dispatcher.extractEntity(e, 1.0f);
		if (state.isInvisible) {
			return null;
		}
		capture.reset();
		dispatcher.submit(state, camera, 0, 0, 0, new PoseStack(), capture);
		List<EntityStates.Draw> draws = new ArrayList<>();
		for (EntityCapture.ModelSubmit m : capture.models) {
			if (draws.size() >= EntityStates.MAX_DRAWS) {
				break;
			}
			ModelInfo info = modelInfo(mc, m.model(), m.renderType());
			if (info != null) {
				draws.add(draw(info, m.pose(), color(m.tint(), m.overlay())));
			}
		}
		for (EntityCapture.ItemSubmit i : capture.items) {
			if (draws.size() >= EntityStates.MAX_DRAWS) {
				break;
			}
			ModelInfo info = itemModel(i.quads());
			if (info != null) {
				draws.add(new EntityStates.Draw(info.id(), info.texture(), -1, affine(i.pose()), new float[] {0, 0, 0, 0, 0, 0, 1, 1, 1}, new byte[1]));
			}
		}
		if (draws.isEmpty()) {
			return null;
		}
		float body = e.getYRot(), head = e.getYHeadRot(), pitch = e.getXRot();
		if (state instanceof LivingEntityRenderState l) {
			body = l.bodyRot;
			head = l.yRot;
			pitch = l.xRot;
		}
		return new EntityStates.Entity(e.getId(), (float) state.x, (float) state.y, (float) state.z, body, head, pitch, draws);
	}

	private static EntityStates.Draw draw(ModelInfo info, Matrix4f pose, int color) {
		List<ModelPart> parts = info.parts();
		float[] poses = new float[parts.size() * EntityStates.POSE_FLOATS];
		byte[] flags = new byte[parts.size()];
		for (int i = 0; i < parts.size(); i++) {
			ModelPart q = parts.get(i);
			int o = i * EntityStates.POSE_FLOATS;
			poses[o] = q.x;
			poses[o + 1] = q.y;
			poses[o + 2] = q.z;
			poses[o + 3] = q.xRot;
			poses[o + 4] = q.yRot;
			poses[o + 5] = q.zRot;
			poses[o + 6] = q.xScale;
			poses[o + 7] = q.yScale;
			poses[o + 8] = q.zScale;
			flags[i] = (byte) ((q.visible ? 0 : EntityStates.FLAG_HIDDEN) | (q.skipDraw ? EntityStates.FLAG_SKIP : 0));
		}
		return new EntityStates.Draw(info.id(), info.texture(), color, affine(pose), poses, flags);
	}

	/** ARGB tint to wire RGBA bytes (u32 little-endian), reddened while the hurt overlay is on. */
	static int color(int argb, int overlay) {
		int a = argb >>> 24, r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
		if ((overlay >>> 16) == OVERLAY_HURT_V) {
			g = g * 6 / 10;
			b = b * 6 / 10;
		}
		return r | (g << 8) | (b << 16) | (a << 24);
	}

	static float[] affine(Matrix4f m) {
		float[] out = new float[12];
		for (int r = 0; r < 3; r++) {
			for (int c = 0; c < 4; c++) {
				out[r * 4 + c] = m.getRowColumn(r, c);
			}
		}
		return out;
	}

	private ModelInfo modelInfo(Minecraft mc, Model<?> model, RenderType renderType) {
		ModelInfo known = models.get(model);
		int texture = texture(mc, renderType);
		if (texture < 0) {
			return null;
		}
		if (known != null) {
			return known.texture() == texture ? known : new ModelInfo(known.id(), texture, known.parts());
		}
		List<ModelPart> parts = model.allParts();
		if (parts.size() > EntityModel.MAX_PARTS) {
			models.put(model, null);
			return null;
		}
		Map<ModelPart, Integer> index = new IdentityHashMap<>();
		for (int i = 0; i < parts.size(); i++) {
			index.put(parts.get(i), i);
		}
		int[] parent = new int[parts.size()];
		java.util.Arrays.fill(parent, EntityModel.NO_PARENT);
		for (int i = 0; i < parts.size(); i++) {
			for (ModelPart child : ((ModelPartAccessor) (Object) parts.get(i)).mcskylines$children().values()) {
				Integer c = index.get(child);
				if (c != null) {
					parent[c] = i;
				}
			}
		}
		List<EntityModel.Part> wire = new ArrayList<>(parts.size());
		for (int i = 0; i < parts.size(); i++) {
			if (parent[i] != EntityModel.NO_PARENT && parent[i] >= i) {
				LOG.warn("[MinecraftSkylines] model {} lists a part before its parent; not drawn", model.getClass().getSimpleName());
				models.put(model, null);
				return null;
			}
			wire.add(new EntityModel.Part(parent[i], quads(parts.get(i))));
		}
		int id = nextModel++;
		if (!guest.send(AppProtocol.ENTITY_MODEL, new EntityModel(id, model.getClass().getSimpleName(), wire).encode())) {
			nextModel--;
			return null;
		}
		ModelInfo info = new ModelInfo(id, texture, parts);
		models.put(model, info);
		return info;
	}

	private static float[] quads(ModelPart part) {
		List<float[]> out = new ArrayList<>();
		for (ModelPart.Cube cube : ((ModelPartAccessor) (Object) part).mcskylines$cubes()) {
			for (ModelPart.Polygon poly : cube.polygons) {
				ModelPart.Vertex[] v = poly.vertices();
				if (v.length != 4) {
					continue;
				}
				float[] q = new float[EntityModel.FLOATS_PER_QUAD];
				for (int k = 0; k < 4; k++) {
					q[k * 5] = v[k].x();
					q[k * 5 + 1] = v[k].y();
					q[k * 5 + 2] = v[k].z();
					q[k * 5 + 3] = v[k].u();
					q[k * 5 + 4] = v[k].v();
				}
				Vector3fc n = poly.normal();
				q[20] = n.x();
				q[21] = n.y();
				q[22] = n.z();
				out.add(q);
			}
		}
		return flatten(out, EntityModel.MAX_QUADS);
	}

	private static float[] flatten(List<float[]> quads, int max) {
		int n = Math.min(quads.size(), max);
		float[] all = new float[n * EntityModel.FLOATS_PER_QUAD];
		for (int i = 0; i < n; i++) {
			System.arraycopy(quads.get(i), 0, all, i * EntityModel.FLOATS_PER_QUAD, EntityModel.FLOATS_PER_QUAD);
		}
		return all;
	}

	/** The texture bound to a render type, sent once from the resource pack's PNG; -1 when it has none we can read. */
	private int texture(Minecraft mc, RenderType renderType) {
		Identifier location = null;
		Map<String, ?> bindings = ((RenderSetupAccessor) (Object) ((RenderTypeAccessor) renderType).mcskylines$state()).mcskylines$textures();
		Object binding = bindings.containsKey("Sampler0") ? bindings.get("Sampler0") : bindings.values().stream().findFirst().orElse(null);
		if (binding != null) {
			location = ((TextureBindingAccessor) binding).mcskylines$location();
		}
		if (location == null) {
			return -1;
		}
		Integer known = textures.get(location);
		if (known != null) {
			return known;
		}
		int id = -1;
		Optional<Resource> resource = mc.getResourceManager().getResource(location);
		if (resource.isPresent()) {
			try (InputStream in = resource.get().open()) {
				byte[] png = in.readAllBytes();
				if (png.length >= 24 && png.length <= EntityTexture.MAX_LENGTH) {
					int w = be32(png, 16), h = be32(png, 20);
					if (guest.send(AppProtocol.ENTITY_TEXTURE, new EntityTexture(nextTexture, w, h, EntityTexture.PNG, png).encode())) {
						id = nextTexture++;
					} else {
						return -1;
					}
				}
			} catch (IOException ex) {
				LOG.warn("[MinecraftSkylines] entity texture {} unreadable: {}", location, ex.getMessage());
			}
		}
		textures.put(location, id);
		return id;
	}

	private static int be32(byte[] b, int o) {
		return ((b[o] & 0xFF) << 24) | ((b[o + 1] & 0xFF) << 16) | ((b[o + 2] & 0xFF) << 8) | (b[o + 3] & 0xFF);
	}

	/** An item's quads as a one-part model (block units × 16) with its sprites packed side by side into its own texture. */
	private ModelInfo itemModel(ItemQuads itemQuads) {
		if (itemModels.containsKey(itemQuads)) {
			return itemModels.get(itemQuads);
		}
		Map<TextureAtlasSprite, Integer> offsets = new LinkedHashMap<>();
		int width = 0, height = 1;
		for (BakedQuad quad : itemQuads.all()) {
			TextureAtlasSprite s = quad.materialInfo().sprite();
			if (!offsets.containsKey(s)) {
				offsets.put(s, width);
				width += s.contents().width();
				height = Math.max(height, s.contents().height());
			}
		}
		if (width == 0 || itemQuads.all().size() > EntityModel.MAX_QUADS) {
			itemModels.put(itemQuads, null);
			return null;
		}
		byte[] rgba = new byte[width * height * 4];
		for (Map.Entry<TextureAtlasSprite, Integer> en : offsets.entrySet()) {
			NativeImage img = ((SpriteContentsAccessor) en.getKey().contents()).mcskylines$originalImage();
			int w = Math.min(en.getKey().contents().width(), img.getWidth()), h = Math.min(en.getKey().contents().height(), img.getHeight());
			for (int y = 0; y < h; y++) {
				for (int x = 0; x < w; x++) {
					int argb = img.getPixel(x, y), o = (y * width + en.getValue() + x) * 4;
					rgba[o] = (byte) (argb >> 16);
					rgba[o + 1] = (byte) (argb >> 8);
					rgba[o + 2] = (byte) argb;
					rgba[o + 3] = (byte) (argb >>> 24);
				}
			}
		}
		List<float[]> quads = new ArrayList<>();
		for (BakedQuad quad : itemQuads.all()) {
			TextureAtlasSprite s = quad.materialInfo().sprite();
			float du = s.getU1() - s.getU0(), dv = s.getV1() - s.getV0();
			float[] q = new float[EntityModel.FLOATS_PER_QUAD];
			for (int k = 0; k < 4; k++) {
				Vector3fc pos = quad.position(k);
				long uv = quad.packedUV(k);
				float lu = du == 0 ? 0 : (UVPair.unpackU(uv) - s.getU0()) / du, lv = dv == 0 ? 0 : (UVPair.unpackV(uv) - s.getV0()) / dv;
				q[k * 5] = pos.x() * 16;
				q[k * 5 + 1] = pos.y() * 16;
				q[k * 5 + 2] = pos.z() * 16;
				q[k * 5 + 3] = (offsets.get(s) + lu * s.contents().width()) / width;
				q[k * 5 + 4] = lv * s.contents().height() / height;
			}
			q[20] = quad.direction().getStepX();
			q[21] = quad.direction().getStepY();
			q[22] = quad.direction().getStepZ();
			quads.add(q);
		}
		int tex = nextTexture, id = nextModel;
		byte[] png = AtlasPng.encode(width, height, rgba);
		if (!guest.send(AppProtocol.ENTITY_TEXTURE, new EntityTexture(tex, width, height, EntityTexture.PNG, png).encode())
				|| !guest.send(AppProtocol.ENTITY_MODEL, new EntityModel(id, "item", List.of(new EntityModel.Part(EntityModel.NO_PARENT, flatten(quads, EntityModel.MAX_QUADS)))).encode())) {
			return null;
		}
		nextTexture++;
		nextModel++;
		ModelInfo info = new ModelInfo(id, tex, List.of());
		itemModels.put(itemQuads, info);
		return info;
	}
}
