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
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HexFormat;
import java.util.List;
import java.util.UUID;
import org.junit.jupiter.api.Test;

class CityVectorsTest {
    private static JsonObject find(String array, String name) throws Exception {
        String dir = System.getProperty("mcskylines.vectors");
        assertNotNull(dir, "system property mcskylines.vectors must be set");
        JsonObject root = JsonParser.parseString(Files.readString(Path.of(dir, "frames.json"))).getAsJsonObject();
        for (JsonElement e : root.getAsJsonArray(array)) {
            if (e.getAsJsonObject().get("name").getAsString().equals(name)) return e.getAsJsonObject();
        }
        throw new AssertionError("vector not found: " + name);
    }

    private static JsonObject vector(String name) throws Exception {
        return find("valid", name);
    }

    private static byte[] payload(JsonObject v) throws Exception {
        Frame f = FrameCodec.decode(HexFormat.of().parseHex(v.get("hex").getAsString()));
        return f.payload();
    }

    private static BlockEdits blockEdits(JsonObject f) {
        List<String> palette = new ArrayList<>();
        for (JsonElement e : f.getAsJsonArray("palette")) palette.add(e.getAsString());
        List<BlockEdits.Edit> edits = new ArrayList<>();
        for (JsonElement e : f.getAsJsonArray("edits")) {
            JsonObject o = e.getAsJsonObject();
            edits.add(new BlockEdits.Edit(o.get("x").getAsInt(), o.get("y").getAsInt(), o.get("z").getAsInt(),
                    o.get("state").getAsInt()));
        }
        return new BlockEdits(f.get("openSeq").getAsInt(), f.get("flags").getAsInt(), palette, edits);
    }

    @Test
    void constants() {
        assertEquals(21, AppProtocol.MINOR);
        assertEquals(0x0160, AppProtocol.WORLD_TIME);
        assertEquals(0x0170, AppProtocol.DYNAMIC_OBSTACLES);
        assertEquals(0x0150, AppProtocol.CITY_OPEN);
        assertEquals(0x0151, AppProtocol.BLOCK_EDITS);
        assertEquals(0x0152, AppProtocol.CITY_CLOSE);
        assertEquals(0x0153, AppProtocol.EDIT_SYNC);
        assertEquals(0x0154, AppProtocol.EDIT_SYNC_ACK);
        assertEquals(0x0155, AppProtocol.CITY_STATE);
        assertEquals(1, BlockEdits.LAST);
        assertEquals(65536, BlockEdits.MAX_EDITS);
        assertEquals(0, CityState.APPLYING);
        assertEquals(1, CityState.READY);
        assertEquals(2, CityState.CLOSED);
    }

    @Test
    void cityOpen() throws Exception {
        JsonObject v = vector("city_open");
        JsonObject f = v.getAsJsonObject("fields");
        byte[] p = payload(v);
        CityOpen expected = new CityOpen(f.get("openSeq").getAsInt(), UUID.fromString(f.get("saveId").getAsString()),
                f.get("cityName").getAsString(), f.get("editCount").getAsInt());
        CityOpen got = CityOpen.decode(p);
        assertEquals(expected, got);
        assertArrayEquals(p, got.encode());
    }

    @Test
    void cityOpenTruncated() throws Exception {
        byte[] p = payload(vector("city_open"));
        assertThrows(ProtocolException.class, () -> CityOpen.decode(Arrays.copyOf(p, p.length - 1)));
    }

    @Test
    void blockEditsVectors() throws Exception {
        for (String name : new String[] {"block_edits_snapshot_last", "block_edits_empty_last",
                "block_edits_guest_air"}) {
            JsonObject v = vector(name);
            byte[] p = payload(v);
            BlockEdits expected = blockEdits(v.getAsJsonObject("fields"));
            BlockEdits got = BlockEdits.decode(p);
            assertEquals(expected, got, name);
            assertArrayEquals(p, got.encode(), name);
        }
    }

    @Test
    void blockEditsLastFlag() throws Exception {
        assertTrue(BlockEdits.decode(payload(vector("block_edits_snapshot_last"))).last());
        assertTrue(BlockEdits.decode(payload(vector("block_edits_empty_last"))).last());
        assertFalse(BlockEdits.decode(payload(vector("block_edits_guest_air"))).last());
    }

    @Test
    void blockEditsTruncated() throws Exception {
        for (String name : new String[] {"block_edits_snapshot_last", "block_edits_empty_last",
                "block_edits_guest_air"}) {
            byte[] p = payload(vector(name));
            assertThrows(ProtocolException.class, () -> BlockEdits.decode(Arrays.copyOf(p, p.length - 1)), name);
        }
    }

    @Test
    void blockEditsInvalidVectors() throws Exception {
        for (String name : new String[] {"block_edits_index_out_of_range", "block_edits_duplicate_palette",
                "block_edits_too_many"}) {
            JsonObject v = find("invalid", name);
            Frame frame = FrameCodec.decode(HexFormat.of().parseHex(v.get("hex").getAsString()));
            assertEquals(AppProtocol.BLOCK_EDITS, frame.type(), name);
            assertThrows(ProtocolException.class, () -> BlockEdits.decode(frame.payload()), name);
        }
    }

    @Test
    void blockEditsEncodeRejectsTooMany() {
        List<BlockEdits.Edit> edits = new ArrayList<>();
        for (int i = 0; i <= BlockEdits.MAX_EDITS; i++) edits.add(new BlockEdits.Edit(i, 0, 0, 0));
        BlockEdits b = new BlockEdits(1, 0, List.of("minecraft:stone"), edits);
        assertThrows(IllegalStateException.class, b::encode);
    }

