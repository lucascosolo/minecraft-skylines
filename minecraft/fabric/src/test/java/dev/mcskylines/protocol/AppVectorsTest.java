package dev.mcskylines.protocol;

import static org.junit.jupiter.api.Assertions.*;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import dev.mcskylines.bridge.Frame;
import dev.mcskylines.bridge.FrameCodec;
import dev.mcskylines.bridge.ProtocolException;
import dev.mcskylines.render.MeshVertices;
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
    void viewport() throws Exception {
        JsonObject v = vector("viewport");
        JsonObject f = v.getAsJsonObject("fields");
        byte[] p = payload(v);
        Viewport got = Viewport.decode(p);
        assertEquals(new Viewport(f.get("width").getAsInt(), f.get("height").getAsInt(),
                f.get("uiScale").getAsFloat()), got);
        assertArrayEquals(p, got.encode());
    }

    @Test
    void viewportTruncated() throws Exception {
        byte[] p = payload(vector("viewport"));
        assertThrows(ProtocolException.class, () -> Viewport.decode(Arrays.copyOf(p, p.length - 1)));
    }

    @Test
    void overlayOffer() throws Exception {
        JsonObject v = vector("overlay_offer");
        JsonObject f = v.getAsJsonObject("fields");
        byte[] p = payload(v);
        OverlayOffer got = OverlayOffer.decode(p);
        assertEquals(new OverlayOffer(f.get("path").getAsString(), f.get("maxWidth").getAsInt(),
                f.get("maxHeight").getAsInt(), f.get("slotCount").getAsInt(),
                Long.parseUnsignedLong(f.get("generation").getAsString())), got);
        assertArrayEquals(p, got.encode());
    }

    @Test
    void overlayOfferTruncated() throws Exception {
        byte[] p = payload(vector("overlay_offer"));
        assertThrows(ProtocolException.class, () -> OverlayOffer.decode(Arrays.copyOf(p, p.length - 1)));
    }

    @Test
    void overlayStop() throws Exception {
        byte[] p = payload(vector("overlay_stop"));
        assertEquals(0, p.length);
        assertEquals(new OverlayStop(), OverlayStop.decode(p));
        assertEquals(0, new OverlayStop().encode().length);
    }

    @Test
    void inputCursor() throws Exception {
        JsonObject v = vector("input_cursor");
        JsonObject f = v.getAsJsonObject("fields");
        byte[] p = payload(v);
        JsonArray events = f.getAsJsonArray("events");
        Input.Event[] evs = new Input.Event[events.size()];
        for (int i = 0; i < evs.length; i++) {
            JsonObject e = events.get(i).getAsJsonObject();
            evs[i] = new Input.Event(e.get("kind").getAsInt(), e.get("action").getAsInt(), e.get("code").getAsInt());
        }
        Input got = Input.decode(p);
        assertEquals(new Input(f.get("yaw").getAsFloat(), f.get("pitch").getAsFloat(), evs), got);
        assertEquals(6, Input.CURSOR);
        for (int i = 0; i < evs.length; i++) {
            JsonObject e = events.get(i).getAsJsonObject();
            Input.Event d = got.events()[i];
            assertEquals(e.get("cursorX").getAsInt(), d.cursorX(), "x" + i);
            assertEquals(e.get("cursorY").getAsInt(), d.cursorY(), "y" + i);
            assertEquals(d, Input.Event.cursor(e.get("cursorX").getAsInt(), e.get("cursorY").getAsInt()), "cursor" + i);
        }
        assertArrayEquals(p, got.encode());
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
        assertEquals(6, AppProtocol.MINOR);
        assertEquals(0x0100, AppProtocol.HOST_STATUS);
        assertEquals(0x0101, AppProtocol.GUEST_STATUS);
        assertEquals(0x0130, AppProtocol.BLOCK_ATLAS);
        assertEquals(0x0131, AppProtocol.ATLAS_REGION);
        assertEquals(0x0132, AppProtocol.SECTION_MESH);
        assertEquals(0x0133, AppProtocol.SECTIONS_CLEAR);
        assertEquals(0x0140, AppProtocol.VIEWPORT);
        assertEquals(0x0141, AppProtocol.OVERLAY_OFFER);
        assertEquals(0x0142, AppProtocol.OVERLAY_STOP);
        assertEquals(0x01F0, AppProtocol.DEBUG_COMMAND);
    }

    private static byte[] hex(JsonObject f, String key) {
        return HexFormat.of().parseHex(f.get(key).getAsString());
    }

    @Test
    void blockAtlas() throws Exception {
        JsonObject v = vector("block_atlas");
        JsonObject f = v.getAsJsonObject("fields");
        byte[] p = payload(v);
        BlockAtlas expected = new BlockAtlas(f.get("width").getAsInt(), f.get("height").getAsInt(),
                f.get("format").getAsInt(), hex(f, "dataHex"));
        BlockAtlas got = BlockAtlas.decode(p);
        assertEquals(expected, got);
        assertEquals(expected.hashCode(), got.hashCode());
        assertArrayEquals(p, got.encode());
        assertEquals(1, BlockAtlas.PNG);
        assertThrows(ProtocolException.class, () -> BlockAtlas.decode(Arrays.copyOf(p, p.length - 1)));
    }

    @Test
    void atlasRegion() throws Exception {
        JsonObject v = vector("atlas_region");
        JsonObject f = v.getAsJsonObject("fields");
        byte[] p = payload(v);
        AtlasRegion expected = new AtlasRegion(f.get("x").getAsInt(), f.get("y").getAsInt(),
                f.get("width").getAsInt(), f.get("height").getAsInt(), hex(f, "rgbaHex"));
        AtlasRegion got = AtlasRegion.decode(p);
        assertEquals(expected, got);
        assertEquals(expected.hashCode(), got.hashCode());
        assertArrayEquals(p, got.encode());
        assertThrows(ProtocolException.class, () -> AtlasRegion.decode(Arrays.copyOf(p, p.length - 1)));
    }

    @Test
    void atlasRegionEncodeRejectsWrongLength() {
        assertThrows(IllegalStateException.class, () -> new AtlasRegion(0, 0, 2, 2, new byte[15]).encode());
    }

    private static SectionMesh meshFromFields(JsonObject f) {
        JsonArray vs = f.getAsJsonArray("vertices");
        MeshVertices mv = new MeshVertices();
        for (JsonElement e : vs) {
            JsonObject o = e.getAsJsonObject();
            int c = (int) o.get("color").getAsLong();
            int argb = (c & 0xFF000000) | ((c & 0xFF) << 16) | (c & 0xFF00) | ((c >>> 16) & 0xFF);
            int light = (int) o.get("light").getAsLong();
            int packed = ((light & 0xFF) << 4) | (((light >>> 8) & 0xFF) << 20);
            mv.put(o.get("x").getAsFloat(), o.get("y").getAsFloat(), o.get("z").getAsFloat(),
                    o.get("u").getAsFloat(), o.get("v").getAsFloat(), argb, packed,
                    (int) o.get("flags").getAsLong());
        }
        return new SectionMesh(f.get("sx").getAsInt(), f.get("sy").getAsInt(), f.get("sz").getAsInt(),
                mv.toBytes());
    }

    @Test
    void sectionMesh() throws Exception {
        for (String name : new String[] {"section_mesh", "section_mesh_empty"}) {
            JsonObject v = vector(name);
            JsonObject f = v.getAsJsonObject("fields");
            byte[] p = payload(v);
            JsonArray vs = f.getAsJsonArray("vertices");
            SectionMesh got = SectionMesh.decode(p);
            assertEquals(f.get("sx").getAsInt(), got.sx(), name);
            assertEquals(f.get("sy").getAsInt(), got.sy(), name);
            assertEquals(f.get("sz").getAsInt(), got.sz(), name);
            assertEquals(vs.size(), got.vertexCount(), name);
            for (int i = 0; i < vs.size(); i++) {
                JsonObject o = vs.get(i).getAsJsonObject();
                assertEquals(o.get("x").getAsFloat(), got.x(i), name);
                assertEquals(o.get("y").getAsFloat(), got.y(i), name);
                assertEquals(o.get("z").getAsFloat(), got.z(i), name);
                assertEquals(o.get("u").getAsFloat(), got.u(i), name);
                assertEquals(o.get("v").getAsFloat(), got.v(i), name);
                assertEquals((int) o.get("color").getAsLong(), got.color(i), name);
                assertEquals((int) o.get("light").getAsLong(), got.light(i), name);
                assertEquals((int) o.get("flags").getAsLong(), got.flags(i), name);
            }
            assertArrayEquals(p, got.encode(), name);
            assertEquals(got, SectionMesh.decode(got.encode()), name);
        }
    }

    @Test
    void sectionMeshBuiltFromFieldsMatchesVector() throws Exception {
        for (String name : new String[] {"section_mesh", "section_mesh_empty"}) {
            JsonObject v = vector(name);
            SectionMesh built = meshFromFields(v.getAsJsonObject("fields"));
            assertArrayEquals(payload(v), built.encode(), name);
        }
    }

    @Test
    void sectionMeshEmptyAndConstants() {
        SectionMesh e = SectionMesh.empty(1, -2, 3);
        assertEquals(0, e.vertexCount());
        assertEquals(1, e.sx());
        assertEquals(-2, e.sy());
        assertEquals(3, e.sz());
        assertEquals(32, SectionMesh.VERTEX_BYTES);
        assertEquals(1, SectionMesh.CUTOUT);
        assertEquals(2, SectionMesh.TRANSLUCENT);
    }

    @Test
    void sectionMeshRejectsNonTriangleVertexArray() {
        assertThrows(IllegalArgumentException.class, () -> new SectionMesh(0, 0, 0, new byte[32]));
        assertThrows(IllegalArgumentException.class, () -> new SectionMesh(0, 0, 0, new byte[97]));
    }

    @Test
    void sectionMeshDecodeRejectsBadPayloads() throws Exception {
        byte[] p = payload(vector("section_mesh"));
        assertThrows(ProtocolException.class, () -> SectionMesh.decode(Arrays.copyOf(p, p.length - 1)));
        // valid payload with one vertex removed: count no longer a multiple of 3 once count is patched
        byte[] one = SectionMesh.empty(0, 0, 0).encode();
        assertThrows(ProtocolException.class, () -> SectionMesh.decode(Arrays.copyOf(one, one.length - 1)));
    }

    @Test
    void sectionsClear() throws Exception {
        JsonObject v = vector("sections_clear");
        byte[] p = payload(v);
        assertEquals(0, p.length);
        assertEquals(new SectionsClear(), SectionsClear.decode(p));
        assertEquals(0, new SectionsClear().encode().length);
    }

    @Test
    void debugCommand() throws Exception {
        JsonObject v = vector("debug_command");
        byte[] p = payload(v);
        DebugCommand got = DebugCommand.decode(p);
        assertEquals(new DebugCommand(v.getAsJsonObject("fields").get("command").getAsString()), got);
        assertArrayEquals(p, got.encode());
    }

    private static BlockSelection blockSelectionFrom(JsonObject f) {
        return new BlockSelection(f.get("visible").getAsBoolean(), f.get("minX").getAsFloat(),
                f.get("minY").getAsFloat(), f.get("minZ").getAsFloat(), f.get("maxX").getAsFloat(),
                f.get("maxY").getAsFloat(), f.get("maxZ").getAsFloat(), f.get("kind").getAsInt());
    }

    @Test
    void blockSelection() throws Exception {
        JsonObject v = vector("block_selection");
        byte[] p = payload(v);
        BlockSelection expected = blockSelectionFrom(v.getAsJsonObject("fields"));
        BlockSelection got = BlockSelection.decode(p);
        assertEquals(expected, got);
        assertEquals(BlockSelection.KIND_PLACEMENT, got.kind());
        assertTrue(got.visible());
        assertArrayEquals(p, got.encode());
    }

    @Test
    void blockSelectionHidden() throws Exception {
        JsonObject v = vector("block_selection_hidden");
        byte[] p = payload(v);
        BlockSelection got = BlockSelection.decode(p);
        assertEquals(blockSelectionFrom(v.getAsJsonObject("fields")), got);
        assertEquals(BlockSelection.HIDDEN, got);
        assertFalse(got.visible());
        assertEquals(BlockSelection.KIND_BLOCK, got.kind());
        assertArrayEquals(p, got.encode());
        assertArrayEquals(p, BlockSelection.HIDDEN.encode());
    }

    @Test
    void blockSelectionTruncated() throws Exception {
        byte[] p = payload(vector("block_selection"));
        assertThrows(ProtocolException.class, () -> BlockSelection.decode(Arrays.copyOf(p, p.length - 1)));
    }

    @Test
    void blockSelectionConstants() {
        assertEquals(0, BlockSelection.KIND_BLOCK);
        assertEquals(1, BlockSelection.KIND_PLACEMENT);
        assertEquals(0x0134, AppProtocol.BLOCK_SELECTION);
        assertEquals(6, AppProtocol.MINOR);
    }
}
