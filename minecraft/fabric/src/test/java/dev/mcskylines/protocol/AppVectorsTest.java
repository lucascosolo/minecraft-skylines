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
    void constants() {
        assertEquals("minecraft-skylines", AppProtocol.NAME);
        assertEquals(1, AppProtocol.MAJOR);
        assertEquals(0, AppProtocol.MINOR);
        assertEquals(0x0100, AppProtocol.HOST_STATUS);
        assertEquals(0x0101, AppProtocol.GUEST_STATUS);
    }
}
