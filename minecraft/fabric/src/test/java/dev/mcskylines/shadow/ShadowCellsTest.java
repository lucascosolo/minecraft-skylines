package dev.mcskylines.shadow;

import static org.junit.jupiter.api.Assertions.*;

import dev.mcskylines.world.BlockKey;
import it.unimi.dsi.fastutil.longs.LongOpenHashSet;
import it.unimi.dsi.fastutil.longs.LongSet;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicReference;
import org.junit.jupiter.api.Test;

class ShadowCellsTest {
    private static LongSet set(long... keys) {
        LongOpenHashSet s = new LongOpenHashSet();
        for (long k : keys) s.add(k);
        return s;
    }

    @Test
    void setChunkReplacesAndEmptyClears() {
        ShadowCells c = new ShadowCells();
        long a = BlockKey.pack(1, 2, 3), b = BlockKey.pack(2, 2, 3);
        long chunk = BlockKey.chunkKey(a);
        c.setChunk(chunk, set(a));
        assertTrue(c.contains(1, 2, 3));
        c.setChunk(chunk, set(b));
        assertFalse(c.contains(1, 2, 3));
        assertTrue(c.contains(2, 2, 3));
        assertEquals(1, c.size());
        c.setChunk(chunk, set());
        assertEquals(0, c.size());
    }

    @Test
    void otherChunksUntouchedAndAbsent() {
        ShadowCells c = new ShadowCells();
        long a = BlockKey.pack(1, 2, 3), far = BlockKey.pack(100, 2, 100);
        c.setChunk(BlockKey.chunkKey(a), set(a));
        c.setChunk(BlockKey.chunkKey(far), set(far));
        c.setChunk(BlockKey.chunkKey(a), set());
        assertTrue(c.contains(100, 2, 100));
        assertFalse(c.contains(101, 2, 100));
        assertEquals(1, c.size());
    }

    @Test
    void removeAffectsOnlyThatKeyAndClearEmpties() {
        ShadowCells c = new ShadowCells();
        long a = BlockKey.pack(1, 2, 3), b = BlockKey.pack(2, 2, 3);
        c.setChunk(BlockKey.chunkKey(a), set(a, b));
        c.remove(a);
        assertFalse(c.contains(1, 2, 3));
        assertTrue(c.contains(2, 2, 3));
        c.clear();
        assertEquals(0, c.size());
        assertFalse(c.contains(2, 2, 3));
    }

    @Test
    void ignoresCollisionTruthTable() {
        ShadowCells c = new ShadowCells();
        long a = BlockKey.pack(1, 2, 3);
        c.setChunk(BlockKey.chunkKey(a), set(a));
        assertTrue(c.ignoresCollision(true, 1, 2, 3, true));
        assertFalse(c.ignoresCollision(false, 1, 2, 3, true));
        assertFalse(c.ignoresCollision(true, 1, 2, 3, false));
        assertFalse(c.ignoresCollision(false, 1, 2, 3, false));
        assertFalse(c.ignoresCollision(true, 9, 9, 9, true));
    }

    @Test
    void concurrentReadersWhileWriterReplaces() throws Exception {
        ShadowCells c = new ShadowCells();
        ExecutorService ex = Executors.newFixedThreadPool(5);
        AtomicReference<Throwable> err = new AtomicReference<>();
        long end = System.nanoTime() + 300_000_000L;
        Runnable reader = () -> {
            try {
                while (System.nanoTime() < end) {
                    for (int x = 0; x < 16; x++) {
                        c.contains(x, 5, x);
                        c.ignoresCollision(true, x, 5, x, true);
                    }
                    c.size();
                }
            } catch (Throwable t) {
                err.set(t);
            }
        };
        Runnable writer = () -> {
            try {
                int n = 0;
                long chunk = BlockKey.chunkKey(BlockKey.pack(0, 5, 0));
                while (System.nanoTime() < end) {
                    LongOpenHashSet s = new LongOpenHashSet();
                    if (n++ % 2 == 0) for (int x = 0; x < 16; x++) s.add(BlockKey.pack(x, 5, x));
                    c.setChunk(chunk, s);
                    c.remove(BlockKey.pack(3, 5, 3));
                }
            } catch (Throwable t) {
                err.set(t);
            }
        };
        Future<?>[] fs = new Future<?>[5];
        for (int i = 0; i < 4; i++) fs[i] = ex.submit(reader);
        fs[4] = ex.submit(writer);
        for (Future<?> f : fs) f.get(10, TimeUnit.SECONDS);
        ex.shutdown();
        assertNull(err.get());
    }
}
