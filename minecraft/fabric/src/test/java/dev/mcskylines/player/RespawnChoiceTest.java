package dev.mcskylines.player;

import static dev.mcskylines.player.RespawnChoice.Choice.*;
import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;

class RespawnChoiceTest {
    @Test
    void allEightCombinations() {
        // decide(alive, cityWorld, hasOwnSpawn)
        assertEquals(NONE, RespawnChoice.decide(true, true, true));
        assertEquals(NONE, RespawnChoice.decide(true, true, false));
        assertEquals(NONE, RespawnChoice.decide(true, false, true));
        assertEquals(NONE, RespawnChoice.decide(true, false, false));
        assertEquals(NONE, RespawnChoice.decide(false, false, true));
        assertEquals(NONE, RespawnChoice.decide(false, false, false));
        assertEquals(OWN_SPAWN, RespawnChoice.decide(false, true, true));
        assertEquals(ASK_HOST, RespawnChoice.decide(false, true, false));
    }
}
