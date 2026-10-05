package dev.mcskylines.bridge;

/** What a bridge reports to its application, drained on the application's own thread. */
public sealed interface BridgeEvent {
	record StateChanged(BridgeState state, String detail) implements BridgeEvent {
	}

	record Message(int type, byte[] payload) implements BridgeEvent {
	}

	/** {@code code} is the GOODBYE or reject code involved, or -1 when there was none. */
	record Disconnected(DisconnectCause cause, int code, String reason) implements BridgeEvent {
	}
}
