package dev.mcskylines.bridge.overlay;

import static org.junit.jupiter.api.Assertions.*;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Arrays;
import java.util.HexFormat;
import java.util.concurrent.atomic.AtomicReference;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.Timeout;
import org.junit.jupiter.api.io.CleanupMode;
import org.junit.jupiter.api.io.TempDir;

class OverlayWriterTest {
    // NEVER: AGENTS.md forbids tests that delete files; JUnit's default cleanup would.
    @TempDir(cleanup = CleanupMode.NEVER) Path tmp;

    private static ByteBuffer le(byte[] b) {
        return ByteBuffer.wrap(b).order(ByteOrder.LITTLE_ENDIAN);
    }

    private static void fill(OverlayWriter w, int width, int height, long id) {
        byte[] px = new byte[width * height * 4];
        ByteBuffer bb = le(px);
        for (int i = 0; i < width * height; i++) bb.putInt((int) id);
        w.backPixels().put(0, px);
        w.publish(width, height, 0, id);
    }

    @Test
    void constantsMatchSpec() {
        assertEquals(0x564F534D, OverlayWriter.MAGIC);
        assertEquals(1, OverlayWriter.LAYOUT_VERSION);
        assertEquals(3, OverlayWriter.SLOT_COUNT);
        assertEquals(4, OverlayWriter.DIRTY);
        assertEquals(0x100, OverlayWriter.HEADER_BYTES);
        assertEquals(0x40, OverlayWriter.SLOT_HEADER_BYTES);
        assertEquals(352, OverlayWriter.fileSize(4, 2));
    }

    @Test
    void goldenFileMatchesVector() throws Exception {
        String dir = System.getProperty("mcskylines.vectors");
        assertNotNull(dir, "system property mcskylines.vectors must be set");
        JsonObject v = JsonParser.parseString(Files.readString(Path.of(dir, "overlay_layout.json"))).getAsJsonObject();
        Path p = tmp.resolve("ov");
        try (OverlayWriter w = OverlayWriter.open(p, 4, 2, 2)) {
            w.backPixels().put(0, HexFormat.of().parseHex("ff0000ff00004080"));
            w.publish(2, 1, 0, 7);
        }
        byte[] file = Files.readAllBytes(p);
        assertEquals(v.get("fileSize").getAsLong(), file.length);
        assertEquals(v.get("fileSize").getAsLong(), OverlayWriter.fileSize(4, 2));
        assertEquals(v.get("hex").getAsString(), HexFormat.of().formatHex(file));
    }

    @Test
    void freshOpenHeader() throws Exception {
        Path p = tmp.resolve("ov");
        try (OverlayWriter w = OverlayWriter.open(p, 4, 2, 9)) {
            assertEquals(p, w.path());
            assertEquals(4, w.maxWidth());
            assertEquals(2, w.maxHeight());
            assertEquals(9L, w.generation());
            assertEquals(0, w.backSlot());
            assertEquals(4 * 2 * 4, w.backPixels().capacity());
            assertEquals(0, w.backPixels().position());
        }
        ByteBuffer b = le(Files.readAllBytes(p));
        assertEquals(0x564F534D, b.getInt(0x00));
        assertEquals(1, b.getInt(0x04));
        assertEquals(4, b.getInt(0x08));
        assertEquals(2, b.getInt(0x0C));
        assertEquals(3, b.getInt(0x10));
        assertEquals(1, b.getInt(0x14));
        assertEquals(0L, b.getLong(0x18));
        assertEquals(9L, b.getLong(0x20));
    }

    @Test
    void reuseLargerFileKeepsSizeAndReinitialisesHeader() throws Exception {
        Path p = tmp.resolve("ov");
        long size = OverlayWriter.fileSize(4, 2) + 1000;
        byte[] junk = new byte[(int) size];
        Arrays.fill(junk, (byte) 0x55);
        Files.write(p, junk);
        OverlayWriter.open(p, 4, 2, 3).close();
        assertEquals(size, Files.size(p));
        ByteBuffer b = le(Files.readAllBytes(p));
        assertEquals(0x564F534D, b.getInt(0));
        assertEquals(1, b.getInt(0x14));
        assertEquals(3L, b.getLong(0x20));
        for (int i = 0x40; i < 0x100; i++) assertEquals(0, b.get(i), "slot header byte " + i);
        assertTrue(Files.exists(p));
    }

