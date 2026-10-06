package dev.mcskylines.entity;

import com.mojang.blaze3d.vertex.PoseStack;
import java.util.ArrayList;
import java.util.List;
import net.minecraft.client.gui.Font;
import net.minecraft.client.model.Model;
import net.minecraft.client.renderer.OrderedSubmitNodeCollector;
import net.minecraft.client.renderer.SubmitNodeCollector;
import net.minecraft.client.renderer.block.MovingBlockRenderState;
import net.minecraft.client.renderer.block.dispatch.BlockStateModelPart;
import net.minecraft.client.renderer.entity.state.EntityRenderState;
import net.minecraft.client.renderer.feature.ModelFeatureRenderer;
import net.minecraft.client.renderer.gizmos.DrawableGizmoPrimitives;
import net.minecraft.client.renderer.item.ItemStackRenderState;
import net.minecraft.client.renderer.rendertype.RenderType;
import net.minecraft.client.renderer.state.level.CameraRenderState;
import net.minecraft.client.renderer.state.level.QuadParticleRenderState;
import net.minecraft.client.renderer.texture.UvMapping;
import net.minecraft.client.resources.model.geometry.ItemQuads;
import net.minecraft.network.chat.Component;
import net.minecraft.util.FormattedCharSequence;
import net.minecraft.world.item.ItemDisplayContext;
import net.minecraft.world.phys.Vec3;
import net.minecraft.world.phys.shapes.VoxelShape;
import org.joml.Matrix4f;
import org.joml.Quaternionf;

/**
 * A SubmitNodeCollector that records what an entity renderer submits instead of drawing it: every model with its
 * state already applied by setupAnim (Minecraft's pose), its texture's render type, tint, overlay and pose matrix,
 * and every item's quads. Everything else (shadows, name tags, flames, leashes, custom geometry) is not captured.
 */
final class EntityCapture implements SubmitNodeCollector {
	/** One submitted model: parts already posed for this state. */
	record ModelSubmit(Model<?> model, RenderType renderType, int tint, int overlay, Matrix4f pose) {
	}

	/** One submitted item: quads in block units under {@code pose}. */
	record ItemSubmit(ItemQuads quads, Matrix4f pose) {
	}

	final List<ModelSubmit> models = new ArrayList<>();
	final List<ItemSubmit> items = new ArrayList<>();

	void reset() {
		models.clear();
		items.clear();
	}

	@Override
	public OrderedSubmitNodeCollector order(int order) {
		return this;
	}

	@Override
	public <S> void submitModel(Model<? super S> model, S state, PoseStack pose, RenderType renderType, int light, int overlay, int tint,
			UvMapping uvMapping, int outline) {
		model.setupAnim(state);
		models.add(new ModelSubmit(model, renderType, tint, overlay, new Matrix4f(pose.last().pose())));
	}

	@Override
	public void submitItem(PoseStack pose, ItemDisplayContext context, int light, int overlay, int outline, int[] tints, ItemQuads quads,
			ItemStackRenderState.FoilType foil) {
		items.add(new ItemSubmit(quads, new Matrix4f(pose.last().pose())));
	}

	@Override
	public void submitShadow(PoseStack pose, float radius, List<EntityRenderState.ShadowPiece> pieces) {
	}

	@Override
	public void submitNameTag(PoseStack pose, Vec3 at, int y, Component text, boolean seeThrough, int light, CameraRenderState camera) {
	}

	@Override
	public void submitText(PoseStack pose, float x, float y, FormattedCharSequence text, boolean shadow, Font.DisplayMode mode, int light,
			int color, int background, int outline) {
	}

	@Override
	public void submitTextBackground(PoseStack pose, float x0, float y0, float x1, float y1, int color, Font.DisplayMode mode, int light) {
	}

	@Override
	public void submitFlame(PoseStack pose, EntityRenderState state, Quaternionf rotation) {
	}

	@Override
	public void submitLeash(PoseStack pose, EntityRenderState.LeashState leash) {
	}

	@Override
	public <S> void submitCrumblingOverlay(Model<? super S> model, S state, PoseStack pose, RenderType renderType, int light, int overlay,
			int tint, ModelFeatureRenderer.CrumblingOverlay crumbling) {
	}

	@Override
	public void submitMovingBlock(PoseStack pose, MovingBlockRenderState state, int light) {
	}

	@Override
	public void submitBlockModel(PoseStack pose, RenderType renderType, List<BlockStateModelPart> parts, int[] tints, int light, int overlay,
			int outline) {
	}

	@Override
	public void submitBreakingBlockModel(PoseStack pose, List<BlockStateModelPart> parts, int progress, boolean flag) {
	}

	@Override
	public void submitShapeOutline(PoseStack pose, VoxelShape shape, RenderType renderType, int color, float width, boolean flag) {
	}

	@Override
	public void submitCustomGeometry(PoseStack pose, RenderType renderType, SubmitNodeCollector.CustomGeometryRenderer renderer) {
	}

	@Override
	public void submitQuadParticleGroup(QuadParticleRenderState state) {
	}

	@Override
	public void submitGizmoPrimitives(DrawableGizmoPrimitives.Group group, CameraRenderState camera, boolean flag) {
	}
}
