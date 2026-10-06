package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import it.unimi.dsi.fastutil.longs.Long2ObjectMap;
import it.unimi.dsi.fastutil.longs.Long2ObjectOpenHashMap;
import it.unimi.dsi.fastutil.longs.LongOpenHashSet;
import org.junit.jupiter.api.Test;

class ReconcilePlanTest {
    private static final String STONE = "minecraft:stone";
    private static final String DIRT = "minecraft:dirt";

    private static long k(int x, int y, int z) {
        return BlockKey.pack(x, y, z);
    }

    private static long ck(int x, int z) {
        return BlockKey.chunkKey(k(x, 0, z));
    }

    @Test
    void airConstant() {
        assertEquals("minecraft:air", ReconcilePlan.AIR);
    }

    @Test
    void emptyEmpty() {
        ReconcilePlan p = ReconcilePlan.plan(new LongOpenHashSet(), new Long2ObjectOpenHashMap<>());
        assertTrue(p.byChunk().isEmpty());
        assertEquals(0, p.edits());
        assertEquals(0, p.reverts());
    }

    @Test
    void touchedOnlyRevertsToAir() {
        ReconcilePlan p = ReconcilePlan.plan(new LongOpenHashSet(new long[] {k(1, 2, 3), k(2, 2, 3)}),
                new Long2ObjectOpenHashMap<>());
        assertEquals(0, p.edits());
        assertEquals(2, p.reverts());
        assertEquals(1, p.byChunk().size());
        Long2ObjectMap<String> chunk = p.byChunk().get(ck(1, 3));
        assertEquals(2, chunk.size());
        assertEquals(ReconcilePlan.AIR, chunk.get(k(1, 2, 3)));
        assertEquals(ReconcilePlan.AIR, chunk.get(k(2, 2, 3)));
    }

    @Test
    void targetOnlyAppliesStates() {
        Long2ObjectOpenHashMap<String> target = new Long2ObjectOpenHashMap<>();
        target.put(k(1, 2, 3), STONE);
        target.put(k(40, -5, 3), DIRT);
        ReconcilePlan p = ReconcilePlan.plan(new LongOpenHashSet(), target);
        assertEquals(2, p.edits());
        assertEquals(0, p.reverts());
        assertEquals(2, p.byChunk().size());
        assertEquals(STONE, p.byChunk().get(ck(1, 3)).get(k(1, 2, 3)));
        assertEquals(DIRT, p.byChunk().get(ck(40, 3)).get(k(40, -5, 3)));
    }

    @Test
    void overlapTakesTargetStateNotAir() {
        Long2ObjectOpenHashMap<String> target = new Long2ObjectOpenHashMap<>();
        target.put(k(1, 2, 3), STONE);
        LongOpenHashSet touched = new LongOpenHashSet(new long[] {k(1, 2, 3), k(2, 2, 3)});
        ReconcilePlan p = ReconcilePlan.plan(touched, target);
        assertEquals(1, p.edits());
        assertEquals(1, p.reverts());
        Long2ObjectMap<String> chunk = p.byChunk().get(ck(1, 3));
        assertEquals(2, chunk.size());
        assertEquals(STONE, chunk.get(k(1, 2, 3)));
        assertEquals(ReconcilePlan.AIR, chunk.get(k(2, 2, 3)));
    }

    @Test
    void groupsAcrossChunksIncludingNegatives() {
        Long2ObjectOpenHashMap<String> target = new Long2ObjectOpenHashMap<>();
        target.put(k(0, 0, 0), STONE);
        target.put(k(15, 9, 15), STONE);
        target.put(k(-1, 0, -1), DIRT);
        target.put(k(-16, 0, -17), DIRT);
        LongOpenHashSet touched = new LongOpenHashSet(new long[] {k(16, 0, 0), k(-30000000, -64, 29999999)});
        ReconcilePlan p = ReconcilePlan.plan(touched, target);
        assertEquals(4, p.edits());
        assertEquals(2, p.reverts());
        assertEquals(5, p.byChunk().size());
        assertEquals(2, p.byChunk().get(ck(0, 0)).size());
        assertEquals(DIRT, p.byChunk().get(ck(-1, -1)).get(k(-1, 0, -1)));
        assertEquals(DIRT, p.byChunk().get(ck(-16, -17)).get(k(-16, 0, -17)));
        assertEquals(ReconcilePlan.AIR, p.byChunk().get(ck(16, 0)).get(k(16, 0, 0)));
        assertEquals(ReconcilePlan.AIR,
                p.byChunk().get(ck(-30000000, 29999999)).get(k(-30000000, -64, 29999999)));
        int total = 0;
        for (Long2ObjectMap<String> c : p.byChunk().values()) total += c.size();
        assertEquals(6, total);
    }

    @Test
    void doesNotMutateInputs() {
        Long2ObjectOpenHashMap<String> target = new Long2ObjectOpenHashMap<>();
        target.put(k(1, 1, 1), STONE);
        LongOpenHashSet touched = new LongOpenHashSet(new long[] {k(1, 1, 1), k(2, 2, 2)});
        ReconcilePlan.plan(touched, target);
        assertEquals(1, target.size());
        assertEquals(STONE, target.get(k(1, 1, 1)));
        assertEquals(new LongOpenHashSet(new long[] {k(1, 1, 1), k(2, 2, 2)}), touched);
    }
}
