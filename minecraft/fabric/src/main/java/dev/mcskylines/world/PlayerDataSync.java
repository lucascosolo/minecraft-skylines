package dev.mcskylines.world;

import java.util.Arrays;

/** Remembers the last player data sent so unchanged data is not sent again. Not thread-safe. */
public final class PlayerDataSync {
	private byte[] last;

	public boolean shouldSend(byte[] data) {
		return last == null || !Arrays.equals(last, data);
	}

	public void sent(byte[] data) {
		last = data.clone();
	}

	public void reset() {
		last = null;
	}
}
