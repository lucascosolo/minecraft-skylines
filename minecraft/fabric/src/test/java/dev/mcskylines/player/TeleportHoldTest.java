package dev.mcskylines.player;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertTrue;

import dev.mcskylines.player.TeleportHold.Target;
import org.junit.jupiter.api.Test;

class TeleportHoldTest {
	private static Target t(int seq) {
		return new Target(seq, 1, 2, 3, 4f, 5f);
	}

	@Test
	void freshStateIsIdle() {
		TeleportHold h = new TeleportHold();
		assertFalse(h.held());
		assertNull(h.holdTarget());
		assertNull(h.takePending(0));
		assertEquals(0, h.ack());
		assertFalse(h.update(100, true));
		assertEquals(0, h.ack());
		assertFalse(h.held());
	}

	@Test
	void requestHolds() {
		TeleportHold h = new TeleportHold();
		Target a = t(1);
		h.request(a);
		assertTrue(h.held());
		assertSame(a, h.holdTarget());
	}

	@Test
	void takePendingReturnsOnceAndKeepsHold() {
		TeleportHold h = new TeleportHold();
		Target a = t(1);
		h.request(a);
		assertSame(a, h.takePending(10));
		assertNull(h.takePending(11));
		assertSame(a, h.holdTarget());
		assertTrue(h.held());
	}

	@Test
	void neverReleasesBeforeTaken() {
		TeleportHold h = new TeleportHold();
		h.request(t(1));
		assertTrue(h.update(1_000_000, true));
		assertTrue(h.held());
		assertEquals(0, h.ack());
	}

	@Test
	void releasesWhenCollisionReady() {
		TeleportHold h = new TeleportHold();
		h.request(t(7));
		h.takePending(0);
		assertTrue(h.update(1, false));
		assertFalse(h.update(2, true));
		assertFalse(h.held());
		assertNull(h.holdTarget());
		assertEquals(7, h.ack());
	}

	@Test
	void timesOutAtExactlySixSeconds() {
		TeleportHold h = new TeleportHold();
		h.request(t(9));
		h.takePending(1000);
		assertTrue(h.update(1000 + TeleportHold.TIMEOUT_MS - 1, false));
		assertEquals(0, h.ack());
		assertFalse(h.update(1000 + TeleportHold.TIMEOUT_MS, false));
		assertEquals(9, h.ack());
		assertNull(h.holdTarget());
	}

	@Test
	void ackSurvivesLaterRequestUntilReleased() {
		TeleportHold h = new TeleportHold();
		h.request(t(1));
		h.takePending(0);
		h.update(1, true);
		h.request(t(2));
		assertEquals(1, h.ack());
		h.takePending(2);
		h.update(3, true);
		assertEquals(2, h.ack());
	}

	@Test
	void newRequestReplacesPendingAndNeverAcksOldOne() {
		TeleportHold h = new TeleportHold();
		h.request(t(1));
		Target b = t(2);
		h.request(b);
		assertSame(b, h.holdTarget());
		assertSame(b, h.takePending(0));
		h.update(1, true);
		assertEquals(2, h.ack());
	}

	@Test
	void newRequestReplacesActiveHold() {
		TeleportHold h = new TeleportHold();
		h.request(t(1));
		h.takePending(0);
		Target b = t(2);
		h.request(b);
		assertSame(b, h.holdTarget());
		assertTrue(h.update(5, true));
		assertEquals(0, h.ack());
		assertSame(b, h.takePending(6));
		assertFalse(h.update(7, true));
		assertEquals(2, h.ack());
	}

	@Test
	void timeoutClockRestartsPerRequest() {
		TeleportHold h = new TeleportHold();
		h.request(t(1));
		h.takePending(0);
		h.request(t(2));
		h.takePending(5000);
		assertTrue(h.update(10_999, false));
		assertFalse(h.update(11_000, false));
		assertEquals(2, h.ack());
	}

	@Test
	void seqRoundTripsAsAnyInt() {
		TeleportHold h = new TeleportHold();
		h.request(t(-1));
		h.takePending(0);
		h.update(1, true);
		assertEquals(-1, h.ack());
	}

	@Test
	void cancelDropsPendingWithoutAck() {
		TeleportHold h = new TeleportHold();
		h.request(t(1));
		h.cancel();
		assertFalse(h.held());
		assertNull(h.holdTarget());
		assertNull(h.takePending(0));
		assertEquals(0, h.ack());
	}

	@Test
	void cancelDropsActiveHoldKeepingEarlierAck() {
		TeleportHold h = new TeleportHold();
		h.request(t(1));
		h.takePending(0);
		h.update(1, true);
		h.request(t(2));
		h.takePending(2);
		h.cancel();
		assertFalse(h.held());
		assertNull(h.holdTarget());
		assertFalse(h.update(100_000, true));
		assertEquals(1, h.ack());
	}
}
