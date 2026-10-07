package dev.mcskylines.protocol;

import static org.junit.jupiter.api.Assertions.*;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import dev.mcskylines.bridge.Frame;
import dev.mcskylines.bridge.FrameCodec;
import dev.mcskylines.bridge.ProtocolException;
import dev.mcskylines.world.CityTime;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Arrays;
import java.util.HexFormat;
import org.junit.jupiter.api.Test;

class TimeSetVectorsTest {
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
        assertEquals(0x0161, AppProtocol.TIME_SET);
        assertEquals(17, AppProtocol.MINOR);
    }

    @Test
    void validVectors() throws Exception {
        for (String name : new String[] {"time_set_noon_today", "time_set_add_days"}) {
            JsonObject v = find("valid", name);
            JsonObject f = v.getAsJsonObject("fields");
            Frame fr = frame(v);
            assertEquals(AppProtocol.TIME_SET, fr.type(), name);
            TimeSet got = TimeSet.decode(fr.payload());
            assertEquals(f.get("hour").getAsFloat(), got.hour(), name);
            assertEquals(f.get("days").getAsInt(), got.days(), name);
            assertArrayEquals(fr.payload(), got.encode(), name);
        }
    }

    @Test
    void cityTimeReproducesVectors() throws Exception {
        for (String name : new String[] {"time_set_noon_today", "time_set_add_days"}) {
            JsonObject f = find("valid", name).getAsJsonObject("fields");
            TimeSet got = CityTime.toCity(f.get("beforeTicks").getAsLong(), f.get("afterTicks").getAsLong());
            assertNotNull(got, name);
            assertEquals(f.get("hour").getAsFloat(), got.hour(), name);
            assertEquals(f.get("days").getAsInt(), got.days(), name);
        }
    }

    @Test
    void invalidVectorsThrow() throws Exception {
        for (String name : new String[] {"time_set_truncated", "time_set_hour_24"}) {
            Frame fr = frame(find("invalid", name));
            assertEquals(AppProtocol.TIME_SET, fr.type(), name);
            assertThrows(ProtocolException.class, () -> TimeSet.decode(fr.payload()), name);
        }
    }

    @Test
    void badHoursThrow() {
        for (float hour : new float[] {Float.NaN, -0.5f, 24f}) {
            byte[] p = new byte[6];
            int bits = Float.floatToRawIntBits(hour);
            for (int i = 0; i < 4; i++) p[i] = (byte) (bits >>> (8 * i));
            assertThrows(ProtocolException.class, () -> TimeSet.decode(p), "hour " + hour);
        }
    }

    @Test
    void everyTruncationThrows() throws Exception {
        byte[] p = frame(find("valid", "time_set_add_days")).payload();
        for (int len = 0; len < p.length; len++) {
            byte[] cut = Arrays.copyOf(p, len);
            assertThrows(ProtocolException.class, () -> TimeSet.decode(cut), "cut to " + len);
        }
    }
}