    @Test
    void blockEditsEncodeAcceptsMax() {
        List<BlockEdits.Edit> edits = new ArrayList<>();
        for (int i = 0; i < BlockEdits.MAX_EDITS; i++) edits.add(new BlockEdits.Edit(i, 0, 0, 0));
        BlockEdits b = new BlockEdits(1, 0, List.of("minecraft:stone"), edits);
        assertDoesNotThrow(() -> BlockEdits.decode(b.encode()));
    }

    @Test
    void blockEditsEncodeRejectsDuplicatePalette() {
        BlockEdits b = new BlockEdits(1, 0, List.of("minecraft:stone", "minecraft:stone"), List.of());
        assertThrows(IllegalStateException.class, b::encode);
    }

    @Test
    void blockEditsEncodeRejectsIndexOutOfRange() {
        BlockEdits b = new BlockEdits(1, 0, List.of("minecraft:stone"), List.of(new BlockEdits.Edit(0, 0, 0, 1)));
        assertThrows(IllegalStateException.class, b::encode);
    }

    @Test
    void cityClose() throws Exception {
        JsonObject v = vector("city_close");
        byte[] p = payload(v);
        CityClose got = CityClose.decode(p);
        assertEquals(new CityClose(v.getAsJsonObject("fields").get("openSeq").getAsInt()), got);
        assertArrayEquals(p, got.encode());
        assertThrows(ProtocolException.class, () -> CityClose.decode(Arrays.copyOf(p, p.length - 1)));
    }

    @Test
    void editSyncAndAck() throws Exception {
        for (String name : new String[] {"edit_sync", "edit_sync_ack"}) {
            JsonObject v = vector(name);
            JsonObject f = v.getAsJsonObject("fields");
            byte[] p = payload(v);
            EditSync got = EditSync.decode(p);
            assertEquals(new EditSync(f.get("openSeq").getAsInt(), (int) f.get("token").getAsLong()), got, name);
            assertEquals(-1, got.token(), name);
            assertArrayEquals(p, got.encode(), name);
            assertThrows(ProtocolException.class, () -> EditSync.decode(Arrays.copyOf(p, p.length - 1)), name);
        }
    }

    @Test
    void cityStateReady() throws Exception {
        JsonObject v = vector("city_state_ready");
        JsonObject f = v.getAsJsonObject("fields");
        byte[] p = payload(v);
        CityState got = CityState.decode(p);
        assertEquals(new CityState(f.get("openSeq").getAsInt(), f.get("state").getAsInt(),
                f.get("appliedCount").getAsInt()), got);
        assertEquals(CityState.READY, got.state());
        assertArrayEquals(p, got.encode());
        assertThrows(ProtocolException.class, () -> CityState.decode(Arrays.copyOf(p, p.length - 1)));
    }

    @Test
    void worldTime() throws Exception {
        for (String name : new String[] {"world_time_evening", "world_time_no_cycle"}) {
            JsonObject v = vector(name);
            JsonObject f = v.getAsJsonObject("fields");
            byte[] p = payload(v);
            WorldTime got = WorldTime.decode(p);
            assertEquals(f.get("hour").getAsFloat(), got.hour());
            assertEquals(f.get("day").getAsLong(), Integer.toUnsignedLong(got.day()));
            assertEquals(f.get("flags").getAsInt(), got.flags());
            assertEquals(f.get("minecraftDayTicks").getAsInt(), WorldTime.minecraftDayTicks(got.hour()));
            assertArrayEquals(p, got.encode());
            assertThrows(ProtocolException.class, () -> WorldTime.decode(Arrays.copyOf(p, p.length - 1)));
        }
        assertEquals(6000, WorldTime.minecraftDayTicks(12f));
        assertEquals(0, WorldTime.minecraftDayTicks(6f));
        assertEquals(18000, WorldTime.minecraftDayTicks(0f));
        assertEquals(2L * 24000 + 6000, new WorldTime(20f, 2, 0).totalTicks()); // no day/night cycle: midday
        assertEquals(2L * 24000 + 14000, new WorldTime(20f, 2, WorldTime.DAY_NIGHT).totalTicks());
    }

    @Test
    void dynamicObstacles() throws Exception {
        JsonObject v = vector("dynamic_obstacles");
        JsonArray want = v.getAsJsonObject("fields").getAsJsonArray("obstacles");
        byte[] p = payload(v);
        DynamicObstacles got = DynamicObstacles.decode(p);
        assertEquals(want.size(), got.obstacles().size());
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
        }
        assertEquals(DynamicObstacles.VEHICLE, got.obstacles().get(0).kind());
        assertEquals(DynamicObstacles.CITIZEN, got.obstacles().get(1).kind());
        assertArrayEquals(p, got.encode());
        assertThrows(ProtocolException.class, () -> DynamicObstacles.decode(Arrays.copyOf(p, p.length - 1)));
    }

    @Test
    void dynamicObstaclesEmpty() throws Exception {
        byte[] p = payload(vector("dynamic_obstacles_empty"));
        DynamicObstacles got = DynamicObstacles.decode(p);
        assertTrue(got.obstacles().isEmpty());
        assertArrayEquals(p, got.encode());
        assertThrows(ProtocolException.class, () -> DynamicObstacles.decode(Arrays.copyOf(p, p.length - 1)));
    }
}
