package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;
import java.util.ArrayList;
import java.util.List;

/** 0x01E2 ENTITY_STATES (guest to host), minor 14: the complete set of entities the guest draws, with their pose. */
public record EntityStates(int seq, List<Entity> entities) {
	public static final int MAX_ENTITIES = 2048;
	public static final int MAX_DRAWS = 16;
	public static final int MAX_PARTS = 1024;
	public static final int FLAG_HIDDEN = 1;
	public static final int FLAG_SKIP = 2;
	public static final int POSE_FLOATS = 9;

	/** Position in the Minecraft frame; the angles are degrees and informational. */
	public record Entity(int entityId, float x, float y, float z, float bodyYaw, float headYaw, float pitch, List<Draw> draws) {
	}

	/**
	 * One model drawn: matrix is the row-major 3 x 4 model-to-world transform, color RGBA8 (R lowest byte of the u32),
	 * poses 9 floats per part (px, py, pz, xRot, yRot, zRot, xScale, yScale, zScale), flags one byte per part.
	 */
	public record Draw(int modelId, int textureId, int color, float[] matrix, float[] poses, byte[] flags) {
	}

	public byte[] encode() {
		if (entities.size() > MAX_ENTITIES) {
			throw new IllegalArgumentException(entities.size() + " entities, above " + MAX_ENTITIES);
		}
		PayloadWriter w = new PayloadWriter().u32(Integer.toUnsignedLong(seq)).u16(entities.size());
		for (Entity e : entities) {
			if (e.draws().size() > MAX_DRAWS) {
				throw new IllegalArgumentException(e.draws().size() + " draws, above " + MAX_DRAWS);
			}
			w.u32(Integer.toUnsignedLong(e.entityId())).f32(e.x()).f32(e.y()).f32(e.z()).f32(e.bodyYaw()).f32(e.headYaw()).f32(e.pitch())
					.u8(e.draws().size());
			for (Draw d : e.draws()) {
				if (d.matrix().length != 12) {
					throw new IllegalArgumentException("matrix has " + d.matrix().length + " floats, expected 12");
				}
				int parts = d.flags().length;
				if (parts > MAX_PARTS || d.poses().length != POSE_FLOATS * parts) {
					throw new IllegalArgumentException("draw has " + parts + " flags and " + d.poses().length + " pose floats");
				}
				w.u32(Integer.toUnsignedLong(d.modelId())).u32(Integer.toUnsignedLong(d.textureId())).u32(Integer.toUnsignedLong(d.color()));
				for (float f : d.matrix()) {
					w.f32(f);
				}
				w.u16(parts);
				for (int i = 0; i < parts; i++) {
					for (int k = 0; k < POSE_FLOATS; k++) {
						w.f32(d.poses()[i * POSE_FLOATS + k]);
					}
					w.u8(d.flags()[i] & 0xFF);
				}
			}
		}
		return w.toByteArray();
	}

	public static EntityStates decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int seq = (int) r.u32();
		int n = r.u16();
		if (n > MAX_ENTITIES) {
			throw new ProtocolException("entity count " + n + " above " + MAX_ENTITIES);
		}
		List<Entity> entities = new ArrayList<>(n);
		for (int i = 0; i < n; i++) {
			int id = (int) r.u32();
			float x = r.f32(), y = r.f32(), z = r.f32(), bodyYaw = r.f32(), headYaw = r.f32(), pitch = r.f32();
			int dc = r.u8();
			if (dc > MAX_DRAWS) {
				throw new ProtocolException("draw count " + dc + " above " + MAX_DRAWS);
			}
			List<Draw> draws = new ArrayList<>(dc);
			for (int j = 0; j < dc; j++) {
				int modelId = (int) r.u32(), textureId = (int) r.u32(), color = (int) r.u32();
				float[] matrix = new float[12];
				for (int k = 0; k < 12; k++) {
					matrix[k] = r.f32();
				}
				int parts = r.u16();
				if (parts > MAX_PARTS) {
					throw new ProtocolException("part count " + parts + " above " + MAX_PARTS);
				}
				float[] poses = new float[POSE_FLOATS * parts];
				byte[] flags = new byte[parts];
				for (int p = 0; p < parts; p++) {
					for (int k = 0; k < POSE_FLOATS; k++) {
						poses[p * POSE_FLOATS + k] = r.f32();
					}
					flags[p] = (byte) r.u8();
				}
				draws.add(new Draw(modelId, textureId, color, matrix, poses, flags));
			}
			entities.add(new Entity(id, x, y, z, bodyYaw, headYaw, pitch, draws));
		}
		return new EntityStates(seq, entities);
	}
}
