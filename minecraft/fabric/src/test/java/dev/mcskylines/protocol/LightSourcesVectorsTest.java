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
import java.util.Arrays;
import java.util.HexFormat;
import java.util.List;
import org.junit.jupiter.api.Test;

class LightSourcesVectorsTest {
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

    private static LightSources expected(JsonObject f) {
        List<LightSources.Light> lights = new ArrayList<>();
        for (JsonElement e : f.getAsJsonArray("lights")) {
            JsonObject o = e.getAsJsonObject();
            lights.add(new LightSources.Light(o.get("x").getAsInt(), o.get("y").getAsInt(), o.get("z").getAsInt(),
                    o.get("level").getAsInt()));
        }
        return new LightSources(lights);
    }

    @Test
    void maxLevel() {
        assertEquals(15, LightSources.MAX_LEVEL);
    }

    @Test
    void lightSourcesVector() throws Exception {
        JsonObject v = find("valid", "light_sources");
        Frame fr = frame(v);
        assertEquals(AppProtocol.LIGHT_SOURCES, fr.type());
        LightSources got = LightSources.decode(fr.payload());
        assertEquals(expected(v.getAsJsonObject("fields")), got);
        assertEquals(3, got.lights().size());
        assertEquals(new LightSources.Light(8639, 1100, -8640, 9), got.lights().get(2));
        assertArrayEquals(fr.payload(), got.encode());
    }

    @Test
    void lightSourcesEmptyVector() throws Exception {
        JsonObject v = find("valid", "light_sources_empty");
        Frame fr = frame(v);
        assertEquals(AppProtocol.LIGHT_SOURCES, fr.type());
        LightSources got = LightSources.decode(fr.payload());
        assertTrue(got.lights().isEmpty());
        assertArrayEquals(fr.payload(), got.encode());
    }

    @Test
    void truncationByOneByteThrows() throws Exception {
        for (String name : new String[] {"light_sources", "light_sources_empty"}) {
            byte[] p = frame(find("valid", name)).payload();
            assertThrows(ProtocolException.class, () -> LightSources.decode(Arrays.copyOf(p, p.length - 1)), name);
        }
    }

    @Test
    void levelZeroVectorIsRejected() throws Exception {
        Frame fr = frame(find("invalid", "light_sources_level_zero"));
        assertEquals(AppProtocol.LIGHT_SOURCES, fr.type());
        assertThrows(ProtocolException.class, () -> LightSources.decode(fr.payload()));
    }

    @Test
    void levelSixteenIsRejected() {
        byte[] p = new LightSources(List.of(new LightSources.Light(0, 0, 0, 16))).encode();
        assertThrows(ProtocolException.class, () -> LightSources.decode(p));
    }
}
