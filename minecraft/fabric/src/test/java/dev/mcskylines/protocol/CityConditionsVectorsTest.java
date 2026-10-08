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
import java.util.ArrayList;
import java.util.HexFormat;
import java.util.List;
import org.junit.jupiter.api.Test;

class CityConditionsVectorsTest {
    private static JsonObject root() throws Exception {
        String dir = System.getProperty("mcskylines.vectors");
        assertNotNull(dir, "system property mcskylines.vectors must be set");
        return JsonParser.parseString(Files.readString(Path.of(dir, "frames.json"))).getAsJsonObject();
    }

    private static JsonObject find(String array, String name) throws Exception {
        for (JsonElement e : root().getAsJsonArray(array)) {
            if (e.getAsJsonObject().get("name").getAsString().equals(name)) return e.getAsJsonObject();
        }
        throw new AssertionError("vector not found: " + name);
    }

    private static List<String> invalidNames(String prefix) throws Exception {
        List<String> out = new ArrayList<>();
        for (JsonElement e : root().getAsJsonArray("invalid")) {
            String n = e.getAsJsonObject().get("name").getAsString();
            if (n.startsWith(prefix)) out.add(n);
        }
        return out;
    }

    private static Frame frame(JsonObject v) throws Exception {
        return FrameCodec.decode(HexFormat.of().parseHex(v.get("hex").getAsString()));
    }

    @Test
    void constants() {
        assertEquals(21, AppProtocol.MINOR);
        assertEquals(0x0210, AppProtocol.CITY_CONDITIONS);
        assertEquals(256, CityConditions.MAX_CELLS);
        assertEquals(256, CityConditions.MAX_FIRES);
        assertEquals(1, CityConditions.WORKED);
    }

    @Test
    void validVectorsDecodeToFieldsAndReEncode() throws Exception {
        for (String name : new String[] {"city_conditions_empty", "city_conditions_full"}) {
            JsonObject v = find("valid", name);
            JsonObject f = v.getAsJsonObject("fields");
            Frame fr = frame(v);
            assertEquals(AppProtocol.CITY_CONDITIONS, fr.type(), name);
            CityConditions got = CityConditions.decode(fr.payload());
            assertEquals((int) f.get("openSeq").getAsLong(), got.openSeq(), name);
            var cells = f.getAsJsonArray("cells");
            assertEquals(cells.size(), got.cells().size(), name);
            for (int i = 0; i < cells.size(); i++) {
                JsonObject c = cells.get(i).getAsJsonObject();
                CityConditions.Cell o = got.cells().get(i);
                assertEquals(c.get("cx").getAsInt(), o.cx(), name);
                assertEquals(c.get("cz").getAsInt(), o.cz(), name);
                assertEquals(c.get("ore").getAsInt(), o.ore(), name);
                assertEquals(c.get("oil").getAsInt(), o.oil(), name);
                assertEquals(c.get("fertility").getAsInt(), o.fertility(), name);
                assertEquals(c.get("forest").getAsInt(), o.forest(), name);
                assertEquals(c.get("pollution").getAsInt(), o.pollution(), name);
                assertEquals(c.get("flags").getAsInt(), o.flags(), name);
                assertEquals(c.get("crime").getAsInt(), o.crime(), name);
                assertEquals(c.get("dead").getAsInt(), o.dead(), name);
                assertEquals((c.get("flags").getAsInt() & 1) != 0, o.worked(), name);
            }
            var fires = f.getAsJsonArray("fires");
            assertEquals(fires.size(), got.fires().size(), name);
            for (int i = 0; i < fires.size(); i++) {
                JsonObject e = fires.get(i).getAsJsonObject();
                CityConditions.Fire o = got.fires().get(i);
                assertEquals(e.get("x").getAsFloat(), o.x(), name);
                assertEquals(e.get("y").getAsFloat(), o.y(), name);
                assertEquals(e.get("z").getAsFloat(), o.z(), name);
                assertEquals(e.get("radius").getAsFloat(), o.radius(), name);
                assertEquals(e.get("intensity").getAsInt(), o.intensity(), name);
            }
            assertArrayEquals(fr.payload(), got.encode(), name);
        }
    }

    @Test
    void invalidVectorsThrow() throws Exception {
        List<String> names = invalidNames("city_conditions_");
        assertFalse(names.isEmpty());
        for (String name : names) {
            Frame fr = frame(find("invalid", name));
            assertEquals(AppProtocol.CITY_CONDITIONS, fr.type(), name);
            assertThrows(ProtocolException.class, () -> CityConditions.decode(fr.payload()), name);
        }
    }
}
