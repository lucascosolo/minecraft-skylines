package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.Test;

class PlayerDataSyncTest {
    @Test
    void sendsWhenNothingSentYet() {
        PlayerDataSync s = new PlayerDataSync();
        assertTrue(s.shouldSend(new byte[] {1}));
        assertTrue(s.shouldSend(new byte[0]));
    }

    @Test
    void doesNotResendEqualContent() {
        PlayerDataSync s = new PlayerDataSync();
        s.sent(new byte[] {1, 2});
        assertFalse(s.shouldSend(new byte[] {1, 2}));
        assertTrue(s.shouldSend(new byte[] {1, 3}));
        assertTrue(s.shouldSend(new byte[] {1}));
    }

    @Test
    void sentCopiesTheArray() {
        PlayerDataSync s = new PlayerDataSync();
        byte[] a = {1, 2};
        s.sent(a);
        a[0] = 9;
        assertTrue(s.shouldSend(new byte[] {9, 2}));
        assertFalse(s.shouldSend(new byte[] {1, 2}));
    }

    @Test
    void resetForgetsLastSent() {
        PlayerDataSync s = new PlayerDataSync();
        s.sent(new byte[] {1});
        s.reset();
        assertTrue(s.shouldSend(new byte[] {1}));
    }

    @Test
    void emptyDataIsComparedLikeAnyOther() {
        PlayerDataSync s = new PlayerDataSync();
        s.sent(new byte[0]);
        assertFalse(s.shouldSend(new byte[0]));
        assertTrue(s.shouldSend(new byte[] {0}));
    }
}
