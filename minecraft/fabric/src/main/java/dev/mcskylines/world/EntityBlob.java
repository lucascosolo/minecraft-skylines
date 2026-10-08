package dev.mcskylines.world;

import dev.mcskylines.protocol.CityEntities;
import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.util.ArrayList;
import java.util.List;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.nbt.NbtAccounter;
import net.minecraft.nbt.NbtIo;
import net.minecraft.nbt.Tag;

/** Protocol 1.18 CITY_ENTITIES on the Fabric guest: gzip NBT {version: 1, DataVersion, entities: [...]}. */
public final class EntityBlob {
	public static final int VERSION = 1;
	public static final int MAX_ENTITIES = 1024;

	public record Decoded(int dataVersion, List<CompoundTag> entities) {
	}

	private EntityBlob() {
	}

	/** Persistent mobs first, then other mobs, then everything else: what survives a trim. */
	public static int priority(boolean mob, boolean persistent) {
		return mob ? (persistent ? 0 : 1) : 2;
	}

	public static byte[] encode(List<CompoundTag> entities, int dataVersion) {
		return encode(entities, dataVersion, CityEntities.MAX_LENGTH);
	}

	/** The first MAX_ENTITIES entities, then fewer from the end until the blob fits {@code maxBytes}. */
	public static byte[] encode(List<CompoundTag> entities, int dataVersion, int maxBytes) {
		int n = Math.min(entities.size(), MAX_ENTITIES);
		while (true) {
			byte[] out = write(entities.subList(0, n), dataVersion);
			if (out.length <= maxBytes || n == 0) {
				return out;
			}
			n = n * 3 / 4;
		}
	}

	public static Decoded decode(byte[] data) throws IOException {
		if (data.length == 0) {
			return new Decoded(0, List.of());
		}
		CompoundTag root = NbtIo.readCompressed(new ByteArrayInputStream(data), NbtAccounter.unlimitedHeap());
		int version = root.getIntOr("version", -1);
		if (version != VERSION) {
			throw new IOException("city entities version " + version + ", expected " + VERSION);
		}
		if (!(root.get("entities") instanceof ListTag entities)) {
			throw new IOException("city entities without an entities list");
		}
		List<CompoundTag> out = new ArrayList<>(entities.size());
		for (Tag t : entities) {
			if (!(t instanceof CompoundTag c)) {
				throw new IOException("city entities list holds a non-compound");
			}
			out.add(c);
		}
		return new Decoded(root.getIntOr("DataVersion", 0), out);
	}

	private static byte[] write(List<CompoundTag> entities, int dataVersion) {
		CompoundTag root = new CompoundTag();
		root.putInt("version", VERSION);
		root.putInt("DataVersion", dataVersion);
		ListTag list = new ListTag();
		list.addAll(entities);
		root.put("entities", list);
		ByteArrayOutputStream bytes = new ByteArrayOutputStream();
		try {
			NbtIo.writeCompressed(root, bytes);
		} catch (IOException e) {
			throw new UncheckedIOException(e);
		}
		return bytes.toByteArray();
	}
}