    @Test
    void smallerFileIsGrown() throws Exception {
        Path p = tmp.resolve("ov");
        Files.write(p, new byte[10]);
        OverlayWriter.open(p, 4, 2, 1).close();
        assertEquals(OverlayWriter.fileSize(4, 2), Files.size(p));
        assertTrue(Files.exists(p));
    }

    @Test
    void badDimensionsRejected() throws Exception {
        try (OverlayWriter w = OverlayWriter.open(tmp.resolve("ov"), 4, 2, 1)) {
            assertThrows(IllegalArgumentException.class, () -> w.publish(0, 1, 0, 1));
            assertThrows(IllegalArgumentException.class, () -> w.publish(1, 0, 0, 1));
            assertThrows(IllegalArgumentException.class, () -> w.publish(5, 1, 0, 1));
            assertThrows(IllegalArgumentException.class, () -> w.publish(1, 3, 0, 1));
        }
    }

    @Test
    void sequentialPollSemantics() throws Exception {
        Path p = tmp.resolve("ov");
        try (OverlayWriter w = OverlayWriter.open(p, 4, 2, 1); OverlayTestReader r = new OverlayTestReader(p)) {
            assertNull(r.poll());
            fill(w, 2, 1, 1);
            OverlayTestReader.Frame f = r.poll();
            assertNotNull(f);
            assertEquals(1L, f.frameId());
            assertEquals(2, f.width());
            assertEquals(1, f.height());
            assertEquals(8, f.pixels().length);
            assertNull(r.poll());
        }
    }

    @Test
    void latestWinsAndSlotsStayPermutation() throws Exception {
        Path p = tmp.resolve("ov");
        try (OverlayWriter w = OverlayWriter.open(p, 4, 2, 1); OverlayTestReader r = new OverlayTestReader(p)) {
            for (long id = 1; id <= 3; id++) {
                fill(w, 2, 2, id);
                int[] slots = {w.backSlot(), r.middle(), r.front()};
                Arrays.sort(slots);
                assertArrayEquals(new int[] {0, 1, 2}, slots);
            }
            OverlayTestReader.Frame f = r.poll();
            assertNotNull(f);
            assertEquals(3L, f.frameId());
            int[] slots = {w.backSlot(), r.middle(), r.front()};
            Arrays.sort(slots);
            assertArrayEquals(new int[] {0, 1, 2}, slots);
            assertNull(r.poll());
        }
    }

    @Test
    @Timeout(60)
    void concurrentStressNoTornFrames() throws Exception {
        final int n = 20000;
        Path p = tmp.resolve("ov");
        AtomicReference<Throwable> writerError = new AtomicReference<>();
        try (OverlayWriter w = OverlayWriter.open(p, 16, 16, 1); OverlayTestReader r = new OverlayTestReader(p)) {
            Thread t = new Thread(() -> {
                try {
                    for (long id = 1; id <= n; id++) fill(w, 16, 16, id);
                } catch (Throwable e) {
                    writerError.set(e);
                }
            });
            t.start();
            long last = 0;
            while (last < n) {
                OverlayTestReader.Frame f = r.poll();
                if (f == null) {
                    if (!t.isAlive() && writerError.get() != null) break;
                    Thread.onSpinWait();
                    continue;
                }
                assertTrue(f.frameId() > last, "frameId not increasing: " + f.frameId() + " after " + last);
                assertEquals(16, f.width());
                assertEquals(16, f.height());
                ByteBuffer px = le(f.pixels());
                for (int i = 0; i < 256; i++) {
                    assertEquals((int) f.frameId(), px.getInt(), "torn pixel " + i + " of frame " + f.frameId());
                }
                last = f.frameId();
            }
            t.join();
            assertNull(writerError.get());
            assertEquals(n, last);
        }
    }
}
