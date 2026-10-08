package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.Test;

class CitizenRulesTest {
    @Test
    void defaultIsOnOn() {
        assertTrue(CitizenRules.DEFAULT.citizens());
        assertTrue(CitizenRules.DEFAULT.conversion());
    }

    @Test
    void proxiesWantedOnlyWhenEverythingAllows() {
        assertTrue(CitizenRules.DEFAULT.proxiesWanted(true, 19));
        assertTrue(CitizenRules.DEFAULT.proxiesWanted(true, 20));
        assertFalse(new CitizenRules(false, true).proxiesWanted(true, 19));
        assertFalse(CitizenRules.DEFAULT.proxiesWanted(false, 19));
        assertFalse(CitizenRules.DEFAULT.proxiesWanted(true, 18));
    }
}
