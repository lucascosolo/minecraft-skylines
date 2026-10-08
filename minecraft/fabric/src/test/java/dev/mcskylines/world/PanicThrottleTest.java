package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.Test;

class PanicThrottleTest {
    @Test
    void firstCallIsAllowed() {
        assertTrue(new PanicThrottle(20).allow(1, 100));
    }

    @Test
    void repeatWithinPeriodIsRefusedAndDoesNotRecord() {
        PanicThrottle t = new PanicThrottle(20);
        assertTrue(t.allow(1, 100));
        assertFalse(t.allow(1, 101));
        assertFalse(t.allow(1, 119));
        assertTrue(t.allow(1, 120));
    }

    @Test
    void allowedAgainAtExactlyPeriod() {
        PanicThrottle t = new PanicThrottle(20);
        assertTrue(t.allow(1, 0));
        assertTrue(t.allow(1, 20));
        assertFalse(t.allow(1, 39));
        assertTrue(t.allow(1, 40));
    }

    @Test
    void idsAreIndependent() {
        PanicThrottle t = new PanicThrottle(20);
        assertTrue(t.allow(1, 100));
        assertTrue(t.allow(2, 101));
        assertFalse(t.allow(1, 102));
    }

    @Test
    void forgetResets() {
        PanicThrottle t = new PanicThrottle(20);
        assertTrue(t.allow(1, 100));
        t.forget(1);
        assertTrue(t.allow(1, 101));
    }
}
