package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import dev.mcskylines.protocol.LightSources;
import dev.mcskylines.world.LampPlan.Cell;
import it.unimi.dsi.fastutil.longs.Long2IntMap;
import it.unimi.dsi.fastutil.longs.Long2IntOpenHashMap;
import it.unimi.dsi.fastutil.longs.LongOpenHashSet;
import java.util.List;
import java.util.function.LongFunction;
import org.junit.jupiter.api.Test;

class LampPlanTest {
    private static final long A = BlockKey.pack(1, 2, 3);

    private static Long2IntMap map(long k, int level) {
        Long2IntMap m = new Long2IntOpenHashMap();
        m.put(k, level);
        return m;
    }

    private static Long2IntMap none() {
        return new Long2IntOpenHashMap();
    }

    private static LongFunction<Cell> all(Cell c) {
        return k -> c;
    }

    private static void assertNothing(LampPlan p) {
        assertTrue(p.place().isEmpty());
        assertTrue(p.remove().isEmpty());
        assertTrue(p.forget().isEmpty());
    }

    @Test
    void wantedTakesMaxOnDuplicates() {
        Long2IntOpenHashMap w = LampPlan.wanted(List.of(
                new LightSources.Light(1, 2, 3, 4), new LightSources.Light(1, 2, 3, 9), new LightSources.Light(1, 2, 3, 6),
                new LightSources.Light(0, 0, 0, 2)));
        assertEquals(2, w.size());
        assertEquals(9, w.get(A));
        assertEquals(2, w.get(BlockKey.pack(0, 0, 0)));
    }

    @Test
    void wantedSkipsOutOfRangePositions() {
        Long2IntOpenHashMap w = LampPlan.wanted(List.of(
                new LightSources.Light(1, -100000, 3, 5), new LightSources.Light(1, 100000, 3, 5),
                new LightSources.Light(1, 2, 3, 5)));
        assertEquals(1, w.size());
        assertTrue(w.containsKey(A));
        assertTrue(LampPlan.wanted(List.of()).isEmpty());
    }

    @Test
    void oursWithSameLevelDoesNothing() {
        assertNothing(LampPlan.plan(map(A, 7), map(A, 7), all(Cell.OURS)));
    }

    @Test
    void oursWithOtherLevelIsReplaced() {
        LampPlan p = LampPlan.plan(map(A, 3), map(A, 7), all(Cell.OURS));
        assertEquals(1, p.place().size());
        assertEquals(7, p.place().get(A));
        assertTrue(p.remove().isEmpty());
        assertTrue(p.forget().isEmpty());
    }

    @Test
    void airIsPlaced() {
        LampPlan p = LampPlan.plan(none(), map(A, 5), all(Cell.AIR));
        assertEquals(5, p.place().get(A));
        assertEquals(1, p.place().size());
        assertTrue(p.remove().isEmpty());
        assertTrue(p.forget().isEmpty());
    }

    @Test
    void airWhereWeThoughtWePlacedIsPlacedAgainAndNotForgotten() {
        LampPlan p = LampPlan.plan(map(A, 5), map(A, 5), all(Cell.AIR));
        assertEquals(5, p.place().get(A));
        assertTrue(p.forget().isEmpty());
        assertTrue(p.remove().isEmpty());
    }

    @Test
    void otherIsNeverOverwrittenAndNotTracked() {
        assertNothing(LampPlan.plan(none(), map(A, 5), all(Cell.OTHER)));
    }

    @Test
    void otherWhereWePlacedIsForgottenNotRemoved() {
        LampPlan p = LampPlan.plan(map(A, 5), map(A, 5), all(Cell.OTHER));
        assertTrue(p.place().isEmpty());
        assertTrue(p.remove().isEmpty());
        assertEquals(LongOpenHashSet.of(A), p.forget());
    }

    @Test
    void unloadedWantedDoesNothing() {
        assertNothing(LampPlan.plan(none(), map(A, 5), all(Cell.UNLOADED)));
        assertNothing(LampPlan.plan(map(A, 5), map(A, 6), all(Cell.UNLOADED)));
    }

    @Test
    void placedNotWantedAndOursIsRemoved() {
        LampPlan p = LampPlan.plan(map(A, 5), none(), all(Cell.OURS));
        assertEquals(LongOpenHashSet.of(A), p.remove());
        assertTrue(p.place().isEmpty());
        assertTrue(p.forget().isEmpty());
    }

    @Test
    void placedNotWantedAndAirOrOtherIsForgotten() {
        for (Cell c : new Cell[] {Cell.AIR, Cell.OTHER}) {
            LampPlan p = LampPlan.plan(map(A, 5), none(), all(c));
            assertEquals(LongOpenHashSet.of(A), p.forget(), c.name());
            assertTrue(p.remove().isEmpty(), c.name());
            assertTrue(p.place().isEmpty(), c.name());
        }
    }

    @Test
    void placedNotWantedAndUnloadedIsKept() {
        assertNothing(LampPlan.plan(map(A, 5), none(), all(Cell.UNLOADED)));
    }

    @Test
    void mixedPlanKeepsKeysInOneSetOnly() {
        long b = BlockKey.pack(10, 2, 3);
        long c = BlockKey.pack(20, 2, 3);
        long d = BlockKey.pack(30, 2, 3);
        Long2IntMap placed = new Long2IntOpenHashMap();
        placed.put(A, 5);
        placed.put(b, 5);
        placed.put(c, 5);
        Long2IntMap wanted = new Long2IntOpenHashMap();
        wanted.put(A, 8);
        wanted.put(d, 4);
        LongFunction<Cell> cells = k -> k == A ? Cell.OURS : k == b ? Cell.OURS : k == c ? Cell.OTHER : Cell.AIR;
        LampPlan p = LampPlan.plan(placed, wanted, cells);
        assertEquals(2, p.place().size());
        assertEquals(8, p.place().get(A));
        assertEquals(4, p.place().get(d));
        assertEquals(LongOpenHashSet.of(b), p.remove());
        assertEquals(LongOpenHashSet.of(c), p.forget());
    }
}
