package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import it.unimi.dsi.fastutil.longs.LongOpenHashSet;
import org.junit.jupiter.api.Test;

class TouchedSetTest {
    private static final long A = BlockKey.pack(1, 2, 3);
    private static final long B = BlockKey.pack(-4, 5, 6);

    @Test
    void startsEmptyAndClean() {
        TouchedSet s = new TouchedSet();
        assertTrue(s.live().isEmpty());
        assertTrue(s.persistent().isEmpty());
        assertFalse(s.dirty());
        assertFalse(s.contains(A));
    }

    @Test
    void addMakesLiveAndPersistent() {
        TouchedSet s = new TouchedSet();
        s.add(A);
        assertTrue(s.contains(A));
        assertTrue(s.live().contains(A));
        assertTrue(s.persistent().contains(A));
        assertTrue(s.dirty());
    }

    @Test
    void initialKeysAreLiveAndNotDirty() {
        TouchedSet s = new TouchedSet(new LongOpenHashSet(new long[] {A, B}));
        assertTrue(s.contains(A));
        assertTrue(s.contains(B));
        assertEquals(2, s.persistent().size());
        assertFalse(s.dirty());
    }

    @Test
    void markWrittenClearsDirty() {
        TouchedSet s = new TouchedSet();
        s.add(A);
        s.markWritten();
        assertFalse(s.dirty());
    }

    @Test
    void readdingKnownKeyDoesNotDirty() {
        TouchedSet s = new TouchedSet(new LongOpenHashSet(new long[] {A}));
        s.add(A);
        assertFalse(s.dirty());
    }

    @Test
    void removedKeyLeavesLiveButStaysPersistentUntilFlush() {
        TouchedSet s = new TouchedSet(new LongOpenHashSet(new long[] {A, B}));
        s.remove(A);
        assertFalse(s.contains(A));
        assertFalse(s.live().contains(A));
        assertTrue(s.live().contains(B));
        assertTrue(s.persistent().contains(A));
        assertTrue(s.persistent().contains(B));
        s.savedWithFlush();
        assertFalse(s.persistent().contains(A));
        assertTrue(s.persistent().contains(B));
        assertEquals(1, s.persistent().size());
    }

    @Test
    void readdingRemovedKeyMakesItLiveAgain() {
        TouchedSet s = new TouchedSet(new LongOpenHashSet(new long[] {A}));
        s.remove(A);
        s.add(A);
        assertTrue(s.contains(A));
        s.savedWithFlush();
        assertTrue(s.persistent().contains(A));
        assertTrue(s.live().contains(A));
    }

    @Test
    void removingUnknownKeyIsHarmless() {
        TouchedSet s = new TouchedSet();
        s.remove(A);
        assertTrue(s.persistent().isEmpty());
        assertFalse(s.dirty());
    }
}
