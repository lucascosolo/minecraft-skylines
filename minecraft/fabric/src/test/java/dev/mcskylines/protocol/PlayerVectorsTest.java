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
import java.util.Arrays;
import java.util.HexFormat;
import org.junit.jupiter.api.Test;

class PlayerVectorsTest {
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
        assertEquals(13, AppProtocol.MINOR);
        assertEquals(0x01B0, AppProtocol.PLAYER_DATA);
        assertEquals(0x01B1, AppProtocol.RESPAWN_REQUEST);
        assertEquals(4194304, PlayerData.MAX_LENGTH);
    }

    @Test
    void playerDataVectors() throws Exception {
        for (String name : new String[] {"player_data", "player_data_fresh"}) {
            JsonObject v = find("valid", name);
            JsonObject f = v.getAsJsonObject("fields");
            Frame fr = frame(v);
            assertEquals(AppProtocol.PLAYER_DATA, fr.type(), name);
            PlayerData got = PlayerData.decode(fr.payload());
            assertEquals((int) f.get("openSeq").getAsLong(), got.openSeq(), name);
            assertArrayEquals(HexFormat.of().parseHex(f.get("dataHex").getAsString()), got.data(), name);
            assertArrayEquals(fr.payload(), got.encode(), name);
        }
    }

    @Test
    void respawnRequestVector() throws Exception {
        JsonObject v = find("valid", "respawn_request");
        Frame fr = frame(v);
        assertEquals(AppProtocol.RESPAWN_REQUEST, fr.type());
        RespawnRequest got = RespawnRequest.decode(fr.payload());
        assertEquals(v.getAsJsonObject("fields").get("openSeq").getAsInt(), got.openSeq());
        assertArrayEquals(fr.payload(), got.encode());
    }

    @Test
    void invalidVectorsThrow() throws Exception {
        for (String name : new String[] {"player_data_too_long", "player_data_overruns"}) {
            Frame fr = frame(find("invalid", name));
            assertEquals(AppProtocol.PLAYER_DATA, fr.type(), name);
            assertThrows(ProtocolException.class, () -> PlayerData.decode(fr.payload()), name);
        }
    }

    @Test
    void everyTruncationThrows() throws Exception {
        for (String name : new String[] {"player_data", "player_data_fresh"}) {
            byte[] p = frame(find("valid", name)).payload();
            for (int len = 0; len < p.length; len++) {
                byte[] cut = Arrays.copyOf(p, len);
                assertThrows(ProtocolException.class, () -> PlayerData.decode(cut), name + " cut to " + len);
            }
        }
        byte[] r = frame(find("valid", "respawn_request")).payload();
        for (int len = 0; len < r.length; len++) {
            byte[] cut = Arrays.copyOf(r, len);
            assertThrows(ProtocolException.class, () -> RespawnRequest.decode(cut), "respawn cut to " + len);
        }
    }

    @Test
    void trailingBytesAreIgnored() throws Exception {
        byte[] enc = new PlayerData(9, new byte[] {1, 2, 3}).encode();
        PlayerData got = PlayerData.decode(Arrays.copyOf(enc, enc.length + 5));
        assertEquals(9, got.openSeq());
        assertArrayEquals(new byte[] {1, 2, 3}, got.data());
    }

    @Test
    void encodeRejectsOversizeData() throws Exception {
        assertEquals(PlayerData.MAX_LENGTH,
                PlayerData.decode(new PlayerData(1, new byte[PlayerData.MAX_LENGTH]).encode()).data().length);
        assertThrows(IllegalArgumentException.class,
                () -> new PlayerData(1, new byte[PlayerData.MAX_LENGTH + 1]).encode());
    }
}
