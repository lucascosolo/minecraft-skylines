package dev.mcskylines.bridge;

import java.util.Locale;

public enum DisconnectCause {
	PEER_GOODBYE, LOCAL_GOODBYE, TIMEOUT, PROTOCOL_ERROR, CONNECTION_LOST, REJECTED, BACKPRESSURE;

	public String wireName() {
		return name().toLowerCase(Locale.ROOT);
	}
}
