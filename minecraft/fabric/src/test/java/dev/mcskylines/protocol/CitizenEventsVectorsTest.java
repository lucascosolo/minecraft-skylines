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
import java.util.HexFormat;
import org.junit.jupiter.api.Test;

class CitizenEventsVectorsTest {
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
        assertEquals(0x01D0, AppProtocol.CITIZEN_EVENTS);
        assertEquals(19, AppProtocol.MINOR);
        assertEquals(1, CitizenEvents.PANIC);
        assertEquals(2, CitizenEvents.KILLED);
        assertEquals(3, CitizenEvents.CONVERTED);
        assertEquals(1024, CitizenEvents.MAX_EVENTS);
    }

    @Test
    void validVectorsDecodeToFieldsAndReEncode() throws Exception {
        for (String name : new String[] {"citizen_events_empty", "citizen_events_three"}) {
            JsonObject v = find("valid", name);
            JsonObject f = v.getAsJsonObject("fields");
            Frame fr = frame(v);
            assertEquals(AppProtocol.CITIZEN_EVENTS, fr.type(), name);
            CitizenEvents got = CitizenEvents.decode(fr.payload());
            assertEquals((int) f.get("openSeq").getAsLong(), got.openSeq(), name);
            var list = f.getAsJsonArray("events");
            assertEquals(list.size(), got.events().size(), name);
            for (int i = 0; i < list.size(); i++) {
                JsonObject e = list.get(i).getAsJsonObject();
                CitizenEvents.Event o = got.events().get(i);
                assertEquals(e.get("kind").getAsInt(), o.kind(), name);
                assertEquals((int) e.get("id").getAsLong(), o.id(), name);
                assertEquals(e.get("x").getAsFloat(), o.x(), name);
                assertEquals(e.get("y").getAsFloat(), o.y(), name);
                assertEquals(e.get("z").getAsFloat(), o.z(), name);
            }
            assertArrayEquals(fr.payload(), got.encode(), name);
        }
    }

    @Test
    void invalidVectorsThrow() throws Exception {
        for (String name : new String[] {
            "citizen_events_too_many", "citizen_events_bad_kind",
            "citizen_events_truncated", "citizen_events_trailing_bytes"}) {
            Frame fr = frame(find("invalid", name));
            assertEquals(AppProtocol.CITIZEN_EVENTS, fr.type(), name);
            assertThrows(ProtocolException.class, () -> CitizenEvents.decode(fr.payload()), name);
        }
    }
}
