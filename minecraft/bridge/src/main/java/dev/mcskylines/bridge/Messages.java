package dev.mcskylines.bridge;

public final class Messages {
	private Messages() {
	}

	/** Decodes a bridge frame's body; application frames are returned unchanged. */
	public static Object decodeBody(Frame f) throws ProtocolException {
		return switch (f.type()) {
			case Hello.TYPE -> Hello.decode(f.payload());
			case Welcome.TYPE -> Welcome.decode(f.payload());
			case Heartbeat.TYPE -> Heartbeat.decode(f.payload());
			case Goodbye.TYPE -> Goodbye.decode(f.payload());
			default -> {
				if (f.type() < FrameCodec.APP_MIN) {
					throw new ProtocolException("unknown bridge frame type 0x" + Integer.toHexString(f.type()));
				}
				yield f;
			}
		};
	}
}
