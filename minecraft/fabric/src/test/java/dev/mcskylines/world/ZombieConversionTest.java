package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.Test;

class ZombieConversionTest {
    @Test
    void allSixteenCombinations() {
        for (int diff = 0; diff <= 3; diff++) {
            for (boolean enabled : new boolean[] {false, true}) {
                for (boolean coin : new boolean[] {false, true}) {
                    boolean expected = enabled && (diff == 3 || (diff == 2 && coin));
                    assertEquals(expected, ZombieConversion.converts(diff, enabled, coin),
                        "difficulty " + diff + " enabled " + enabled + " coin " + coin);
                }
            }
        }
    }
}
