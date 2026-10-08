package dev.mcskylines.protocol;

import static org.junit.jupiter.api.Assertions.*;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import dev.mcskylines.bridge.Frame;
import dev.mcskylines.bridge.FrameCodec;
import dev.mcskylines.bridge.ProtocolException;
import dev.mcskylines.collision.SkyTri;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HexFormat;
import java.util.List;
import org.junit.jupiter.api.Test;

class ShopVectorsTest {
    private static JsonObject find(String array, String name) throws Exception {
        String dir = System.getProperty("mcskylines.vectors");
        assertNotNull(dir, "system property mcskylines.vectors must be set");
        JsonObject root = JsonParser.parseString(Files.readString(Path.of(dir, "frames.json"))).getAsJsonObject();
        for (JsonElement e : root.getAsJsonArray(array)) {
            if (e.getAsJsonObject().get("name").getAsString().equals(name)) return e.getAsJsonObject();
        }
        throw new AssertionError("vector not found: " + name);
    }

    private static Frame frame(JsonObject v) throws Exception {
        return FrameCodec.decode(HexFormat.of().parseHex(v.get("hex").getAsString()));
    }

    @Test
    void constants() {
        assertEquals(20, AppProtocol.MINOR);
        assertEquals(0x0200, AppProtocol.SHOP_OPEN);
        assertEquals(0x0201, AppProtocol.SHOP_OFFERS);
        assertEquals(0x0202, AppProtocol.SHOP_TRADE);
        assertEquals(0, ShopOffers.OPEN);
        assertEquals(1, ShopOffers.NOT_A_SHOP);
        assertEquals(2, ShopOffers.CLOSED);
        assertEquals(32, ShopOffers.MAX_OFFERS);
        assertEquals(64, ShopOffers.MAX_ITEM_COUNT);
        assertEquals(1 << 10, SkyTri.TRADER);
    }

    @Test
    void shopOpenDecodesToFieldsAndReEncodes() throws Exception {
        JsonObject v = find("valid", "shop_open");
        JsonObject f = v.getAsJsonObject("fields");
        Frame fr = frame(v);
        assertEquals(AppProtocol.SHOP_OPEN, fr.type());
        ShopOpen got = ShopOpen.decode(fr.payload());
        assertEquals((int) f.get("openSeq").getAsLong(), got.openSeq());
        assertEquals((int) f.get("requestId").getAsLong(), got.requestId());
        var eye = f.getAsJsonArray("eye");
        var hit = f.getAsJsonArray("hit");
        assertEquals(eye.get(0).getAsFloat(), got.eyeX());
        assertEquals(eye.get(1).getAsFloat(), got.eyeY());
        assertEquals(eye.get(2).getAsFloat(), got.eyeZ());
        assertEquals(hit.get(0).getAsFloat(), got.hitX());
        assertEquals(hit.get(1).getAsFloat(), got.hitY());
        assertEquals(hit.get(2).getAsFloat(), got.hitZ());
        assertArrayEquals(fr.payload(), got.encode());
    }

