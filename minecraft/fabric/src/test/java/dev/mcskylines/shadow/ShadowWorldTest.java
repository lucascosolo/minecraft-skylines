package dev.mcskylines.shadow;

import static org.junit.jupiter.api.Assertions.*;

import dev.mcskylines.collision.CollisionStore;
import dev.mcskylines.collision.SkyTri;
import dev.mcskylines.protocol.CollisionRegion;
import dev.mcskylines.protocol.CollisionReset;
import dev.mcskylines.protocol.Trees;
import dev.mcskylines.world.BlockKey;
import java.util.*;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

class ShadowWorldTest {
    private static final String LOG = "minecraft:oak_log[axis=y]";
    private static int epochCounter = 1000;

    private final Map<Long, String> world = new HashMap<>();
    private final Set<Long> owned = new HashSet<>();
    private final List<Long> cleared = new ArrayList<>();
    private final ShadowWorld sw = new ShadowWorld();
    private int epoch;

    private final ShadowWorld.Host host = new ShadowWorld.Host() {
        public boolean playerOwns(long key) {
            return owned.contains(key);
        }

        public boolean chunkReady(long chunkKey) {
            return true;
        }

        public boolean holds(long key, String block) {
            return block.equals(world.get(key));
        }

        public void place(long key, String block) {
            if (block == null) {
                world.remove(key);
                cleared.add(key);
            } else {
                world.put(key, block);
            }
        }
    };

    @BeforeEach
    void setUp() {
        epoch = ++epochCounter;
        CollisionStore.INSTANCE.accept(new CollisionReset(epoch));
        ShadowWorld.clearTrees();
        ShadowCells.INSTANCE.clear();
        CollisionStore.INSTANCE.accept(flat(10.3f));
    }

    private CollisionRegion flat(float y) {
        float[] v = {0, y, 0, 16, y, 0, 16, y, 16, 0, y, 0, 16, y, 16, 0, y, 16};
        return new CollisionRegion(epoch, 0, 0, v, new short[] {1, 1});
    }

    private static long k(int x, int y, int z) {
        return BlockKey.pack(x, y, z);
    }

    private void ticks() {
        for (int i = 0; i < 6; i++) {
            sw.tick(host);
        }
    }

    private void build() {
        sw.start(7L);
        ticks();
    }

    private List<Long> logsAt(int x, int z) {
        List<Long> out = new ArrayList<>();
        for (var e : world.entrySet()) {
            long key = e.getKey();
            if (LOG.equals(e.getValue()) && BlockKey.x(key) == x && BlockKey.z(key) == z) {
                out.add(key);
            }
        }
        out.sort(Comparator.comparingInt(BlockKey::y));
        return out;
    }

    @Test
    void buildsCrustUnderEveryColumn() {
        build();
        int bottom = 9 - ShadowPlanner.CRUST + 1;
        for (int x = 0; x < 16; x++) {
            for (int z = 0; z < 16; z++) {
                for (int y = bottom; y <= 9; y++) {
                    assertNotNull(world.get(k(x, y, z)), "missing cell " + x + "," + y + "," + z);
                    assertTrue(ShadowCells.INSTANCE.contains(x, y, z), "not tracked " + x + "," + y + "," + z);
                }
                assertEquals("minecraft:grass_block", world.get(k(x, 9, z)));
                assertNull(world.get(k(x, bottom - 1, z)));
                assertFalse(ShadowCells.INSTANCE.contains(x, bottom - 1, z));
            }
        }
    }

    @Test
    void ownedPositionsAreNeverPlaced() {
        owned.add(k(5, 9, 5));
        owned.add(k(5, 6, 5));
        build();
        assertNull(world.get(k(5, 9, 5)));
        assertNull(world.get(k(5, 6, 5)));
        assertFalse(ShadowCells.INSTANCE.contains(5, 9, 5));
        assertNotNull(world.get(k(5, 8, 5)));
        assertNotNull(world.get(k(6, 9, 5)));
    }

