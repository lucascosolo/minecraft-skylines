package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import dev.mcskylines.protocol.OreMined;
import org.junit.jupiter.api.Test;

class OreLedgerTest {
    @Test
    void emptyLedgerDrainsNull() {
        OreLedger l = new OreLedger();
        assertTrue(l.isEmpty());
        assertNull(l.drain(1));
    }

    @Test
    void countsOresAndIgnoresOtherBlocks() {
        OreLedger l = new OreLedger();
        l.broke("minecraft:stone", 0, 0);
        l.broke("minecraft:oak_log", 0, 0);
        l.broke("minecraft:iron_ore", 0, 0);
        l.broke("minecraft:deepslate_iron_ore", 1, -1);
        l.broke("minecraft:coal_ore", 0, 0);
        l.broke("minecraft:deepslate_coal_ore", 0, 0);
        assertFalse(l.isEmpty());
        OreMined m = l.drain(9);
        assertEquals(9, m.openSeq());
        assertEquals(2, m.entries().size());
        OreMined.Entry ore = m.entries().get(0);
        assertEquals(OreMined.ORE, ore.resource());
        assertEquals(256, ore.cx());
        assertEquals(256, ore.cz());
        assertEquals(2, ore.blocks());
        OreMined.Entry oil = m.entries().get(1);
        assertEquals(OreMined.OIL, oil.resource());
        assertEquals(2, oil.blocks());
        assertTrue(l.isEmpty());
        assertNull(l.drain(10));
    }

    @Test
    void usesCityHazardsCells() {
        OreLedger l = new OreLedger();
        l.broke("minecraft:gold_ore", 34, -34);
        OreMined.Entry e = l.drain(1).entries().get(0);
        assertEquals(CityHazards.cellX(34), e.cx());
        assertEquals(CityHazards.cellZ(-34), e.cz());
    }

    @Test
    void sortedByResourceThenCxThenCz() {
        OreLedger l = new OreLedger();
        l.broke("minecraft:coal_ore", 0, 0);
        l.broke("minecraft:iron_ore", 100, 0);
        l.broke("minecraft:iron_ore", 0, -100);
        l.broke("minecraft:iron_ore", 0, 0);
        var es = l.drain(1).entries();
        assertEquals(4, es.size());
        for (int i = 1; i < es.size(); i++) {
            OreMined.Entry a = es.get(i - 1), b = es.get(i);
            int c = Integer.compare(a.resource(), b.resource());
            if (c == 0) c = Integer.compare(a.cx(), b.cx());
            if (c == 0) c = Integer.compare(a.cz(), b.cz());
            assertTrue(c < 0, "entries out of order at " + i);
        }
        assertEquals(OreMined.OIL, es.get(3).resource());
    }

    @Test
    void blocksClampedAt65535() {
        OreLedger l = new OreLedger();
        for (int i = 0; i < 70000; i++) l.broke("minecraft:iron_ore", 0, 0);
        assertEquals(65535, l.drain(1).entries().get(0).blocks());
        assertTrue(l.isEmpty());
    }

    @Test
    void drainCapsEntriesAndKeepsRest() {
        OreLedger l = new OreLedger();
        for (int i = 0; i < 300; i++) l.broke("minecraft:iron_ore", (i % 20) * 33.75, -(i / 20) * 33.75);
        OreMined first = l.drain(1);
        assertEquals(256, first.entries().size());
        assertFalse(l.isEmpty());
        OreMined second = l.drain(2);
        assertEquals(44, second.entries().size());
        assertTrue(l.isEmpty());
    }

    @Test
    void clearEmptiesLedger() {
        OreLedger l = new OreLedger();
        l.broke("minecraft:iron_ore", 0, 0);
        l.clear();
        assertTrue(l.isEmpty());
        assertNull(l.drain(1));
    }
}
