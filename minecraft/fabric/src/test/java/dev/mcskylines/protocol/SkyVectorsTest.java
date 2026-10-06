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

class SkyVectorsTest {
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

    private static float[] floats(JsonObject f, String key) {
        JsonArray a = f.getAsJsonArray(key);
        float[] r = new float[a.size()];
        for (int i = 0; i < r.length; i++) r[i] = a.get(i).getAsFloat();
        return r;
    }

    private static void assertState(JsonObject f, SkyState s) {
        assertEquals(f.get("flags").getAsInt(), s.flags());
        assertArrayEquals(floats(f, "skyColor"), s.skyColor(), 0f);
        assertArrayEquals(floats(f, "fogColor"), s.fogColor(), 0f);
        assertArrayEquals(floats(f, "sunriseColor"), s.sunriseColor(), 0f);
        assertEquals(f.get("starBrightness").getAsFloat(), s.starBrightness(), 0f);
        assertEquals(f.get("rainLevel").getAsFloat(), s.rainLevel(), 0f);
        assertEquals(f.get("moonPhase").getAsInt(), s.moonPhase());
        assertArrayEquals(floats(f, "cloudColor"), s.cloudColor(), 0f);
        assertEquals(f.get("cloudHeight").getAsFloat(), s.cloudHeight(), 0f);
        assertEquals(f.get("cloudOffset").getAsFloat(), s.cloudOffset(), 0f);
        assertEquals(f.get("cloudSpeed").getAsFloat(), s.cloudSpeed(), 0f);
    }

    @Test
    void constants() {
        assertEquals(1, SkyState.FLAG_SKY);
        assertEquals(2, SkyState.FLAG_CLOUDS);
        assertEquals(8, SkyState.MOON_PHASES);
        assertEquals(0, SkyTextures.SUN);
        assertEquals(1, SkyTextures.MOON);
        assertEquals(2, SkyTextures.CLOUDS);
        assertEquals(1, SkyTextures.PNG);
    }

    @Test
    void skyStateVectorsDecodeAndReencode() throws Exception {
        for (String name : new String[] {"sky_state_day", "sky_state_night"}) {
            JsonObject v = find("valid", name);
            Frame fr = frame(v);
            assertEquals(AppProtocol.SKY_STATE, fr.type(), name);
            SkyState got = SkyState.decode(fr.payload());
            assertState(v.getAsJsonObject("fields"), got);
            assertEquals(78, fr.payload().length, name);
            assertArrayEquals(fr.payload(), got.encode(), name);
        }
    }

    @Test
    void skyTexturesVectorsDecodeAndReencode() throws Exception {
        for (String name : new String[] {"sky_textures", "sky_textures_empty"}) {
            JsonObject v = find("valid", name);
            Frame fr = frame(v);
            assertEquals(AppProtocol.SKY_TEXTURES, fr.type(), name);
            SkyTextures got = SkyTextures.decode(fr.payload());
            JsonArray exp = v.getAsJsonObject("fields").getAsJsonArray("textures");
            assertEquals(exp.size(), got.textures().size(), name);
            for (int i = 0; i < exp.size(); i++) {
                JsonObject e = exp.get(i).getAsJsonObject();
                SkyTextures.Texture t = got.textures().get(i);
                assertEquals(e.get("kind").getAsInt(), t.kind());
                assertEquals(e.get("phase").getAsInt(), t.phase());
                assertEquals(e.get("format").getAsInt(), t.format());
                assertArrayEquals(HexFormat.of().parseHex(e.get("dataHex").getAsString()), t.data());
            }
            assertArrayEquals(fr.payload(), got.encode(), name);
        }
    }

    @Test
    void unknownKindAndFormatDecode() throws Exception {
        SkyTextures got = SkyTextures.decode(frame(find("valid", "sky_textures")).payload());
        assertEquals(4, got.textures().size());
        assertEquals(9, got.textures().get(3).kind());
        assertEquals(2, got.textures().get(3).format());
    }

    @Test
    void invalidVectorsThrow() throws Exception {
        Frame s = frame(find("invalid", "sky_state_moon_phase_8"));
        assertEquals(AppProtocol.SKY_STATE, s.type());
        assertThrows(ProtocolException.class, () -> SkyState.decode(s.payload()));
        Frame t = frame(find("invalid", "sky_textures_moon_phase_8"));
        assertEquals(AppProtocol.SKY_TEXTURES, t.type());
        assertThrows(ProtocolException.class, () -> SkyTextures.decode(t.payload()));
    }

    @Test
    void everyTruncationThrows() throws Exception {
        for (String name : new String[] {"sky_state_day", "sky_state_night"}) {
            byte[] p = frame(find("valid", name)).payload();
            for (int n = 0; n < p.length; n++) {
                byte[] cut = Arrays.copyOf(p, n);
                assertThrows(ProtocolException.class, () -> SkyState.decode(cut), name + " len " + n);
            }
        }
        for (String name : new String[] {"sky_textures", "sky_textures_empty"}) {
            byte[] p = frame(find("valid", name)).payload();
            for (int n = 0; n < p.length; n++) {
                byte[] cut = Arrays.copyOf(p, n);
                assertThrows(ProtocolException.class, () -> SkyTextures.decode(cut), name + " len " + n);
            }
        }
    }

    @Test
    void trailingBytesAreIgnored() throws Exception {
        byte[] p = frame(find("valid", "sky_state_day")).payload();
        SkyState got = SkyState.decode(Arrays.copyOf(p, p.length + 3));
        assertArrayEquals(p, got.encode());
    }
}
