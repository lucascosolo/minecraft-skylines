package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import dev.mcskylines.world.CityEntityRule.Fate;
import java.util.Set;
import org.junit.jupiter.api.Test;

class CityEntityRuleTest {
    @Test
    void savedOnlyForPlainSerializableLiveNonPlayers() {
        assertTrue(CityEntityRule.saved(false, false, true, false));
        assertFalse(CityEntityRule.saved(true, false, true, false));
        assertFalse(CityEntityRule.saved(false, true, true, false));
        assertFalse(CityEntityRule.saved(false, false, false, false));
        assertFalse(CityEntityRule.saved(false, false, true, true));
    }

    @Test
    void tagFormat() {
        assertEquals("mcskylines.gen.", CityEntityRule.TAG_PREFIX);
        assertEquals("mcskylines.gen.0", CityEntityRule.tag(0));
        assertEquals("mcskylines.gen.ff", CityEntityRule.tag(255));
        assertTrue(CityEntityRule.tag(-1).endsWith("ffffffffffffffff"));
    }

    @Test
    void isGenTag() {
        assertTrue(CityEntityRule.isGenTag(CityEntityRule.tag(7)));
        assertFalse(CityEntityRule.isGenTag("foo"));
        assertFalse(CityEntityRule.isGenTag("mcskylines"));
    }

    @Test
    void playerAlwaysKept() {
        assertEquals(Fate.KEEP, CityEntityRule.onLoad(true, Set.of(), 1, false));
        assertEquals(Fate.KEEP, CityEntityRule.onLoad(true, Set.of(CityEntityRule.tag(9)), 1, true));
    }

    @Test
    void currentGenerationKeptEvenWithOtherTags() {
        assertEquals(Fate.KEEP, CityEntityRule.onLoad(false, Set.of(CityEntityRule.tag(5)), 5, false));
        assertEquals(Fate.KEEP, CityEntityRule.onLoad(false, Set.of("foo", CityEntityRule.tag(5)), 5, true));
    }

    @Test
    void otherGenerationDiscarded() {
        assertEquals(Fate.DISCARD, CityEntityRule.onLoad(false, Set.of(CityEntityRule.tag(4)), 5, true));
        assertEquals(Fate.DISCARD, CityEntityRule.onLoad(false, Set.of("foo", CityEntityRule.tag(4)), 5, false));
    }

    @Test
    void untaggedAdoptedOnlyWhenCityReady() {
        assertEquals(Fate.ADOPT, CityEntityRule.onLoad(false, Set.of(), 5, true));
        assertEquals(Fate.ADOPT, CityEntityRule.onLoad(false, Set.of("foo"), 5, true));
        assertEquals(Fate.DISCARD, CityEntityRule.onLoad(false, Set.of(), 5, false));
        assertEquals(Fate.DISCARD, CityEntityRule.onLoad(false, Set.of("foo"), 5, false));
    }
}
