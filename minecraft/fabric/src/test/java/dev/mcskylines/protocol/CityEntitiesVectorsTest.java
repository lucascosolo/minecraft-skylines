package dev.mcskylines.protocol;

import static org.junit.jupiter.api.Assertions.*;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import dev.mcskylines.bridge.Frame;
import dev.mcskylines.bridge.FrameCodec;
import dev.mcskylines.bridge.ProtocolException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Arrays;
import java.util.HexFormat;
import org.junit.jupiter.api.Test;

class CityEntitiesVectorsTest {
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
        assertEquals(21, AppProtocol.MINOR);
        assertEquals(0x01D0, AppProtocol.CITY_ENTITIES);
        assertEquals(0x01D1, AppProtocol.CITY_FOCUS);
        assertEquals(4194304, CityEntities.MAX_LENGTH);
        assertEquals(1, CityFocus.ACTIVE);
    }

    @Test
    void cityEntitiesVectors() throws Exception {
        for (String name : new String[] {"city_entities", "city_entities_empty"}) {
            JsonObject v = find("valid", name);
            JsonObject f = v.getAsJsonObject("fields");
            Frame fr = frame(v);
            assertEquals(AppProtocol.CITY_ENTITIES, fr.type(), name);
            assertEquals(v.get("type").getAsInt(), fr.type(), name);
            CityEntities got = CityEntities.decode(fr.payload());
            assertEquals((int) f.get("openSeq").getAsLong(), got.openSeq(), name);
            assertArrayEquals(HexFormat.of().parseHex(f.get("dataHex").getAsString()), got.data(), name);
            assertArrayEquals(fr.payload(), got.encode(), name);
        }
    }

    @Test
    void cityFocusVectors() throws Exception {
        for (String name : new String[] {"city_focus_active", "city_focus_off"}) {
            JsonObject v = find("valid", name);
            JsonObject f = v.getAsJsonObject("fields");
            Frame fr = frame(v);
            assertEquals(AppProtocol.CITY_FOCUS, fr.type(), name);
            assertEquals(v.get("type").getAsInt(), fr.type(), name);
            CityFocus got = CityFocus.decode(fr.payload());
            assertEquals(f.get("x").getAsFloat(), got.x(), name);
            assertEquals(f.get("z").getAsFloat(), got.z(), name);
            assertEquals(f.get("flags").getAsInt(), got.flags(), name);
            assertEquals((f.get("flags").getAsInt() & 1) != 0, got.active(), name);
            assertEquals(9, got.encode().length, name);
            assertArrayEquals(fr.payload(), got.encode(), name);
        }
    }

    @Test
    void invalidVectorsThrow() throws Exception {
        for (String name : new String[] {"city_entities_too_long", "city_entities_overruns"}) {
            Frame fr = frame(find("invalid", name));
            assertEquals(AppProtocol.CITY_ENTITIES, fr.type(), name);
            assertThrows(ProtocolException.class, () -> CityEntities.decode(fr.payload()), name);
        }
        for (String name : new String[] {"city_focus_short", "city_focus_long", "city_focus_active_nan"}) {
            Frame fr = frame(find("invalid", name));
            assertEquals(AppProtocol.CITY_FOCUS, fr.type(), name);
            assertThrows(ProtocolException.class, () -> CityFocus.decode(fr.payload()), name);
        }
    }

    @Test
    void everyTruncationThrows() throws Exception {
        for (String name : new String[] {"city_entities", "city_entities_empty"}) {
            byte[] p = frame(find("valid", name)).payload();
            for (int len = 0; len < p.length; len++) {
                byte[] cut = Arrays.copyOf(p, len);
                assertThrows(ProtocolException.class, () -> CityEntities.decode(cut), name + " cut to " + len);
            }
        }
    }

    @Test
    void inactiveFocusAllowsNonFinite() throws Exception {
        CityFocus got = CityFocus.decode(new CityFocus(Float.NaN, Float.POSITIVE_INFINITY, 0).encode());
        assertFalse(got.active());
        assertEquals(0, got.flags());
    }

    @Test
    void encodeRejectsOversizeData() throws Exception {
        assertEquals(CityEntities.MAX_LENGTH,
                CityEntities.decode(new CityEntities(1, new byte[CityEntities.MAX_LENGTH]).encode()).data().length);
        assertThrows(IllegalArgumentException.class,
                () -> new CityEntities(1, new byte[CityEntities.MAX_LENGTH + 1]).encode());
    }
}
