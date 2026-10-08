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
import java.util.HexFormat;
import org.junit.jupiter.api.Test;

class EntityVectorsTest {
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

    private static void assertFloats(JsonArray expected, float[] actual, String name) {
        assertEquals(expected.size(), actual.length, name);
        for (int i = 0; i < actual.length; i++) assertEquals(expected.get(i).getAsFloat(), actual[i], name + "[" + i + "]");
    }

    @Test
    void constants() {
        assertEquals(0x01E0, AppProtocol.ENTITY_MODEL);
        assertEquals(0x01E1, AppProtocol.ENTITY_TEXTURE);
        assertEquals(0x01E2, AppProtocol.ENTITY_STATES);
        assertEquals(21, AppProtocol.MINOR);
        assertEquals(1024, EntityModel.MAX_PARTS);
        assertEquals(4096, EntityModel.MAX_QUADS);
        assertEquals(23, EntityModel.FLOATS_PER_QUAD);
        assertEquals(0xFFFF, EntityModel.NO_PARENT);
        assertEquals(1, EntityTexture.PNG);
        assertEquals(4194304, EntityTexture.MAX_LENGTH);
        assertEquals(2048, EntityStates.MAX_ENTITIES);
        assertEquals(16, EntityStates.MAX_DRAWS);
        assertEquals(1024, EntityStates.MAX_PARTS);
        assertEquals(1, EntityStates.FLAG_HIDDEN);
        assertEquals(2, EntityStates.FLAG_SKIP);
        assertEquals(9, EntityStates.POSE_FLOATS);
    }

    @Test
    void modelVector() throws Exception {
        String name = "entity_model_two_parts";
        JsonObject v = find("valid", name);
        JsonObject f = v.getAsJsonObject("fields");
        Frame fr = frame(v);
        assertEquals(AppProtocol.ENTITY_MODEL, fr.type(), name);
        EntityModel got = EntityModel.decode(fr.payload());
        assertEquals((int) f.get("modelId").getAsLong(), got.modelId(), name);
        assertEquals(f.get("name").getAsString(), got.name(), name);
        JsonArray parts = f.getAsJsonArray("parts");
        assertEquals(parts.size(), got.parts().size(), name);
        for (int i = 0; i < parts.size(); i++) {
            JsonObject p = parts.get(i).getAsJsonObject();
            assertEquals(p.get("parent").getAsInt(), got.parts().get(i).parent(), name);
            assertFloats(p.getAsJsonArray("quads"), got.parts().get(i).quads(), name + " part " + i);
        }
        assertArrayEquals(fr.payload(), got.encode(), name);
    }

    @Test
    void textureVector() throws Exception {
        String name = "entity_texture_png";
        JsonObject v = find("valid", name);
        JsonObject f = v.getAsJsonObject("fields");
        Frame fr = frame(v);
        assertEquals(AppProtocol.ENTITY_TEXTURE, fr.type(), name);
        EntityTexture got = EntityTexture.decode(fr.payload());
        assertEquals((int) f.get("textureId").getAsLong(), got.textureId(), name);
        assertEquals((int) f.get("width").getAsLong(), got.width(), name);
        assertEquals((int) f.get("height").getAsLong(), got.height(), name);
        assertEquals(f.get("format").getAsInt(), got.format(), name);
        assertArrayEquals(HexFormat.of().parseHex(f.get("dataHex").getAsString()), got.data(), name);
        assertArrayEquals(fr.payload(), got.encode(), name);
    }

