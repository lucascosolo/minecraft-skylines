package dev.mcskylines.world;

import it.unimi.dsi.fastutil.longs.LongCollection;
import it.unimi.dsi.fastutil.longs.LongOpenHashSet;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.NoSuchFileException;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.Arrays;
import java.util.zip.CRC32;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/** {@code <world>/mcskylines/touched.bin}: "MSKT", u8 version 1, u32 count, (i32 x, y, z) sorted by key, u32 CRC-32; little-endian. */
public final class TouchedFile {
	private static final Logger LOG = LoggerFactory.getLogger("mcskylines");
	private static final byte[] MAGIC = "MSKT".getBytes(StandardCharsets.US_ASCII);
	private static final int VERSION = 1;
	private static final int HEADER = 9;

	private TouchedFile() {
	}

	public static byte[] encode(LongCollection keys) {
		long[] sorted = keys.toLongArray();
		Arrays.sort(sorted);
		ByteBuffer b = ByteBuffer.allocate(HEADER + 12 * sorted.length + 4).order(ByteOrder.LITTLE_ENDIAN);
		b.put(MAGIC).put((byte) VERSION).putInt(sorted.length);
		for (long k : sorted) {
			b.putInt(BlockKey.x(k)).putInt(BlockKey.y(k)).putInt(BlockKey.z(k));
		}
		b.putInt((int) crc(b.array(), b.position()));
		return b.array();
	}

	public static LongOpenHashSet decode(byte[] bytes) throws IOException {
		if (bytes.length < HEADER + 4 || !Arrays.equals(bytes, 0, 4, MAGIC, 0, 4)) {
			throw new IOException("not a touched-set file");
		}
		ByteBuffer b = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).position(4);
		int version = b.get() & 0xFF;
		if (version != VERSION) {
			throw new IOException("unknown touched-set version " + version);
		}
		long count = Integer.toUnsignedLong(b.getInt());
		if (bytes.length != HEADER + 12 * count + 4) {
			throw new IOException("touched-set length " + bytes.length + " does not match count " + count);
		}
		if ((int) crc(bytes, bytes.length - 4) != b.getInt(bytes.length - 4)) {
			throw new IOException("touched-set checksum mismatch");
		}
		LongOpenHashSet keys = new LongOpenHashSet((int) count);
		for (long i = 0; i < count; i++) {
			int x = b.getInt();
			int y = b.getInt();
			int z = b.getInt();
			if (!BlockKey.fits(x, y, z)) {
				throw new IOException("touched-set position out of range: " + x + ", " + y + ", " + z);
			}
			keys.add(BlockKey.pack(x, y, z));
		}
		return keys;
	}

	/** Writes a sibling temp file, then atomically replaces {@code file} with it. */
	public static void write(Path file, LongCollection keys) throws IOException {
		Files.createDirectories(file.toAbsolutePath().getParent());
		Path tmp = file.resolveSibling(file.getFileName() + ".tmp");
		Files.write(tmp, encode(keys));
		Files.move(tmp, file, StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING);
	}

	/** Missing: empty. Unreadable: logged loudly, moved aside for inspection, empty. */
	public static LongOpenHashSet read(Path file) {
		byte[] bytes;
		try {
			bytes = Files.readAllBytes(file);
		} catch (NoSuchFileException e) {
			return new LongOpenHashSet();
		} catch (IOException e) {
			return unreadable(file, e);
		}
		try {
			return decode(bytes);
		} catch (IOException e) {
			return unreadable(file, e);
		}
	}

	private static LongOpenHashSet unreadable(Path file, IOException cause) {
		Path aside = file.resolveSibling(file.getFileName() + ".unreadable-" + System.currentTimeMillis());
		LOG.error("[MinecraftSkylines] touched-set file {} is unreadable ({}); treating it as empty, so blocks from "
			+ "earlier cities may remain in unvisited places. Moving it to {}", file, cause.getMessage(), aside.getFileName());
		try {
			Files.move(file, aside);
		} catch (IOException e) {
			LOG.error("[MinecraftSkylines] could not move {} aside: {}", file, e.toString());
		}
		return new LongOpenHashSet();
	}

	private static long crc(byte[] bytes, int length) {
		CRC32 c = new CRC32();
		c.update(bytes, 0, length);
		return c.getValue();
	}
}