    @Test
    void wouldFillIsTrueForPlannedCellsIncludingOwned() {
        owned.add(k(5, 9, 5));
        build();
        assertTrue(sw.wouldFill(k(5, 9, 5)));
        assertTrue(sw.wouldFill(k(3, 4, 3)));
        assertFalse(sw.wouldFill(k(3, 3, 3)));
        assertFalse(sw.wouldFill(k(3, 40, 3)));
        assertFalse(sw.wouldFill(k(100, 9, 100)));
    }

    @Test
    void playerChangeRemovesCellFromShadowCells() {
        build();
        assertTrue(ShadowCells.INSTANCE.contains(4, 8, 4));
        assertEquals(-1, sw.playerChanged(k(4, 8, 4), true));
        assertFalse(ShadowCells.INSTANCE.contains(4, 8, 4));
        assertTrue(ShadowCells.INSTANCE.contains(4, 9, 4));
    }

    @Test
    void unplannedCellChangeIsIgnored() {
        build();
        assertEquals(-1, sw.playerChanged(k(4, 50, 4), true));
        assertEquals(-1, sw.playerChanged(k(100, 9, 100), true));
        assertTrue(ShadowCells.INSTANCE.contains(4, 9, 4));
    }

    @Test
    void diggingAtTheFloorDeepensNeighbourColumns() {
        build();
        int floor = 9 - ShadowPlanner.CRUST + 1;
        assertNull(world.get(k(8, floor - 1, 8)));
        sw.playerChanged(k(8, floor, 8), true);
        ticks();
        for (int dx = -1; dx <= 1; dx++) {
            for (int dz = -1; dz <= 1; dz++) {
                for (int y = floor - ShadowPlanner.CRUST; y < floor; y++) {
                    assertNotNull(world.get(k(8 + dx, y, 8 + dz)), "not deepened at " + (8 + dx) + "," + y + "," + (8 + dz));
                    assertTrue(ShadowCells.INSTANCE.contains(8 + dx, y, 8 + dz));
                }
            }
        }
        assertNull(world.get(k(11, floor - 1, 8)));
        assertNull(world.get(k(8, floor - ShadowPlanner.CRUST - 1, 8)));
    }

    @Test
    void treeLogsStandAndLastLogReturnsTreeId() {
        ShadowWorld.acceptTrees(new Trees(epoch, 0, 0, List.of(new Trees.Tree(42, 8.5f, 10.3f, 8.5f, 8f, 3f, 0))));
        build();
        List<Long> logs = logsAt(8, 8);
        assertTrue(logs.size() >= 2, "expected a trunk, got " + logs.size());
        for (int i = 0; i < logs.size(); i++) {
            assertEquals(10 + i, BlockKey.y(logs.get(i)), "trunk must be contiguous from y=10");
        }
        for (int i = 0; i < logs.size() - 1; i++) {
            assertEquals(-1, sw.playerChanged(logs.get(i), true), "log " + i);
        }
        assertEquals(42, sw.playerChanged(logs.get(logs.size() - 1), true));
    }

    @Test
    void bushHasNoLogsAndNeverReturnsATreeId() {
        ShadowWorld.acceptTrees(new Trees(epoch, 0, 0, List.of(new Trees.Tree(9, 8.5f, 10.3f, 8.5f, 2f, 1.5f, 6))));
        build();
        assertTrue(world.values().stream().noneMatch(b -> b.contains("_log")));
        for (long key : new ArrayList<>(world.keySet())) {
            assertEquals(-1, sw.playerChanged(key, true));
        }
    }

    @Test
    void resetClearsCellsAndPlan() {
        build();
        assertTrue(ShadowCells.INSTANCE.size() > 0);
        sw.reset();
        assertEquals(0, ShadowCells.INSTANCE.size());
        assertFalse(sw.wouldFill(k(3, 9, 3)));
        assertFalse(sw.wouldFill(k(3, 4, 3)));
    }

