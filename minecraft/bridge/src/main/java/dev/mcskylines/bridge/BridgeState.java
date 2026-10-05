package dev.mcskylines.bridge;

import java.util.Locale;

/** Link states from bridge-v1.md; {@code LISTENING} is host-only and never reported by a guest. */
public enum BridgeState {
	DISCONNECTED, LISTENING, CONNECTING, HANDSHAKING, CONNECTED, REJECTED, CLOSING;

	public String wireName() {
		return name().toLowerCase(Locale.ROOT);
	}
}
