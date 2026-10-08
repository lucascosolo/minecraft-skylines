package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.Test;

class ProxyPlanTest {
    private static void check(ProxyPlan.Plan p, int[] spawn, int[] remove, int[] keep) {
        assertArrayEquals(spawn, p.spawn(), "spawn");
        assertArrayEquals(remove, p.remove(), "remove");
        assertArrayEquals(keep, p.keep(), "keep");
    }

    @Test
    void emptyInputs() {
        check(ProxyPlan.reconcile(new int[0], new int[0], 5), new int[0], new int[0], new int[0]);
    }

    @Test
    void spawnsAllWhenNoneExist() {
        check(ProxyPlan.reconcile(new int[0], new int[] {9, 3, 5}, 10), new int[] {9, 3, 5}, new int[0], new int[0]);
    }

    @Test
    void removesAllWhenNothingWanted() {
        check(ProxyPlan.reconcile(new int[] {8, 2, 5}, new int[0], 10), new int[0], new int[] {2, 5, 8}, new int[0]);
    }

    @Test
    void mixed() {
        check(ProxyPlan.reconcile(new int[] {4, 1, 7}, new int[] {7, 2, 1}, 10),
            new int[] {2}, new int[] {4}, new int[] {7, 1});
    }

    @Test
    void capKeepsNearestAndRemovesExistingBeyondCap() {
        check(ProxyPlan.reconcile(new int[] {30, 10}, new int[] {10, 20, 30}, 2),
            new int[] {20}, new int[] {30}, new int[] {10});
    }

    @Test
    void duplicatesInWantedAreIgnored() {
        check(ProxyPlan.reconcile(new int[] {5}, new int[] {5, 5, 6, 5, 7}, 2),
            new int[] {6}, new int[0], new int[] {5});
    }
}
