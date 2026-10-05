package dev.mcskylines.bridge.overlay;

import java.io.IOException;
import java.io.RandomAccessFile;
import java.lang.invoke.MethodHandles;
import java.lang.invoke.VarHandle;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.MappedByteBuffer;
import java.nio.channels.FileChannel;
import java.nio.file.Path;

/**
 * Guest side of the shared-memory overlay (protocol/minecraft-skylines-v1.md, "Shared-memory layout"): owns the
 * back slot, publishes it with an atomic exchange of the {@code state} word. Never deletes or shrinks the file.
 */
public final class OverlayWriter implements AutoCloseable {
	public static final int MAGIC = 0x564F534D;
	public static final int LAYOUT_VERSION = 1;
	public static final int SLOT_COUNT = 3;
	public static final int DIRTY = 4;
	public static final int HEADER_BYTES = 0x100;
	public static final int SLOT_HEADER_BYTES = 0x40;
	static final int STATE = 0x14;
	private static final VarHandle INT = MethodHandles.byteBufferViewVarHandle(int[].class, ByteOrder.LITTLE_ENDIAN);

	private final Path path;
	private final int maxWidth;
	private final int maxHeight;
	private final long generation;
	private final MappedByteBuffer map;
	private int back;

	private OverlayWriter(Path path, int maxWidth, int maxHeight, long generation, MappedByteBuffer map) {
		this.path = path;
		this.maxWidth = maxWidth;
		this.maxHeight = maxHeight;
		this.generation = generation;
		this.map = map;
	}

	public static long fileSize(int maxWidth, int maxHeight) {
		return HEADER_BYTES + (long) SLOT_COUNT * maxWidth * maxHeight * 4;
	}

	public static OverlayWriter open(Path path, int maxWidth, int maxHeight, long generation) throws IOException {
		if (maxWidth < 1 || maxHeight < 1 || fileSize(maxWidth, maxHeight) > Integer.MAX_VALUE) {
			throw new IllegalArgumentException("bad overlay size " + maxWidth + "x" + maxHeight);
		}
		long size = fileSize(maxWidth, maxHeight);
		try (RandomAccessFile raf = new RandomAccessFile(path.toFile(), "rw")) {
			if (raf.length() < size) {
				raf.setLength(size);
			}
			MappedByteBuffer map = raf.getChannel().map(FileChannel.MapMode.READ_WRITE, 0, size);
			map.order(ByteOrder.LITTLE_ENDIAN);
			OverlayWriter w = new OverlayWriter(path, maxWidth, maxHeight, generation, map);
			w.initHeader();
			return w;
		}
	}

	private void initHeader() {
		for (int i = 0; i < HEADER_BYTES; i += 8) {
			map.putLong(i, 0L);
		}
		map.putInt(0x00, MAGIC).putInt(0x04, LAYOUT_VERSION).putInt(0x08, maxWidth).putInt(0x0C, maxHeight)
			.putInt(0x10, SLOT_COUNT).putLong(0x20, generation);
		INT.setVolatile(map, STATE, 1);
		back = 0;
	}

	public Path path() {
		return path;
	}

	public int maxWidth() {
		return maxWidth;
	}

	public int maxHeight() {
		return maxHeight;
	}

	public long generation() {
		return generation;
	}

	public int backSlot() {
		return back;
	}

	/** The back slot's pixel area: write {@code width*height*4} RGBA bytes from index 0, then {@link #publish}. */
	public ByteBuffer backPixels() {
		int bytes = maxWidth * maxHeight * 4;
		return map.slice(HEADER_BYTES + back * bytes, bytes).order(ByteOrder.LITTLE_ENDIAN);
	}

	/** Makes the back slot the newest frame; {@code frameId} also becomes {@code framesPublished}. */
	public void publish(int width, int height, int flags, long frameId) {
		if (width < 1 || height < 1 || width > maxWidth || height > maxHeight) {
			throw new IllegalArgumentException("frame " + width + "x" + height + " exceeds " + maxWidth + "x" + maxHeight);
		}
		int h = SLOT_HEADER_BYTES + back * SLOT_HEADER_BYTES;
		map.putInt(h, width).putInt(h + 4, height).putInt(h + 8, flags).putLong(h + 16, frameId);
		map.putLong(0x18, frameId);
		back = (int) INT.getAndSet(map, STATE, back | DIRTY) & 3;
	}

	@Override
	public void close() {
		// The file is closed at open(); the mapping is released when garbage collected. The file is never deleted.
	}
}
