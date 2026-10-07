package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import dev.mcskylines.protocol.TimeSet;
import org.junit.jupiter.api.Test;

class CityTimeTest {
    @Test
    void unchangedTimeIsNull() {
        assertNull(CityTime.toCity(6000, 6000));
        assertNull(CityTime.toCity(-5, -5));
    }

    @Test
    void noonMapsToCityHour12() {
        // Minecraft tick 6000 is noon; the city clock is Minecraft + 6 h.
        TimeSet t = CityTime.toCity(2000, 6000);
        assertEquals(12.0f, t.hour());
        assertEquals(0, t.days());
    }

    @Test
    void addOneDayKeepsHourAndAddsOneDay() {
        TimeSet t = CityTime.toCity(1000, 25000);
        assertEquals(7.0f, t.hour());
        assertEquals(1, t.days());
    }

    @Test
    void partialDaysAreFloored() {
        assertEquals(1, CityTime.toCity(0, 47999).days());
        assertEquals(2, CityTime.toCity(0, 48000).days());
    }

    @Test
    void backwardsHasZeroDays() {
        TimeSet t = CityTime.toCity(100000, 6000);
        assertEquals(0, t.days());
        assertEquals(12.0f, t.hour());
    }

    @Test
    void hourWrapsAtMinecraftTick18000() {
        assertEquals(0.0f, CityTime.toCity(0, 18000).hour());
        float h = CityTime.toCity(0, 17999).hour();
        assertEquals((float) (((17999 / 1000.0) + 6) % 24), h);
        assertTrue(h > 23.99f && h < 24.0f);
    }

    @Test
    void daysCapAt65535() {
        assertEquals(65535, CityTime.toCity(0, 24000L * 70000L).days());
        assertEquals(65535, CityTime.toCity(0, 24000L * 65535L).days());
    }

    @Test
    void negativeAfterUsesFloorMod() {
        assertEquals((float) (((23999 / 1000.0) + 6) % 24), CityTime.toCity(0, -1).hour());
    }

    @Test
    void rebaseCountsFromStartOfShownDay() {
        assertEquals(24000L * 3 + 6000, CityTime.rebase(24000L * 3 + 13000, 6000));
        assertEquals(48000, CityTime.rebase(48000, 0));
        assertEquals(24000L * 3 + 23999, CityTime.rebase(24000L * 3 + 1, 23999));
    }

    @Test
    void rebaseNegativeCurrentUsesFloorDiv() {
        assertEquals(-24000L + 6000, CityTime.rebase(-1, 6000));
        assertEquals(-48000L + 1000, CityTime.rebase(-24001, 1000));
    }
}
