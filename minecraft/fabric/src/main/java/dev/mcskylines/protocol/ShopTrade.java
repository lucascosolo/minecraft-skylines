package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

/** 0x0202 SHOP_TRADE (guest to host), minor 20: {@code times} trades of offer {@code slot} at {@code building}. */
public record ShopTrade(int openSeq, int building, int slot, int times) {
	public byte[] encode() {
		return new PayloadWriter().u32(Integer.toUnsignedLong(openSeq)).u16(building).u8(slot).u16(times).toByteArray();
	}

	public static ShopTrade decode(byte[] payload) throws ProtocolException {
		if (payload.length != 9) throw new ProtocolException("shop trade payload is " + payload.length + " bytes, not 9");
		PayloadReader r = new PayloadReader(payload);
		ShopTrade m = new ShopTrade((int) r.u32(), r.u16(), r.u8(), r.u16());
		if (m.building == 0 || m.times == 0) throw new ProtocolException("shop trade with building 0 or zero times");
		return m;
	}
}
