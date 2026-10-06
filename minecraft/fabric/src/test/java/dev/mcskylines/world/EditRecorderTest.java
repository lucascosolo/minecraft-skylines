package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import dev.mcskylines.protocol.BlockEdits;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import org.junit.jupiter.api.Test;

class EditRecorderTest {
    private static String name(String s) {
        return "minecraft:" + s;
    }

    private static BlockEdits wireRoundTrip(BlockEdits b) {
        try {
            return BlockEdits.decode(b.encode());
        } catch (Exception e) {
            throw new AssertionError(e);
        }
    }

    @Test
    void emptyRecorder() {
        EditRecorder<String> r = new EditRecorder<>();
        assertTrue(r.isEmpty());
        assertEquals(0, r.size());
        assertTrue(r.drain(1, EditRecorderTest::name).isEmpty());
    }

    @Test
    void latestStatePerKeyWins() {
        EditRecorder<String> r = new EditRecorder<>();
        long k = BlockKey.pack(1, 2, 3);
        r.record(k, "stone");
        r.record(k, "dirt");
        assertEquals(1, r.size());
        assertFalse(r.isEmpty());
        List<BlockEdits> batches = r.drain(9, EditRecorderTest::name);
        assertEquals(1, batches.size());
        BlockEdits b = batches.get(0);
        assertEquals(9, b.openSeq());
        assertEquals(0, b.flags());
        assertFalse(b.last());
        assertEquals(List.of("minecraft:dirt"), b.palette());
        assertEquals(List.of(new BlockEdits.Edit(1, 2, 3, 0)), b.edits());
    }

    @Test
    void drainEmptiesRecorder() {
        EditRecorder<String> r = new EditRecorder<>();
        r.record(BlockKey.pack(0, 0, 0), "stone");
        r.drain(1, EditRecorderTest::name);
        assertTrue(r.isEmpty());
        assertEquals(0, r.size());
        assertTrue(r.drain(1, EditRecorderTest::name).isEmpty());
    }

    @Test
    void clearEmpties() {
        EditRecorder<String> r = new EditRecorder<>();
        r.record(BlockKey.pack(0, 0, 0), "stone");
        r.record(BlockKey.pack(1, 0, 0), "stone");
        assertEquals(2, r.size());
        r.clear();
        assertTrue(r.isEmpty());
        assertTrue(r.drain(1, EditRecorderTest::name).isEmpty());
    }

    @Test
    void paletteHasExactlyDistinctUsedStates() {
        EditRecorder<String> r = new EditRecorder<>();
        r.record(BlockKey.pack(0, 0, 0), "stone");
        r.record(BlockKey.pack(1, 0, 0), "dirt");
        r.record(BlockKey.pack(2, 0, 0), "stone");
        r.record(BlockKey.pack(-3, -64, 4), "air");
        BlockEdits b = r.drain(2, EditRecorderTest::name).get(0);
        assertEquals(3, b.palette().size());
        assertEquals(Set.of("minecraft:stone", "minecraft:dirt", "minecraft:air"), new HashSet<>(b.palette()));
        assertEquals(4, b.edits().size());
        Map<String, String> byPos = new HashMap<>();
        for (BlockEdits.Edit e : b.edits()) byPos.put(e.x() + "," + e.y() + "," + e.z(), b.palette().get(e.state()));
        assertEquals("minecraft:stone", byPos.get("0,0,0"));
        assertEquals("minecraft:dirt", byPos.get("1,0,0"));
        assertEquals("minecraft:stone", byPos.get("2,0,0"));
        assertEquals("minecraft:air", byPos.get("-3,-64,4"));
    }

    @Test
    void batchingSplitsAtMaxAndCoversEveryKeyOnce() {
        EditRecorder<Integer> r = new EditRecorder<>();
        for (int i = 0; i < 25; i++) r.record(BlockKey.pack(i, i - 10, -i), i % 4);
        List<BlockEdits> batches = r.drain(5, s -> "minecraft:s" + s, 10);
        assertEquals(3, batches.size());
        Set<String> seen = new HashSet<>();
        int total = 0;
        for (BlockEdits b : batches) {
            assertTrue(b.edits().size() <= 10);
            assertEquals(5, b.openSeq());
            assertEquals(0, b.flags());
            assertEquals(b.palette().size(), new HashSet<>(b.palette()).size());
            Set<Integer> used = new HashSet<>();
            for (BlockEdits.Edit e : b.edits()) {
                used.add(e.state());
                assertTrue(seen.add(e.x() + "," + e.y() + "," + e.z()), "key repeated across batches");
                assertEquals("minecraft:s" + (e.x() % 4), b.palette().get(e.state()));
                total++;
            }
            assertEquals(b.palette().size(), used.size(), "palette has unused entries");
        }
        assertEquals(25, total);
        assertTrue(r.isEmpty());
    }

    @Test
    void defaultMaxPerBatchIsProtocolMax() {
        EditRecorder<String> r = new EditRecorder<>();
        int n = BlockEdits.MAX_EDITS + 5;
        for (int i = 0; i < n; i++) r.record(BlockKey.pack(i, 0, 0), "stone");
        List<BlockEdits> batches = r.drain(1, EditRecorderTest::name);
        assertEquals(2, batches.size());
        assertEquals(n, batches.get(0).edits().size() + batches.get(1).edits().size());
        for (BlockEdits b : batches) {
            assertTrue(b.edits().size() <= BlockEdits.MAX_EDITS);
            assertEquals(b, wireRoundTrip(b));
        }
    }

    @Test
    void producedBatchesSurviveWireRoundTrip() {
        EditRecorder<String> r = new EditRecorder<>();
        r.record(BlockKey.pack(-30000000, -64, 29999999), "stone");
        r.record(BlockKey.pack(10, 64, -21), "oak_stairs[facing=north]");
        for (BlockEdits b : r.drain(7, EditRecorderTest::name)) assertEquals(b, wireRoundTrip(b));
    }
}
