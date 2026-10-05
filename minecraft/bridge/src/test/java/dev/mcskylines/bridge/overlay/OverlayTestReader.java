package dev.mcskylines.bridge.overlay;

import java.io.IOException;
import java.lang.invoke.MethodHandles;
import java.lang.invoke.VarHandle;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.MappedByteBuffer;
import java.nio.channels.FileChannel;
import java.nio.file.Path;
import java.nio.file.StandardOpenOption;

/** Host side of the triple buffer, per protocol "Shared-memory layout". */
final class OverlayTestReader implements AutoCloseable {
    record Frame(int slot, int width, int height, int flags, long frameId, byte[] pixels) {}

    private static final VarHandle INT =
            MethodHandles.byteBufferViewVarHandle(int[].class, ByteOrder.LITTLE_ENDIAN);
    private static final int STATE = 0x14;
    private static final int DIRTY = 4;

    private final FileChannel channel;
    private final MappedByteBuffer map;
    private final int maxWidth;
    private final int maxHeight;
    private int front = 2;

    OverlayTestReader(Path path) throws IOException {
        channel = FileChannel.open(path, StandardOpenOption.READ, StandardOpenOption.WRITE);
        map = channel.map(FileChannel.MapMode.READ_WRITE, 0, channel.size());
        map.order(ByteOrder.LITTLE_ENDIAN);
        maxWidth = map.getInt(0x08);
        maxHeight = map.getInt(0x0C);
    }

    int front() { return front; }

    int state() { return (int) INT.getVolatile(map, STATE); }

    int middle() { return state() & 3; }

    Frame poll() {
        if ((state() & DIRTY) == 0) return null;
        int old = (int) INT.getAndSet(map, STATE, front);
        front = old & 3;
        int h = 0x40 + 0x40 * front;
        int width = map.getInt(h);
        int height = map.getInt(h + 4);
        int flags = map.getInt(h + 8);
        long frameId = map.getLong(h + 16);
        byte[] px = new byte[width * height * 4];
        map.get(0x100 + front * maxWidth * maxHeight * 4, px);
        return new Frame(front, width, height, flags, frameId, px);
    }

    @Override
    public void close() throws IOException {
        channel.close();
    }
}
