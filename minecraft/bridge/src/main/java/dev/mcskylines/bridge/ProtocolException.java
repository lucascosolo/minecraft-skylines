package dev.mcskylines.bridge;

import java.io.IOException;

/** Something received violates protocol/bridge-v1.md; the session ends with GOODBYE(PROTOCOL_ERROR). */
public final class ProtocolException extends IOException {
	public ProtocolException(String message) {
		super(message);
	}
}
