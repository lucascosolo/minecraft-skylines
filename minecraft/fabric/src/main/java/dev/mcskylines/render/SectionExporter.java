// Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
// See minecraft/THIRD-PARTY-NOTICES.md for the full licence text.
package dev.mcskylines.render;

import com.mojang.blaze3d.vertex.QuadInstance;
import com.mojang.blaze3d.vertex.VertexConsumer;
import dev.mcskylines.bridge.BridgeGuest;
import dev.mcskylines.bridge.FrameCodec;
import dev.mcskylines.protocol.AppProtocol;
import dev.mcskylines.protocol.BlockAtlas;
import dev.mcskylines.protocol.SectionMesh;
import dev.mcskylines.protocol.SectionsClear;
import it.unimi.dsi.fastutil.longs.LongLinkedOpenHashSet;
import it.unimi.dsi.fastutil.longs.LongOpenHashSet;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import net.minecraft.client.Minecraft;
import net.minecraft.client.model.geom.builders.UVPair;
import net.minecraft.client.multiplayer.ClientLevel;
import net.minecraft.client.renderer.block.BlockQuadOutput;
import net.minecraft.client.renderer.block.FluidRenderer;
import net.minecraft.client.renderer.block.ModelBlockRenderer;
import net.minecraft.client.renderer.chunk.ChunkSectionLayer;
import net.minecraft.client.renderer.texture.TextureAtlas;
import net.minecraft.client.renderer.texture.TextureAtlasSprite;
import net.minecraft.client.resources.model.geometry.BakedQuad;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.SectionPos;
import net.minecraft.world.level.CardinalLighting;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.RenderShape;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.chunk.LevelChunk;
import net.minecraft.world.level.chunk.LevelChunkSection;
import net.minecraft.world.level.chunk.status.ChunkStatus;
import net.minecraft.world.level.material.FluidState;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * Streams the meshes of placed blocks (Minecraft's own block and fluid renderer: models, biome tint, ambient
 * occlusion) and the block atlas to the host. Port of SkyCraft's WorldExporter, block sections only. Client thread
 * only, except {@link #markDirty}.
 */
public final class SectionExporter {
	private static final Logger LOG = LoggerFactory.getLogger("mcskylines");
	private static final int SECTIONS_PER_FRAME = 12;
	private static final long FRAME_BUDGET_NS = 3_000_000L;
	/** Stop meshing while this much is still waiting for the socket (the bridge ends the session at 64 MiB). */
	private static final long MAX_QUEUED_BYTES = 8L << 20;
	private static final int MAX_ATLAS_PNG_BYTES = FrameCodec.MAX_PAYLOAD - 13;
	private static final ExecutorService PNG_WORKER = Executors.newSingleThreadExecutor(r -> {
		Thread t = new Thread(r, "mcskylines-atlas-png");
		t.setDaemon(true);
		return t;
	});

	private static final LongLinkedOpenHashSet DIRTY = new LongLinkedOpenHashSet();

	private final BridgeGuest guest;
	private final LongOpenHashSet sent = new LongOpenHashSet(); // sections the host holds a mesh for
	private final MeshBuilder mesh = new MeshBuilder();
	private boolean resetPending = true;
	private ClientLevel sentLevel;
	private BlockAtlasImage atlas;
	private CompletableFuture<AtlasPng.Fitted> atlasPng;
	private boolean atlasSent;
	private boolean animate;
	private ModelBlockRenderer blockRenderer;
	private FluidRenderer fluidRenderer;
	private int meshesSent;

	public SectionExporter(BridgeGuest guest) {
		this.guest = guest;
	}

	/** From LevelExtractor.setSectionDirty (any thread Minecraft calls it on). */
	public static void markDirty(int sx, int sy, int sz, boolean playerChanged) {
		long key = SectionPos.asLong(sx, sy, sz);
		synchronized (DIRTY) {
			if (playerChanged) {
				DIRTY.addAndMoveToFirst(key);
			} else {
				DIRTY.add(key);
			}
		}
	}

	/** A new session: the host holds nothing, so clear, atlas and every section go again. */
	public void linkUp() {
		resetPending = true;
	}

	/** Once per rendered frame while the host speaks app minor 2 or later. */
	public void frame(Minecraft mc) {
		ClientLevel level = mc.level;
		if (level == null || mc.player == null) {
			sentLevel = null;
			return;
		}
		if (resetPending || level != sentLevel || atlas == null || atlas.stale(mc)) {
			resendEverything(mc, level);
		}
		if (!atlasSent) {
			if (!atlasPng.isDone()) {
				return;
			}
			sendAtlas(atlasPng.join());
		}
		if (guest.queuedBytes() > MAX_QUEUED_BYTES) {
			return;
		}
		meshDirtySections(mc, level);
		if (animate) {
			atlas.animate(level.getGameTime(), region -> guest.send(AppProtocol.ATLAS_REGION, region.encode()));
		}
	}

	private void resendEverything(Minecraft mc, ClientLevel level) {
		resetPending = false;
		sentLevel = level;
		guest.send(AppProtocol.SECTIONS_CLEAR, new SectionsClear().encode());
		sent.clear();
		if (atlas == null || atlas.stale(mc)) {
			atlas = BlockAtlasImage.build(mc);
			BlockAtlasImage a = atlas;
			atlasPng = CompletableFuture.supplyAsync(() -> AtlasPng.fit(a.width, a.height, a.rgba, MAX_ATLAS_PNG_BYTES), PNG_WORKER);
		}
		atlasSent = false;
		blockRenderer = new ModelBlockRenderer(mc.options.ambientOcclusion().get(), true, mc.getBlockColors());
		fluidRenderer = new FluidRenderer(mc.getModelManager().getFluidStateModelSet());
		// Everything already loaded is meshed again; later chunk loads mark themselves dirty.
		int radius = mc.options.getEffectiveRenderDistance() + 1;
		int pcx = SectionPos.blockToSectionCoord(mc.player.getBlockX()), pcz = SectionPos.blockToSectionCoord(mc.player.getBlockZ());
		for (int cx = pcx - radius; cx <= pcx + radius; cx++) {
			for (int cz = pcz - radius; cz <= pcz + radius; cz++) {
				LevelChunk chunk = level.getChunkSource().getChunk(cx, cz, ChunkStatus.FULL, false);
				if (chunk == null) {
					continue;
				}
				LevelChunkSection[] sections = chunk.getSections();
				for (int i = 0; i < sections.length; i++) {
					if (!sections[i].hasOnlyAir()) {
						markDirty(cx, chunk.getSectionYFromSectionIndex(i), cz, false);
					}
				}
			}
		}
	}

	private void sendAtlas(AtlasPng.Fitted png) {
		boolean ok = guest.send(AppProtocol.BLOCK_ATLAS, new BlockAtlas(png.width(), png.height(), BlockAtlas.PNG, png.png()).encode());
		atlasSent = true;
		animate = png.halvings() == 0;
		LOG.info("[MinecraftSkylines] block atlas {}x{} ({} animated sprites), PNG {} bytes as {}x{}: {}", atlas.width, atlas.height,
			atlas.animatedSprites(), png.png().length, png.width(), png.height(), ok ? "sent" : "NOT sent (no session)");
		if (png.halvings() > 0) {
			LOG.error("[MinecraftSkylines] block atlas PNG exceeded {} bytes; downscaled by 2 x{} (animated sprites disabled)",
				MAX_ATLAS_PNG_BYTES, png.halvings());
		}
	}

	private void meshDirtySections(Minecraft mc, ClientLevel level) {
		// Chunk loads and light updates dirty many all-air sections; those cost a lookup. Real meshing is limited per frame.
		long deadline = System.nanoTime() + FRAME_BUDGET_NS;
		int radius = mc.options.getEffectiveRenderDistance() + 1;
		int pcx = SectionPos.blockToSectionCoord(mc.player.getBlockX()), pcz = SectionPos.blockToSectionCoord(mc.player.getBlockZ());
		int meshed = 0;
		while (meshed < SECTIONS_PER_FRAME && System.nanoTime() < deadline) {
			long key;
			synchronized (DIRTY) {
				if (DIRTY.isEmpty()) {
					return;
				}
				key = DIRTY.removeFirstLong();
			}
			if (Math.abs(SectionPos.x(key) - pcx) <= radius && Math.abs(SectionPos.z(key) - pcz) <= radius && meshSection(mc, level, key)) {
				meshed++;
			}
		}
	}

	/** Returns true if real meshing work was done. */
	private boolean meshSection(Minecraft mc, ClientLevel level, long key) {
		int sx = SectionPos.x(key), sy = SectionPos.y(key), sz = SectionPos.z(key);
		LevelChunk chunk = level.getChunkSource().getChunk(sx, sz, ChunkStatus.FULL, false);
		if (chunk == null) {
			return false; // unloaded: the host keeps what it has
		}
		int index = level.getSectionIndexFromSectionY(sy);
		LevelChunkSection section = index >= 0 && index < chunk.getSections().length ? chunk.getSections()[index] : null;
		boolean empty = section == null || section.hasOnlyAir();
		if (empty && !sent.contains(key)) {
			return false;
		}
		mesh.reset(level.cardinalLighting());
		if (!empty) {
			BlockPos origin = SectionPos.of(sx, sy, sz).origin();
			BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
			for (int y = 0; y < 16; y++) {
				for (int z = 0; z < 16; z++) {
					for (int x = 0; x < 16; x++) {
						pos.set(origin.getX() + x, origin.getY() + y, origin.getZ() + z);
						BlockState state = chunk.getBlockState(pos);
						// CS1 draws its own city: shadow blocks (docs/plans/survival.md) are never sent.
						if (state.isAir()) {
							continue;
						}
						// The host clips CS1's terrain over dug columns; the walls, floors and ceilings of the cavity are the
						// shadow blocks' faces toward dug (cave air) cells (docs/plans/survival.md).
						boolean shadow = dev.mcskylines.shadow.ShadowCells.INSTANCE.contains(pos.getX(), pos.getY(), pos.getZ());
						if (shadow) {
							// Only ground (full blocks) bounds a cavity; a grass plant next to cave air stays hidden.
							int faces = state.isSolidRender() ? caveFaces(level, pos) : 0;
							if (faces == 0) {
								continue;
							}
							mesh.faceMask = faces;
						}
						FluidState fluid = state.getFluidState();
						if (!shadow && !fluid.isEmpty()) {
							fluidRenderer.tesselate(level, pos, mesh, state, fluid);
						}
						if (state.getRenderShape() == RenderShape.MODEL) {
							var model = mc.getModelManager().getBlockStateModelSet().get(state);
							blockRenderer.tesselateBlock(mesh, x, y, z, level, pos.immutable(), state, model, state.getSeed(pos));
						}
						mesh.faceMask = -1;
					}
				}
			}
		}
		if (mesh.out.vertexCount() == 0 && !sent.contains(key)) {
			return true;
		}
		if (guest.send(AppProtocol.SECTION_MESH, new SectionMesh(sx, sy, sz, mesh.out.toBytes()).encode())) {
			if (mesh.out.vertexCount() > 0) {
				sent.add(key);
			} else {
				sent.remove(key);
			}
			if (++meshesSent <= 10 || meshesSent % 200 == 0) {
				LOG.info("[MinecraftSkylines] section mesh {} {} {}: {} vertices ({} sections at the host)", sx, sy, sz,
					mesh.out.vertexCount(), sent.size());
			}
		}
		return true;
	}

	/** Bit per Direction ordinal for each neighbour that is cave air. */
	private static int caveFaces(ClientLevel level, BlockPos pos) {
		int mask = 0;
		for (Direction d : Direction.values()) {
			if (level.getBlockState(pos.relative(d)).is(Blocks.CAVE_AIR)) {
				mask |= 1 << d.ordinal();
			}
		}
		return mask;
	}

	private static int flags(ChunkSectionLayer layer) {
		return layer == ChunkSectionLayer.TRANSLUCENT ? SectionMesh.TRANSLUCENT : layer == ChunkSectionLayer.CUTOUT ? SectionMesh.CUTOUT : 0;
	}

	/** Collects Minecraft's block quads and fluid vertices as triangles in SECTION_MESH layout. */
	private static final class MeshBuilder implements BlockQuadOutput, FluidRenderer.Output, VertexConsumer {
		private static final int[] QUAD = {0, 1, 2, 0, 2, 3};
		final MeshVertices out = new MeshVertices();
		/** The level's fixed per-face brightness, which the host's lighting replaces. */
		private CardinalLighting cardinal = CardinalLighting.DEFAULT;
		private final float[] fq = new float[4 * 7];
		private int fqCount;
		private int fluidFlags;
		/** Direction ordinals whose quads are kept; -1 keeps all. */
		int faceMask = -1;

		void reset(CardinalLighting cardinal) {
			this.cardinal = cardinal;
			out.reset();
			fqCount = 0;
			faceMask = -1;
		}

		@Override
		public void put(float x, float y, float z, BakedQuad quad, QuadInstance instance) {
			if (faceMask != -1 && (faceMask & 1 << quad.direction().ordinal()) == 0) {
				return;
			}
			TextureAtlasSprite sprite = quad.materialInfo().sprite();
			if (!sprite.atlasLocation().equals(TextureAtlas.LOCATION_BLOCKS)) {
				return; // UVs would not index the block atlas
			}
			int emission = quad.materialInfo().lightEmission();
			Direction override = quad.materialInfo().shadeDirectionOverride();
			float shade = cardinal.byFace(override != null ? override : quad.direction());
			int flags = flags(quad.materialInfo().layer());
			for (int k : QUAD) {
				var p = quad.position(k);
				long uv = quad.packedUV(k);
				out.put(p.x() + x, p.y() + y, p.z() + z, UVPair.unpackU(uv), UVPair.unpackV(uv),
					MeshVertices.unshade(instance.getColor(k), shade), instance.getLightCoordsWithEmission(k, emission), flags);
			}
		}

		@Override
		public VertexConsumer getBuilder(ChunkSectionLayer layer) {
			fluidFlags = flags(layer);
			return this;
		}

		@Override
		public void addVertex(float x, float y, float z, int color, float u, float v, int overlay, int light, float nx, float ny, float nz) {
			int o = fqCount * 7;
			fq[o] = x;
			fq[o + 1] = y;
			fq[o + 2] = z;
			fq[o + 3] = u;
			fq[o + 4] = v;
			fq[o + 5] = Float.intBitsToFloat(color);
			fq[o + 6] = Float.intBitsToFloat(light);
			if (++fqCount < 4) {
				return;
			}
			fqCount = 0;
			// Fluid faces are shaded like block faces (up, down, or the side's brightness).
			Direction normal = nx == 0 && ny == 0 && nz == 0 ? null : Direction.getApproximateNearest(nx, ny, nz);
			float shade = normal == null ? 1.0F
				: normal.getAxis() == Direction.Axis.Y ? cardinal.byFace(normal) : cardinal.up() * cardinal.byFace(normal);
			for (int k : QUAD) {
				int b = k * 7;
				out.put(fq[b], fq[b + 1], fq[b + 2], fq[b + 3], fq[b + 4], MeshVertices.unshade(Float.floatToRawIntBits(fq[b + 5]), shade),
					Float.floatToRawIntBits(fq[b + 6]), fluidFlags);
			}
		}

		@Override
		public VertexConsumer addVertex(float x, float y, float z) {
			throw new UnsupportedOperationException();
		}

		@Override
		public VertexConsumer setColor(int r, int g, int b, int a) {
			return this;
		}

		@Override
		public VertexConsumer setColor(int color) {
			return this;
		}

		@Override
		public VertexConsumer setUv(float u, float v) {
			return this;
		}

		@Override
		public VertexConsumer setUv1(int u, int v) {
			return this;
		}

		@Override
		public VertexConsumer setUv2(int u, int v) {
			return this;
		}

		@Override
		public VertexConsumer setUv3(float u, float v) {
			return this;
		}

		@Override
		public VertexConsumer setNormal(float x, float y, float z) {
			return this;
		}

		@Override
		public VertexConsumer setLineWidth(float width) {
			return this;
		}
	}
}