    @Test
    void statesVectors() throws Exception {
        for (String name : new String[] {"entity_states_empty", "entity_states_two"}) {
            JsonObject v = find("valid", name);
            JsonObject f = v.getAsJsonObject("fields");
            Frame fr = frame(v);
            assertEquals(AppProtocol.ENTITY_STATES, fr.type(), name);
            EntityStates got = EntityStates.decode(fr.payload());
            assertEquals((int) f.get("seq").getAsLong(), got.seq(), name);
            JsonArray ents = f.getAsJsonArray("entities");
            assertEquals(ents.size(), got.entities().size(), name);
            for (int i = 0; i < ents.size(); i++) {
                JsonObject e = ents.get(i).getAsJsonObject();
                EntityStates.Entity a = got.entities().get(i);
                assertEquals((int) e.get("entityId").getAsLong(), a.entityId(), name);
                assertEquals(e.get("x").getAsFloat(), a.x(), name);
                assertEquals(e.get("y").getAsFloat(), a.y(), name);
                assertEquals(e.get("z").getAsFloat(), a.z(), name);
                assertEquals(e.get("bodyYaw").getAsFloat(), a.bodyYaw(), name);
                assertEquals(e.get("headYaw").getAsFloat(), a.headYaw(), name);
                assertEquals(e.get("pitch").getAsFloat(), a.pitch(), name);
                JsonArray draws = e.getAsJsonArray("draws");
                assertEquals(draws.size(), a.draws().size(), name);
                for (int j = 0; j < draws.size(); j++) {
                    JsonObject d = draws.get(j).getAsJsonObject();
                    EntityStates.Draw ad = a.draws().get(j);
                    assertEquals((int) d.get("modelId").getAsLong(), ad.modelId(), name);
                    assertEquals((int) d.get("textureId").getAsLong(), ad.textureId(), name);
                    assertEquals((int) d.get("color").getAsLong(), ad.color(), name);
                    assertFloats(d.getAsJsonArray("matrix"), ad.matrix(), name + " matrix");
                    JsonArray parts = d.getAsJsonArray("parts");
                    assertEquals(parts.size(), ad.flags().length, name);
                    assertEquals(parts.size() * EntityStates.POSE_FLOATS, ad.poses().length, name);
                    String[] keys = {"px", "py", "pz", "xRot", "yRot", "zRot", "xScale", "yScale", "zScale"};
                    for (int k = 0; k < parts.size(); k++) {
                        JsonObject p = parts.get(k).getAsJsonObject();
                        for (int c = 0; c < keys.length; c++) {
                            assertEquals(p.get(keys[c]).getAsFloat(), ad.poses()[k * EntityStates.POSE_FLOATS + c], name + " " + keys[c]);
                        }
                        assertEquals(p.get("flags").getAsInt(), ad.flags()[k] & 0xFF, name);
                    }
                }
            }
            assertArrayEquals(fr.payload(), got.encode(), name);
        }
    }

    @Test
    void invalidVectorsThrow() throws Exception {
        record Case(String name, int type) {}
        Case[] cases = {
            new Case("entity_model_too_many_parts", AppProtocol.ENTITY_MODEL),
            new Case("entity_model_parent_not_before", AppProtocol.ENTITY_MODEL),
            new Case("entity_model_too_many_quads", AppProtocol.ENTITY_MODEL),
            new Case("entity_texture_too_long", AppProtocol.ENTITY_TEXTURE),
            new Case("entity_states_too_many", AppProtocol.ENTITY_STATES),
            new Case("entity_states_too_many_draws", AppProtocol.ENTITY_STATES),
            new Case("entity_states_too_many_parts", AppProtocol.ENTITY_STATES),
            new Case("entity_states_truncated", AppProtocol.ENTITY_STATES),
        };
        for (Case c : cases) {
            Frame fr = frame(find("invalid", c.name()));
            assertEquals(c.type(), fr.type(), c.name());
            byte[] p = fr.payload();
            switch (c.type()) {
                case AppProtocol.ENTITY_MODEL -> assertThrows(ProtocolException.class, () -> EntityModel.decode(p), c.name());
                case AppProtocol.ENTITY_TEXTURE -> assertThrows(ProtocolException.class, () -> EntityTexture.decode(p), c.name());
                default -> assertThrows(ProtocolException.class, () -> EntityStates.decode(p), c.name());
            }
        }
    }
}
