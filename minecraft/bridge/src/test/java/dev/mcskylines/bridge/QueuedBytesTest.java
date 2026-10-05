package dev.mcskylines.bridge;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;

class QueuedBytesTest {
    @Test
    void noSessionMeansZeroQueuedBytes() {
        BridgeGuest guest = new BridgeGuest(BridgeGuest.Config.of("x", 1, 0, "n", "v"));
        assertEquals(0L, guest.queuedBytes());
    }
}
