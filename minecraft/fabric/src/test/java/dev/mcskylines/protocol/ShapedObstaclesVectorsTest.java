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

class ShapedObstaclesVectorsTest {
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
        assertEquals(0x0171, AppProtocol.SHAPED_OBSTACLES);
    }

    @Test
    void twelveArgumentObstacleHasNoYawRateAndNoProfile() {
        DynamicObstacles.Obstacle o = new DynamicObstacles.Obstacle(1, 2, 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f);
        assertEquals(0f, o.yawRate());
        assertEquals(0, o.profile().length);
    }

    @Test
    void shapedObstacles() throws Exception {
        JsonObject v = find("valid", "shaped_obstacles");
        JsonArray want = v.getAsJsonObject("fields").getAsJsonArray("obstacles");
        Frame fr = frame(v);
        assertEquals(AppProtocol.SHAPED_OBSTACLES, fr.type());
        byte[] p = fr.payload();
        DynamicObstacles got = DynamicObstacles.decodeShaped(p);
        assertEquals(want.size(), got.obstacles().size());
        assertTrue(want.size() > 0);
        for (int i = 0; i < want.size(); i++) {
            JsonObject w = want.get(i).getAsJsonObject();
            DynamicObstacles.Obstacle o = got.obstacles().get(i);
            assertEquals(w.get("kind").getAsInt(), o.kind());
            assertEquals(w.get("id").getAsLong(), Integer.toUnsignedLong(o.id()));
            assertEquals(w.get("x").getAsFloat(), o.x());
            assertEquals(w.get("y").getAsFloat(), o.y());
            assertEquals(w.get("z").getAsFloat(), o.z());
            assertEquals(w.get("yaw").getAsFloat(), o.yaw());
            assertEquals(w.get("halfWidth").getAsFloat(), o.halfWidth());
            assertEquals(w.get("halfHeight").getAsFloat(), o.halfHeight());
            assertEquals(w.get("halfLength").getAsFloat(), o.halfLength());
            assertEquals(w.get("vx").getAsFloat(), o.vx());
            assertEquals(w.get("vy").getAsFloat(), o.vy());
            assertEquals(w.get("vz").getAsFloat(), o.vz());
            assertEquals(w.get("yawRate").getAsFloat(), o.yawRate());
            JsonArray prof = w.getAsJsonArray("profile");
            assertEquals(prof.size(), o.profile().length);
            for (int k = 0; k < prof.size(); k++) {
                assertEquals(prof.get(k).getAsInt(), o.profile()[k] & 0xFF, "profile[" + k + "]");
            }
        }
        assertArrayEquals(p, got.encodeShaped());
        assertThrows(ProtocolException.class, () -> DynamicObstacles.decodeShaped(Arrays.copyOf(p, p.length - 1)));
    }

    @Test
    void shapedObstaclesEmpty() throws Exception {
        byte[] p = frame(find("valid", "shaped_obstacles_empty")).payload();
        DynamicObstacles got = DynamicObstacles.decodeShaped(p);
        assertTrue(got.obstacles().isEmpty());
        assertArrayEquals(p, got.encodeShaped());
    }

    @Test
    void shapedObstaclesEveryTruncationThrows() throws Exception {
        byte[] p = frame(find("valid", "shaped_obstacles")).payload();
        for (int len = 0; len < p.length; len++) {
            byte[] cut = Arrays.copyOf(p, len);
            assertThrows(ProtocolException.class, () -> DynamicObstacles.decodeShaped(cut), "cut to " + len);
        }
    }

    @Test
    void truncatedInvalidVectorThrows() throws Exception {
        Frame fr = frame(find("invalid", "shaped_obstacles_truncated"));
        assertEquals(AppProtocol.SHAPED_OBSTACLES, fr.type());
        assertThrows(ProtocolException.class, () -> DynamicObstacles.decodeShaped(fr.payload()));
    }
}