    @Test
    void rebuildAfterRegionChangeClearsOldCellsUnlessOwned() {
        build();
        assertNotNull(world.get(k(1, 9, 1)));
        owned.add(k(2, 7, 2));
        // The ground drops to solidTop 4; the floor (y=4) is kept and grass grows at y=5, so cells 6..9 are no longer planned.
        CollisionStore.INSTANCE.accept(flat(5.3f));
        ShadowWorld.regionChanged(0, 0);
        ticks();
        assertNull(world.get(k(1, 9, 1)), "old top cell must be cleared");
        assertNull(world.get(k(1, 6, 1)), "old unplanned cell must be cleared");
        assertTrue(world.get(k(1, 5, 1)).contains("grass"), "grass on the new top");
        assertTrue(cleared.contains(k(1, 9, 1)));
        assertNotNull(world.get(k(2, 7, 2)), "owned cell must stay in the world");
        assertFalse(cleared.contains(k(2, 7, 2)), "owned cell must not be cleared");
        assertEquals("minecraft:grass_block", world.get(k(1, 4, 1)));
        assertTrue(ShadowCells.INSTANCE.contains(1, 4, 1));
        assertFalse(ShadowCells.INSTANCE.contains(1, 9, 1));
    }

    /** Axis-aligned quad over x in [x0,x1], z in [0,16] at height y, as two triangles. */
    private static float[] quadX(float x0, float x1, float y) {
        return new float[] {x0, y, 0, x1, y, 0, x1, y, 16, x0, y, 0, x1, y, 16, x0, y, 16};
    }

    private void feed(float[] a, short fa, float[] b, short fb) {
        float[] v = new float[a.length + b.length];
        System.arraycopy(a, 0, v, 0, a.length);
        System.arraycopy(b, 0, v, a.length, b.length);
        CollisionStore.INSTANCE.accept(new CollisionRegion(epoch, 0, 0, v, new short[] {fa, fa, fb, fb}));
    }

    @Test
    void refusesBreakUnderBuildingButNotOnBareGround() {
        feed(quadX(0, 16, 10.3f), (short) 1, quadX(0, 8, 20f), (short) 8);
        build();
        assertTrue(sw.refusesBreak(k(3, 9, 3)), "planned crust under a building");
        assertTrue(sw.refusesBreak(k(3, 12, 3)), "planned building fill");
        assertFalse(sw.refusesBreak(k(12, 9, 12)), "bare ground column");
        assertFalse(sw.refusesBreak(k(3, 3, 3)), "unplanned cell");
        assertFalse(sw.refusesBreak(k(100, 9, 100)), "unbuilt chunk");
    }

    @Test
    void refusesBreakUnderGroundLevelRoad() {
        feed(quadX(0, 16, 10.3f), (short) 1, quadX(0, 8, 11f), (short) 2);
        build();
        assertTrue(sw.refusesBreak(k(3, 9, 3)));
        assertFalse(sw.refusesBreak(k(12, 9, 12)));
    }

    @Test
    void bridgeDeckCellIsPlannedButNotRefused() {
        feed(quadX(0, 16, 10.3f), (short) 1, quadX(0, 8, 20f), (short) 4);
        build();
        assertTrue(sw.wouldFill(k(3, 19, 3)), "deck cell planned");
        assertFalse(sw.refusesBreak(k(3, 19, 3)));
        assertFalse(sw.refusesBreak(k(3, 9, 3)));
    }

    @Test
    void refusesBreakIsFalseBeforeAnyBuild() {
        assertFalse(sw.refusesBreak(k(3, 9, 3)));
    }

    @Test
    void dugSurfaceAloneStillGetsACrust() {
        CollisionStore.INSTANCE.accept(new CollisionRegion(epoch, 0, 0, quadX(0, 16, 10.3f), new short[] {(short) SkyTri.DUG_SURFACE, (short) SkyTri.DUG_SURFACE}));
        build();
        int bottom = 9 - ShadowPlanner.CRUST + 1;
        for (int y = bottom; y <= 9; y++) {
            assertTrue(sw.wouldFill(k(3, y, 3)), "crust cell y=" + y);
        }
        assertFalse(sw.wouldFill(k(3, bottom - 1, 3)));
        assertFalse(sw.refusesBreak(k(3, 9, 3)), "bare dug surface is not protected");
    }
}
