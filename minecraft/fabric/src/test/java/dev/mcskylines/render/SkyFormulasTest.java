package dev.mcskylines.render;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.Test;

class SkyFormulasTest {
    @Test
    void cloudOffsetAtZero() {
        assertEquals(0f, SkyFormulas.cloudOffset(0L, 0f, 256), 1e-3f);
    }

    @Test
    void cloudOffsetScalesTimeAndPartialTick() {
        assertEquals(3.015f, SkyFormulas.cloudOffset(100L, 0.5f, 256), 1e-3f);
    }

    @Test
    void cloudOffsetWrapsAtWidthTimes400() {
        long period = 256L * 400L;
        assertEquals(0f, SkyFormulas.cloudOffset(period, 0f, 256), 1e-3f);
        assertEquals(0.3f, SkyFormulas.cloudOffset(period + 10, 0f, 256), 1e-3f);
        assertEquals(SkyFormulas.cloudOffset(77L, 0.25f, 256),
                SkyFormulas.cloudOffset(77L + 5 * period, 0.25f, 256), 1e-3f);
        assertEquals(SkyFormulas.cloudOffset(5L, 0f, 128),
                SkyFormulas.cloudOffset(5L + 128L * 400L, 0f, 128), 1e-3f);
    }

    @Test
    void cloudOffsetNegativeGameTimeUsesFloorMod() {
        // floorMod(-1, 102400) = 102399
        assertEquals(102399 * 0.03f, SkyFormulas.cloudOffset(-1L, 0f, 256), 1e-3f);
        assertEquals(SkyFormulas.cloudOffset(100L, 0f, 256),
                SkyFormulas.cloudOffset(100L - 256L * 400L, 0f, 256), 1e-3f);
    }

    @Test
    void moonTextureAllPhases() {
        String[] names = {"full_moon", "waning_gibbous", "third_quarter", "waning_crescent", "new_moon",
                "waxing_crescent", "first_quarter", "waxing_gibbous"};
        for (int i = 0; i < 8; i++) {
            assertEquals("textures/environment/celestial/moon/" + names[i] + ".png", SkyFormulas.moonTexture(i));
        }
    }

    @Test
    void moonTextureOutOfRangeThrows() {
        assertThrows(IllegalArgumentException.class, () -> SkyFormulas.moonTexture(-1));
        assertThrows(IllegalArgumentException.class, () -> SkyFormulas.moonTexture(8));
    }

    @Test
    void constants() {
        assertEquals("textures/environment/celestial/sun.png", SkyFormulas.SUN_TEXTURE);
        assertEquals("textures/environment/clouds.png", SkyFormulas.CLOUDS_TEXTURE);
        assertEquals(2 * 1024 * 1024, SkyFormulas.MAX_TEXTURE_BYTES);
    }
}
