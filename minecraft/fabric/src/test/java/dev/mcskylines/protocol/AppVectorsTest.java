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
import java.util.UUID;
import org.junit.jupiter.api.Test;

class AppVectorsTest {
    private static JsonObject vector(String name) throws Exception {
        String dir = System.getProperty("mcskylines.vectors");
        assertNotNull(dir, "system property mcskylines.vectors must be set");
        JsonObject root = JsonParser.parseString(Files.readString(Path.of(dir, "frames.json"))).getAsJsonObject();
        JsonArray valid = root.getAsJsonArray("valid");
        for (JsonElement e : valid) {
            if (e.getAsJsonObject().get("name").getAsString().equals(name)) return e.getAsJsonObject();
        }
        throw new AssertionError("vector not found: " + name);
    }

    private static byte[] payload(JsonObject v) throws Exception {
        Frame f = FrameCodec.decode(HexFormat.of().parseHex(v.get("hex").getAsString()));
        return f.payload();
    }

    @Test
    void hostStatus() throws Exception {
        JsonObject v = vector("host_status");
        JsonObject f = v.getAsJsonObject("fields");
        byte[] p = payload(v);
        HostStatus expected = new HostStatus(f.get("flags").getAsInt(), f.get("cityName").getAsString(),
                UUID.fromString(f.get("saveId").getAsString()), f.get("gameVersion").getAsString());
        HostStatus got = HostStatus.decode(p);
        assertEquals(expected, got);
        assertArrayEquals(p, got.encode());
        assertTrue(got.has(HostStatus.IN_CITY));
    }

    @Test
    void hostStatusTruncated() throws Exception {
        byte[] p = payload(vector("host_status"));
        byte[] cut = Arrays.copyOf(p, p.length - 1);
        assertThrows(ProtocolException.class, () -> HostStatus.decode(cut));
    }

    @Test
    void guestStatusUnpaired() throws Exception {
        JsonObject v = vector("guest_status_unpaired");
        JsonObject f = v.getAsJsonObject("fields");
        byte[] p = payload(v);
        GuestStatus expected = new GuestStatus(f.get("flags").getAsInt(), f.get("worldName").getAsString(),
                UUID.fromString(f.get("pairedSaveId").getAsString()));
        GuestStatus got = GuestStatus.decode(p);
        assertEquals(expected, got);
        assertArrayEquals(p, got.encode());
        assertTrue(got.has(GuestStatus.IN_WORLD));
        assertTrue(got.has(GuestStatus.SCREEN_OPEN));
    }

    @Test
    void guestStatusTruncated() throws Exception {
        byte[] p = payload(vector("guest_status_unpaired"));
        byte[] cut = Arrays.copyOf(p, p.length - 1);
        assertThrows(ProtocolException.class, () -> GuestStatus.decode(cut));
    }

    @Test
    void enterPlayerMode() throws Exception {
        JsonObject v = vector("enter_player_mode");
        JsonObject f = v.getAsJsonObject("fields");
        byte[] p = payload(v);
        EnterPlayerMode expected = new EnterPlayerMode(f.get("teleportSeq").getAsInt(), f.get("x").getAsDouble(),
                f.get("y").getAsDouble(), f.get("z").getAsDouble(), f.get("yaw").getAsFloat(),
                f.get("pitch").getAsFloat(), f.get("collisionEpoch").getAsInt());
        EnterPlayerMode got = EnterPlayerMode.decode(p);
        assertEquals(expected, got);
        assertArrayEquals(p, got.encode());
    }

    @Test
    void exitPlayerMode() throws Exception {
        JsonObject v = vector("exit_player_mode");
        byte[] p = payload(v);
        ExitPlayerMode got = ExitPlayerMode.decode(p);
        assertEquals(new ExitPlayerMode(v.getAsJsonObject("fields").get("reason").getAsString()), got);
        assertArrayEquals(p, got.encode());
    }

