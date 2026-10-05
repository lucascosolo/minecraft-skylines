package dev.mcskylines;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import dev.mcskylines.protocol.HostStatus;
import java.util.UUID;
import org.junit.jupiter.api.Test;

class PrewarmTest {
	private static HostStatus status(int flags) {
		return new HostStatus(flags, "City", UUID.randomUUID(), "1.0");
	}

	@Test
	void opensWorldWhenFirstStatusIsInCity() {
		assertTrue(LinkController.shouldOpenWorld(null, status(HostStatus.IN_CITY), 1));
	}

	@Test
	void opensWorldWhenEnteringCity() {
		assertTrue(LinkController.shouldOpenWorld(status(0), status(HostStatus.IN_CITY), 1));
	}

	@Test
	void doesNotReopenWhileStillInCity() {
		assertFalse(LinkController.shouldOpenWorld(status(HostStatus.IN_CITY), status(HostStatus.IN_CITY), 1));
	}

	@Test
	void doesNotOpenWhenLeavingCity() {
		assertFalse(LinkController.shouldOpenWorld(status(HostStatus.IN_CITY), status(0), 1));
	}

	@Test
	void doesNotOpenWhenAppMinorIsZero() {
		assertFalse(LinkController.shouldOpenWorld(null, status(HostStatus.IN_CITY), 0));
	}
}