    @Test
    void shopOffersDecodeToFieldsAndReEncode() throws Exception {
        for (String name : new String[] {"shop_offers_open", "shop_offers_closed", "shop_offers_not_a_shop"}) {
            JsonObject v = find("valid", name);
            JsonObject f = v.getAsJsonObject("fields");
            Frame fr = frame(v);
            assertEquals(AppProtocol.SHOP_OFFERS, fr.type(), name);
            ShopOffers got = ShopOffers.decode(fr.payload());
            assertEquals((int) f.get("openSeq").getAsLong(), got.openSeq(), name);
            assertEquals((int) f.get("requestId").getAsLong(), got.requestId(), name);
            assertEquals(f.get("building").getAsInt(), got.building(), name);
            assertEquals(f.get("status").getAsInt(), got.status(), name);
            assertEquals(f.get("name").getAsString(), got.name(), name);
            assertEquals(f.get("level").getAsInt(), got.level(), name);
            var list = f.getAsJsonArray("offers");
            assertEquals(list.size(), got.offers().size(), name);
            List<ShopOffers.Offer> built = new ArrayList<>();
            for (int i = 0; i < list.size(); i++) {
                JsonObject e = list.get(i).getAsJsonObject();
                ShopOffers.Offer o = got.offers().get(i);
                assertEquals(e.get("slot").getAsInt(), o.slot(), name);
                assertEquals(e.get("costItem").getAsString(), o.costItem(), name);
                assertEquals(e.get("costCount").getAsInt(), o.costCount(), name);
                assertEquals(e.get("resultItem").getAsString(), o.resultItem(), name);
                assertEquals(e.get("resultCount").getAsInt(), o.resultCount(), name);
                assertEquals(e.get("uses").getAsInt(), o.uses(), name);
                built.add(new ShopOffers.Offer(e.get("slot").getAsInt(), e.get("costItem").getAsString(),
                        e.get("costCount").getAsInt(), e.get("resultItem").getAsString(),
                        e.get("resultCount").getAsInt(), e.get("uses").getAsInt()));
            }
            assertArrayEquals(fr.payload(), got.encode(), name);
            ShopOffers fromFields = new ShopOffers((int) f.get("openSeq").getAsLong(),
                    (int) f.get("requestId").getAsLong(), f.get("building").getAsInt(),
                    f.get("status").getAsInt(), f.get("name").getAsString(), f.get("level").getAsInt(), built);
            assertArrayEquals(fr.payload(), fromFields.encode(), name);
        }
    }

    @Test
    void shopTradeDecodesToFieldsAndReEncodes() throws Exception {
        JsonObject v = find("valid", "shop_trade");
        JsonObject f = v.getAsJsonObject("fields");
        Frame fr = frame(v);
        assertEquals(AppProtocol.SHOP_TRADE, fr.type());
        ShopTrade got = ShopTrade.decode(fr.payload());
        assertEquals((int) f.get("openSeq").getAsLong(), got.openSeq());
        assertEquals(f.get("building").getAsInt(), got.building());
        assertEquals(f.get("slot").getAsInt(), got.slot());
        assertEquals(f.get("times").getAsInt(), got.times());
        assertArrayEquals(fr.payload(), got.encode());
        ShopTrade fromFields = new ShopTrade((int) f.get("openSeq").getAsLong(), f.get("building").getAsInt(),
                f.get("slot").getAsInt(), f.get("times").getAsInt());
        assertArrayEquals(fr.payload(), fromFields.encode());
    }

    @Test
    void invalidVectorsThrow() throws Exception {
        for (String name : new String[] {"shop_open_short", "shop_open_not_finite"}) {
            Frame fr = frame(find("invalid", name));
            assertEquals(AppProtocol.SHOP_OPEN, fr.type(), name);
            assertThrows(ProtocolException.class, () -> ShopOpen.decode(fr.payload()), name);
        }
        for (String name : new String[] {
            "shop_offers_bad_status", "shop_offers_too_many", "shop_offers_closed_with_offers",
            "shop_offers_zero_count", "shop_offers_count_65", "shop_offers_truncated",
            "shop_offers_trailing_bytes"}) {
            Frame fr = frame(find("invalid", name));
            assertEquals(AppProtocol.SHOP_OFFERS, fr.type(), name);
            assertThrows(ProtocolException.class, () -> ShopOffers.decode(fr.payload()), name);
        }
        for (String name : new String[] {"shop_trade_short", "shop_trade_zero_times", "shop_trade_building_zero"}) {
            Frame fr = frame(find("invalid", name));
            assertEquals(AppProtocol.SHOP_TRADE, fr.type(), name);
            assertThrows(ProtocolException.class, () -> ShopTrade.decode(fr.payload()), name);
        }
    }
}
