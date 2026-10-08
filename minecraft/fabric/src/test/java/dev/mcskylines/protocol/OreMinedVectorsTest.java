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

class OreMinedVectorsTest {
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
        assertEquals(0x0211, AppProtocol.ORE_MINED);
        assertEquals(1, OreMined.ORE);
        assertEquals(2, OreMined.OIL);
        assertEquals(256, OreMined.MAX_ENTRIES);
    }

    @Test
    void validVectorsDecodeToFieldsAndReEncode() throws Exception {
        for (String name : new String[] {"ore_mined_empty", "ore_mined_two"}) {
            JsonObject v = find("valid", name);
            JsonObject f = v.getAsJsonObject("fields");
            Frame fr = frame(v);
            assertEquals(AppProtocol.ORE_MINED, fr.type(), name);
            OreMined got = OreMined.decode(fr.payload());
            assertEquals((int) f.get("openSeq").getAsLong(), got.openSeq(), name);
            var list = f.getAsJsonArray("entries");
            assertEquals(list.size(), got.entries().size(), name);
            for (int i = 0; i < list.size(); i++) {
                JsonObject e = list.get(i).getAsJsonObject();
                OreMined.Entry o = got.entries().get(i);
                assertEquals(e.get("resource").getAsInt(), o.resource(), name);
                assertEquals(e.get("cx").getAsInt(), o.cx(), name);
                assertEquals(e.get("cz").getAsInt(), o.cz(), name);
                assertEquals(e.get("blocks").getAsInt(), o.blocks(), name);
            }
            assertArrayEquals(fr.payload(), got.encode(), name);
        }
    }

    @Test
    void invalidVectorsThrow() throws Exception {
        List<String> names = invalidNames("ore_mined_");
        assertFalse(names.isEmpty());
        for (String name : names) {
            Frame fr = frame(find("invalid", name));
            assertEquals(AppProtocol.ORE_MINED, fr.type(), name);
            assertThrows(ProtocolException.class, () -> OreMined.decode(fr.payload()), name);
        }
    }
}
