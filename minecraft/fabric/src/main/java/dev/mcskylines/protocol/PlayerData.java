package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x01B0 PLAYER_DATA, minor 11: a player's saved data blob, u32 openSeq, u32 length, raw bytes. */
public record PlayerData(int openSeq, byte[] data) {
	public static final int MAX_LENGTH = 4194304;

	public byte[] encode() {
		if (data.length > MAX_LENGTH) {
			throw new IllegalArgumentException("player data " + data.length + " above " + MAX_LENGTH);
		}
		PayloadWriter w = new PayloadWriter().u32(Integer.toUnsignedLong(openSeq)).u32(data.length);
		for (byte b : data) {
			w.u8(b & 0xFF);
		}
		return w.toByteArray();
	}

	public static PlayerData decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int seq = (int) r.u32();
		long length = r.u32();
		if (length > MAX_LENGTH) {
			throw new ProtocolException("player data length " + length + " above " + MAX_LENGTH);
		}
		byte[] data = new byte[(int) length];
		for (int i = 0; i < data.length; i++) {
			data[i] = (byte) r.u8();
		}
		return new PlayerData(seq, data);
	}
}
