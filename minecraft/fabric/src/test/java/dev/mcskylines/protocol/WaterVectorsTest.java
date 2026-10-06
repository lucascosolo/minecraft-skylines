package dev.mcskylines.protocol;

import static org.junit.jupiter.api.Assertions.*;

import com.google.gson.JsonArray;
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

class WaterVectorsTest {
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

    private static float[] floats(JsonArray a) {
        float[] out = new float[a.size()];
        for (int i = 0; i < out.length; i++) out[i] = a.get(i).getAsFloat();
        return out;
    }

    @Test
    void constants() {
        assertEquals(0x01A0, AppProtocol.WATER_SURFACE);
        assertEquals(128, WaterSurface.MAX_SIZE);
    }

    @Test
    void waterSurfaceVector() throws Exception {
        JsonObject v = find("valid", "water_surface");
        JsonObject f = v.getAsJsonObject("fields");
        Frame fr = frame(v);
        assertEquals(AppProtocol.WATER_SURFACE, fr.type());
        WaterSurface got = WaterSurface.decode(fr.payload());
        assertEquals(f.get("originX").getAsInt(), got.originX());
        assertEquals(f.get("originZ").getAsInt(), got.originZ());
        assertEquals(f.get("size").getAsInt(), got.size());
        assertArrayEquals(floats(f.getAsJsonArray("surface")), got.surface());
        assertArrayEquals(floats(f.getAsJsonArray("bottom")), got.bottom());
        assertArrayEquals(fr.payload(), got.encode());
    }

    @Test
    void waterSurfaceEmptyVector() throws Exception {
        Frame fr = frame(find("valid", "water_surface_empty"));
        assertEquals(AppProtocol.WATER_SURFACE, fr.type());
        WaterSurface got = WaterSurface.decode(fr.payload());
        assertEquals(0, got.size());
        assertEquals(0, got.surface().length);
        assertEquals(0, got.bottom().length);
        assertArrayEquals(fr.payload(), got.encode());
    }

    @Test
    void everyTruncationThrows() throws Exception {
        for (String name : new String[] {"water_surface", "water_surface_empty"}) {
            byte[] p = frame(find("valid", name)).payload();
            for (int len = 0; len < p.length; len++) {
                byte[] cut = Arrays.copyOf(p, len);
                assertThrows(ProtocolException.class, () -> WaterSurface.decode(cut), name + " cut to " + len);
            }
        }
    }

    @Test
    void size129IsRejectedBeforeReadingColumns() throws Exception {
        Frame fr = frame(find("invalid", "water_surface_size_129"));
        assertEquals(AppProtocol.WATER_SURFACE, fr.type());
        assertThrows(ProtocolException.class, () -> WaterSurface.decode(fr.payload()));
    }
}
