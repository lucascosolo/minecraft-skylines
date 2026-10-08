package dev.mcskylines.protocol;

import dev.mcskylines.bridge.PayloadReader;
import dev.mcskylines.bridge.PayloadWriter;
import dev.mcskylines.bridge.ProtocolException;

import java.util.ArrayList;
import java.util.List;

/** 0x0201 SHOP_OFFERS (host to guest), minor 20: what the building at {@code building} trades. */
public record ShopOffers(int openSeq, int requestId, int building, int status, String name, int level, List<Offer> offers) {
	public static final int OPEN = 0;
	public static final int NOT_A_SHOP = 1;
	public static final int CLOSED = 2;
	public static final int MAX_OFFERS = 32;
	public static final int MAX_ITEM_COUNT = 64;

	public record Offer(int slot, String costItem, int costCount, String resultItem, int resultCount, int uses) {
	}

	public byte[] encode() {
		PayloadWriter w = new PayloadWriter().u32(Integer.toUnsignedLong(openSeq)).u32(Integer.toUnsignedLong(requestId))
			.u16(building).u8(status).string(name).u8(level).u16(offers.size());
		for (Offer o : offers) {
			w.u8(o.slot()).string(o.costItem()).u8(o.costCount()).string(o.resultItem()).u8(o.resultCount()).u16(o.uses());
		}
		return w.toByteArray();
	}

	public static ShopOffers decode(byte[] payload) throws ProtocolException {
		PayloadReader r = new PayloadReader(payload);
		int openSeq = (int) r.u32();
		int requestId = (int) r.u32();
		int building = r.u16();
		int status = r.u8();
		String name = r.string();
		int level = r.u8();
		int n = r.u16();
		if (status > CLOSED) throw new ProtocolException("shop status " + status + " is above " + CLOSED);
		if (n > MAX_OFFERS) throw new ProtocolException("shop offer count " + n + " is above " + MAX_OFFERS);
		if (n > 0 && status != OPEN) throw new ProtocolException("offers on a shop that is not open");
		List<Offer> offers = new ArrayList<>(n);
		for (int i = 0; i < n; i++) {
			Offer o = new Offer(r.u8(), r.string(), r.u8(), r.string(), r.u8(), r.u16());
			if (o.costCount() < 1 || o.costCount() > MAX_ITEM_COUNT || o.resultCount() < 1 || o.resultCount() > MAX_ITEM_COUNT) {
				throw new ProtocolException("shop offer item count outside 1.." + MAX_ITEM_COUNT);
			}
			offers.add(o);
		}
		ShopOffers m = new ShopOffers(openSeq, requestId, building, status, name, level, List.copyOf(offers));
		if (m.encode().length != payload.length) throw new ProtocolException("shop offers payload longer than its count says");
		return m;
	}
}
