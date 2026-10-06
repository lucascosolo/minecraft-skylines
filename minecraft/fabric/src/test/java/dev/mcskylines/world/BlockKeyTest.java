package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import it.unimi.dsi.fastutil.longs.LongOpenHashSet;
import org.junit.jupiter.api.Test;

class BlockKeyTest {
    private static void roundTrip(int x, int y, int z) {
        long k = BlockKey.pack(x, y, z);
        assertEquals(x, BlockKey.x(k));
        assertEquals(y, BlockKey.y(k));
        assertEquals(z, BlockKey.z(k));
    }

    @Test
    void roundTripsOrdinaryAndNegative() {
        roundTrip(0, 0, 0);
        roundTrip(10, 64, -21);
        roundTrip(-1, -1, -1);
        roundTrip(-30000000, -64, 29999999);
    }

    @Test
    void roundTripsExtremes() {
        int[] xs = {-33554432, 33554431};
        int[] ys = {-2048, 2047};
        for (int x : xs) for (int y : ys) for (int z : xs) roundTrip(x, y, z);
    }

    @Test
    void rejectsOutOfRange() {
        assertThrows(IllegalArgumentException.class, () -> BlockKey.pack(33554432, 0, 0));
        assertThrows(IllegalArgumentException.class, () -> BlockKey.pack(-33554433, 0, 0));
        assertThrows(IllegalArgumentException.class, () -> BlockKey.pack(0, 2048, 0));
        assertThrows(IllegalArgumentException.class, () -> BlockKey.pack(0, -2049, 0));
        assertThrows(IllegalArgumentException.class, () -> BlockKey.pack(0, 0, 33554432));
        assertThrows(IllegalArgumentException.class, () -> BlockKey.pack(0, 0, -33554433));
    }

    @Test
    void fitsMatchesRange() {
        assertTrue(BlockKey.fits(-33554432, -2048, 33554431));
        assertTrue(BlockKey.fits(33554431, 2047, -33554432));
        assertFalse(BlockKey.fits(33554432, 0, 0));
        assertFalse(BlockKey.fits(0, 2048, 0));
        assertFalse(BlockKey.fits(0, 0, -33554433));
        assertFalse(BlockKey.fits(0, -2049, 0));
    }

    @Test
    void distinctPositionsDistinctKeys() {
        LongOpenHashSet seen = new LongOpenHashSet();
        int n = 0;
        for (int x = -3; x <= 3; x++)
            for (int y = -3; y <= 3; y++)
                for (int z = -3; z <= 3; z++) {
                    seen.add(BlockKey.pack(x, y, z));
                    n++;
                }
        assertEquals(n, seen.size());
    }

    @Test
    void chunkKeyPacksChunkCoordinates() {
        long ck = BlockKey.chunkKey(BlockKey.pack(17, 5, -1));
        assertEquals(1, BlockKey.chunkX(ck));
        assertEquals(-1, BlockKey.chunkZ(ck));
        assertEquals((1L & 0xFFFFFFFFL) | ((long) -1 << 32), ck);
    }

    @Test
    void chunkKeyUsesFloorShift() {
        assertEquals(0, BlockKey.chunkX(BlockKey.chunkKey(BlockKey.pack(15, 0, 0))));
        assertEquals(1, BlockKey.chunkX(BlockKey.chunkKey(BlockKey.pack(16, 0, 0))));
        assertEquals(-1, BlockKey.chunkX(BlockKey.chunkKey(BlockKey.pack(-1, 0, 0))));
        assertEquals(-1, BlockKey.chunkX(BlockKey.chunkKey(BlockKey.pack(-16, 0, 0))));
        assertEquals(-2, BlockKey.chunkZ(BlockKey.chunkKey(BlockKey.pack(0, 0, -17))));
        assertEquals(-1875000, BlockKey.chunkX(BlockKey.chunkKey(BlockKey.pack(-30000000, -64, 29999999))));
        assertEquals(1874999, BlockKey.chunkZ(BlockKey.chunkKey(BlockKey.pack(-30000000, -64, 29999999))));
    }

    @Test
    void sameChunkSameKeyDifferentChunkDifferentKey() {
        assertEquals(BlockKey.chunkKey(BlockKey.pack(0, 0, 0)), BlockKey.chunkKey(BlockKey.pack(15, 100, 15)));
        assertNotEquals(BlockKey.chunkKey(BlockKey.pack(0, 0, 0)), BlockKey.chunkKey(BlockKey.pack(16, 0, 0)));
        assertNotEquals(BlockKey.chunkKey(BlockKey.pack(16, 0, 0)), BlockKey.chunkKey(BlockKey.pack(0, 0, 16)));
        assertNotEquals(BlockKey.chunkKey(BlockKey.pack(-1, 0, 0)), BlockKey.chunkKey(BlockKey.pack(0, 0, -1)));
    }
}
