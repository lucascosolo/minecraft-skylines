package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import it.unimi.dsi.fastutil.longs.LongOpenHashSet;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;
import java.util.Random;
import java.util.stream.Stream;
import java.util.zip.CRC32;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class TouchedFileTest {
    private static LongOpenHashSet setOf(long... keys) {
        return new LongOpenHashSet(keys);
    }

    private static byte[] withCrc(byte[] body) {
        CRC32 crc = new CRC32();
        crc.update(body);
        ByteBuffer b = ByteBuffer.allocate(body.length + 4).order(ByteOrder.LITTLE_ENDIAN);
        b.put(body).putInt((int) crc.getValue());
        return b.array();
    }

    private static byte[] oneEntryBody(int version, int count, int x, int y, int z) {
        return ByteBuffer.allocate(9 + 12).order(ByteOrder.LITTLE_ENDIAN)
                .put("MSKT".getBytes(StandardCharsets.US_ASCII)).put((byte) version).putInt(count)
                .putInt(x).putInt(y).putInt(z).array();
    }

    @Test
    void roundTripEmpty() throws Exception {
        assertEquals(new LongOpenHashSet(), TouchedFile.decode(TouchedFile.encode(new LongOpenHashSet())));
    }

    @Test
    void roundTripOne() throws Exception {
        LongOpenHashSet s = setOf(BlockKey.pack(1, 2, 3));
        assertEquals(s, TouchedFile.decode(TouchedFile.encode(s)));
    }

    @Test
    void roundTripManyIncludingExtremes() throws Exception {
        LongOpenHashSet s = new LongOpenHashSet();
        Random r = new Random(42);
        for (int i = 0; i < 2000; i++)
            s.add(BlockKey.pack(r.nextInt(2_000_000) - 1_000_000, r.nextInt(4096) - 2048,
                    r.nextInt(2_000_000) - 1_000_000));
        s.add(BlockKey.pack(-33554432, -2048, -33554432));
        s.add(BlockKey.pack(33554431, 2047, 33554431));
        s.add(BlockKey.pack(-30000000, -64, 29999999));
        assertEquals(s, TouchedFile.decode(TouchedFile.encode(s)));
    }

    @Test
    void goldenLayoutOneElement() throws Exception {
        byte[] expected = withCrc(oneEntryBody(1, 1, -5, 64, 7));
        assertArrayEquals(expected, TouchedFile.encode(setOf(BlockKey.pack(-5, 64, 7))));
        assertEquals(setOf(BlockKey.pack(-5, 64, 7)), TouchedFile.decode(expected));
    }

    @Test
    void entriesAreInAscendingPackedKeyOrder() {
        long[] keys = {BlockKey.pack(5, 0, 0), BlockKey.pack(-3, 9, 2), BlockKey.pack(0, 0, 1),
                BlockKey.pack(7, -7, -7)};
        byte[] bytes = TouchedFile.encode(setOf(keys));
        ByteBuffer b = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN);
        b.position(5);
        assertEquals(4, b.getInt());
        long[] sorted = keys.clone();
        Arrays.sort(sorted);
        for (long k : sorted) {
            assertEquals(BlockKey.x(k), b.getInt());
            assertEquals(BlockKey.y(k), b.getInt());
            assertEquals(BlockKey.z(k), b.getInt());
        }
        assertEquals(bytes.length - 4, b.position());
    }

    @Test
    void encodeIsDeterministicAcrossInsertionOrder() {
        List<Long> keys = new ArrayList<>();
        for (int i = 0; i < 200; i++) keys.add(BlockKey.pack(i * 37 - 3000, i % 50 - 25, 1000 - i * 11));
        byte[] first = TouchedFile.encode(new LongOpenHashSet(keys));
        Collections.shuffle(keys, new Random(7));
        LongOpenHashSet other = new LongOpenHashSet();
        for (long k : keys) other.add(k);
        assertArrayEquals(first, TouchedFile.encode(other));
    }

    @Test
    void rejectsBadMagic() {
        byte[] body = oneEntryBody(1, 1, 1, 2, 3);
        body[0] = 'X';
        assertThrows(IOException.class, () -> TouchedFile.decode(withCrc(body)));
    }

    @Test
    void rejectsUnknownVersion() {
        assertThrows(IOException.class, () -> TouchedFile.decode(withCrc(oneEntryBody(2, 1, 1, 2, 3))));
    }

    @Test
    void rejectsTruncation() {
        byte[] good = TouchedFile.encode(setOf(BlockKey.pack(1, 2, 3), BlockKey.pack(4, 5, 6)));
        for (int cut : new int[] {0, 3, 8, 9, good.length - 5, good.length - 1})
            assertThrows(IOException.class, () -> TouchedFile.decode(Arrays.copyOf(good, cut)), "cut " + cut);
    }

    @Test
    void rejectsCountLargerThanData() {
        byte[] body = oneEntryBody(1, 5, 1, 2, 3);
        assertThrows(IOException.class, () -> TouchedFile.decode(withCrc(body)));
    }

    @Test
    void rejectsTrailingBytes() {
        byte[] body = Arrays.copyOf(oneEntryBody(1, 1, 1, 2, 3), 9 + 12 + 3);
        assertThrows(IOException.class, () -> TouchedFile.decode(withCrc(body)));
        byte[] good = TouchedFile.encode(setOf(BlockKey.pack(1, 2, 3)));
        assertThrows(IOException.class, () -> TouchedFile.decode(Arrays.copyOf(good, good.length + 1)));
    }

    @Test
    void rejectsCrcMismatch() {
        byte[] good = TouchedFile.encode(setOf(BlockKey.pack(1, 2, 3)));
        byte[] flippedBody = good.clone();
        flippedBody[10] ^= 1;
        assertThrows(IOException.class, () -> TouchedFile.decode(flippedBody));
        byte[] flippedCrc = good.clone();
        flippedCrc[good.length - 1] ^= 1;
        assertThrows(IOException.class, () -> TouchedFile.decode(flippedCrc));
    }

    @Test
    void rejectsCoordinateThatDoesNotFit() {
        assertThrows(IOException.class, () -> TouchedFile.decode(withCrc(oneEntryBody(1, 1, 33554432, 0, 0))));
        assertThrows(IOException.class, () -> TouchedFile.decode(withCrc(oneEntryBody(1, 1, 0, 2048, 0))));
        assertThrows(IOException.class, () -> TouchedFile.decode(withCrc(oneEntryBody(1, 1, 0, 0, -33554433))));
    }

    @Test
    void writeThenReadCreatesParentsAndLeavesNoTmp(@TempDir Path dir) throws Exception {
        Path file = dir.resolve("a/b/touched.mskt");
        LongOpenHashSet s = setOf(BlockKey.pack(1, 2, 3), BlockKey.pack(-9, -8, -7));
        TouchedFile.write(file, s);
        assertTrue(Files.isRegularFile(file));
        assertEquals(s, TouchedFile.decode(Files.readAllBytes(file)));
        assertEquals(s, TouchedFile.read(file));
        try (Stream<Path> st = Files.list(file.getParent())) {
            assertTrue(st.noneMatch(p -> p.getFileName().toString().endsWith(".tmp")));
        }
    }

    @Test
    void writeOverwritesExistingFile(@TempDir Path dir) throws Exception {
        Path file = dir.resolve("touched.mskt");
        TouchedFile.write(file, setOf(BlockKey.pack(1, 1, 1)));
        LongOpenHashSet second = setOf(BlockKey.pack(2, 2, 2), BlockKey.pack(3, 3, 3));
        TouchedFile.write(file, second);
        assertEquals(second, TouchedFile.read(file));
        try (Stream<Path> st = Files.list(dir)) {
            assertEquals(1, st.count());
        }
    }

    @Test
    void readMissingFileIsEmpty(@TempDir Path dir) {
        assertTrue(TouchedFile.read(dir.resolve("nope.mskt")).isEmpty());
    }

    @Test
    void readCorruptFileIsEmptyAndMovedAside(@TempDir Path dir) throws Exception {
        Path file = dir.resolve("touched.mskt");
        byte[] garbage = "definitely not a touched file".getBytes(StandardCharsets.US_ASCII);
        Files.write(file, garbage);
        assertTrue(TouchedFile.read(file).isEmpty());
        assertFalse(Files.exists(file));
        List<Path> aside;
        try (Stream<Path> st = Files.list(dir)) {
            aside = st.filter(p -> p.getFileName().toString().startsWith("touched.mskt.unreadable-")).toList();
        }
        assertEquals(1, aside.size());
        assertArrayEquals(garbage, Files.readAllBytes(aside.get(0)));
    }
}