    @Test
    void input() throws Exception {
        for (String name : new String[] {"input", "input_empty"}) {
            JsonObject v = vector(name);
            JsonObject f = v.getAsJsonObject("fields");
            byte[] p = payload(v);
            JsonArray events = f.getAsJsonArray("events");
            Input.Event[] evs = new Input.Event[events.size()];
            for (int i = 0; i < evs.length; i++) {
                JsonObject e = events.get(i).getAsJsonObject();
                evs[i] = new Input.Event(e.get("kind").getAsInt(), e.get("action").getAsInt(), e.get("code").getAsInt());
            }
            Input got = Input.decode(p);
            assertEquals(new Input(f.get("yaw").getAsFloat(), f.get("pitch").getAsFloat(), evs), got, name);
            assertArrayEquals(p, got.encode(), name);
        }
    }

    @Test
    void collisionRegion() throws Exception {
        for (String name : new String[] {"collision_region", "collision_region_empty"}) {
            JsonObject v = vector(name);
            JsonObject f = v.getAsJsonObject("fields");
            byte[] p = payload(v);
            JsonArray tris = f.getAsJsonArray("tris");
            float[] verts = new float[9 * tris.size()];
            short[] flags = new short[tris.size()];
            for (int t = 0; t < tris.size(); t++) {
                JsonObject tri = tris.get(t).getAsJsonObject();
                JsonArray vv = tri.getAsJsonArray("v");
                assertEquals(9, vv.size());
                for (int i = 0; i < 9; i++) verts[9 * t + i] = vv.get(i).getAsFloat();
                flags[t] = (short) tri.get("flags").getAsInt();
            }
            CollisionRegion got = CollisionRegion.decode(p);
            assertEquals(new CollisionRegion(f.get("epoch").getAsInt(), f.get("regionX").getAsInt(),
                    f.get("regionZ").getAsInt(), verts, flags), got, name);
            assertEquals(tris.size(), got.triangleCount());
            assertArrayEquals(p, got.encode(), name);
        }
    }

    @Test
    void collisionReset() throws Exception {
        JsonObject v = vector("collision_reset");
        byte[] p = payload(v);
        CollisionReset got = CollisionReset.decode(p);
        assertEquals(new CollisionReset(v.getAsJsonObject("fields").get("epoch").getAsInt()), got);
        assertArrayEquals(p, got.encode());
    }

    @Test
    void playerState() throws Exception {
        JsonObject v = vector("player_state");
        JsonObject f = v.getAsJsonObject("fields");
        byte[] p = payload(v);
        PlayerState expected = new PlayerState(f.get("flags").getAsInt(), f.get("teleportAck").getAsInt(),
                f.get("x").getAsDouble(), f.get("y").getAsDouble(), f.get("z").getAsDouble(),
                f.get("eyeX").getAsDouble(), f.get("eyeY").getAsDouble(), f.get("eyeZ").getAsDouble(),
                f.get("yaw").getAsFloat(), f.get("pitch").getAsFloat(), f.get("fovDeg").getAsFloat(),
                f.get("tickSeq").getAsInt(), f.get("prevX").getAsDouble(), f.get("prevY").getAsDouble(),
                f.get("prevZ").getAsDouble(), f.get("curX").getAsDouble(), f.get("curY").getAsDouble(),
                f.get("curZ").getAsDouble(), f.get("prevEyeHeight").getAsFloat(), f.get("curEyeHeight").getAsFloat(),
                f.get("partialTick").getAsFloat(), f.get("tickMs").getAsFloat());
        PlayerState got = PlayerState.decode(p);
        assertEquals(expected, got);
        assertArrayEquals(p, got.encode());
        assertTrue(got.has(PlayerState.IN_WORLD));
        assertTrue(got.has(PlayerState.HELD));
    }

    @Test
    void truncatedPlayerMessages() {
        assertThrows(ProtocolException.class, () -> EnterPlayerMode.decode(new byte[10]));
        assertThrows(ProtocolException.class, () -> Input.decode(new byte[] {0, 0, 0, 0, 0, 0, 1, 0, 1, 1}));
        assertThrows(ProtocolException.class, () -> PlayerState.decode(new byte[20]));
        assertThrows(ProtocolException.class, () -> CollisionRegion.decode(
                new byte[] {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, -1, -1, -1}));
    }

    @Test
    void constants() {
        assertEquals("minecraft-skylines", AppProtocol.NAME);
        assertEquals(1, AppProtocol.MAJOR);
        assertEquals(1, AppProtocol.MINOR);
        assertEquals(0x0100, AppProtocol.HOST_STATUS);
        assertEquals(0x0101, AppProtocol.GUEST_STATUS);
    }
}
