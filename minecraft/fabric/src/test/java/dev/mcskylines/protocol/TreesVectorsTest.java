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

class TreesVectorsTest {
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
        assertEquals(0x01C0, AppProtocol.TREES);
        assertEquals(0x01C1, AppProtocol.TREE_FELLED);
        assertEquals(0x01C2, AppProtocol.TREE_GROWN);
        assertEquals(4096, Trees.MAX_COUNT);
    }

    @Test
    void treesVectors() throws Exception {
        for (String name : new String[] {"trees_empty", "trees_one", "trees_two"}) {
            JsonObject v = find("valid", name);
            JsonObject f = v.getAsJsonObject("fields");
            Frame fr = frame(v);
            assertEquals(AppProtocol.TREES, fr.type(), name);
            Trees got = Trees.decode(fr.payload());
            assertEquals((int) f.get("epoch").getAsLong(), got.epoch(), name);
            assertEquals(f.get("regionX").getAsInt(), got.regionX(), name);
            assertEquals(f.get("regionZ").getAsInt(), got.regionZ(), name);
            assertEquals(f.getAsJsonArray("trees").size(), got.trees().size(), name);
            for (int i = 0; i < got.trees().size(); i++) {
                JsonObject t = f.getAsJsonArray("trees").get(i).getAsJsonObject();
                Trees.Tree a = got.trees().get(i);
                assertEquals((int) t.get("id").getAsLong(), a.id(), name);
                assertEquals(t.get("x").getAsFloat(), a.x(), name);
                assertEquals(t.get("y").getAsFloat(), a.y(), name);
                assertEquals(t.get("z").getAsFloat(), a.z(), name);
                assertEquals(t.get("height").getAsFloat(), a.height(), name);
                assertEquals(t.get("radius").getAsFloat(), a.radius(), name);
                assertEquals(t.get("kind").getAsInt(), a.kind(), name);
            }
            assertArrayEquals(fr.payload(), got.encode(), name);
        }
    }

    @Test
    void treeFelledVector() throws Exception {
        JsonObject v = find("valid", "tree_felled");
        Frame fr = frame(v);
        assertEquals(AppProtocol.TREE_FELLED, fr.type());
        TreeFelled got = TreeFelled.decode(fr.payload());
        assertEquals(v.getAsJsonObject("fields").get("openSeq").getAsInt(), got.openSeq());
        assertEquals(v.getAsJsonObject("fields").get("treeId").getAsInt(), got.treeId());
        assertArrayEquals(fr.payload(), got.encode());
    }

    @Test
    void treeGrownVector() throws Exception {
        JsonObject v = find("valid", "tree_grown");
        Frame fr = frame(v);
        assertEquals(AppProtocol.TREE_GROWN, fr.type());
        JsonObject f = v.getAsJsonObject("fields");
        TreeGrown got = TreeGrown.decode(fr.payload());
        assertEquals((int) f.get("openSeq").getAsLong(), got.openSeq());
        assertEquals(f.get("x").getAsFloat(), got.x());
        assertEquals(f.get("y").getAsFloat(), got.y());
        assertEquals(f.get("z").getAsFloat(), got.z());
        assertEquals(f.get("kind").getAsInt(), got.kind());
        assertEquals((int) f.get("seed").getAsLong(), got.seed());
        assertEquals(21, fr.payload().length);
        assertArrayEquals(fr.payload(), got.encode());
    }

    @Test
    void treeGrownInvalidVectorsThrow() throws Exception {
        for (String name : new String[] {"tree_grown_truncated", "tree_grown_bad_kind"}) {
            Frame fr = frame(find("invalid", name));
            assertEquals(AppProtocol.TREE_GROWN, fr.type(), name);
            assertThrows(ProtocolException.class, () -> TreeGrown.decode(fr.payload()), name);
        }
    }

    @Test
    void treeGrownEveryTruncationThrows() throws Exception {
        byte[] p = frame(find("valid", "tree_grown")).payload();
        for (int len = 0; len < p.length; len++) {
            byte[] cut = Arrays.copyOf(p, len);
            assertThrows(ProtocolException.class, () -> TreeGrown.decode(cut), "grown cut to " + len);
        }
    }

    @Test
    void invalidVectorsThrow() throws Exception {
        for (String name : new String[] {"trees_too_many", "trees_truncated", "trees_trailing_bytes"}) {
            Frame fr = frame(find("invalid", name));
            assertEquals(AppProtocol.TREES, fr.type(), name);
            assertThrows(ProtocolException.class, () -> Trees.decode(fr.payload()), name);
        }
        Frame fr = frame(find("invalid", "tree_felled_truncated"));
        assertThrows(ProtocolException.class, () -> TreeFelled.decode(fr.payload()));
    }

    @Test
    void everyTruncationThrows() throws Exception {
        byte[] p = frame(find("valid", "trees_two")).payload();
        for (int len = 0; len < p.length; len++) {
            byte[] cut = Arrays.copyOf(p, len);
            assertThrows(ProtocolException.class, () -> Trees.decode(cut), "trees cut to " + len);
        }
        byte[] t = frame(find("valid", "tree_felled")).payload();
        for (int len = 0; len < t.length; len++) {
            byte[] cut = Arrays.copyOf(t, len);
            assertThrows(ProtocolException.class, () -> TreeFelled.decode(cut), "felled cut to " + len);
        }
    }
}
