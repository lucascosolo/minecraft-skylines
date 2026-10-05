package dev.mcskylines.protocol;

import static org.junit.jupiter.api.Assertions.*;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.stream.Stream;
import org.junit.jupiter.api.DynamicTest;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.TestFactory;

class MinecraftFrameTest {
    private static JsonObject load() throws Exception {
        String dir = System.getProperty("mcskylines.vectors");
        assertNotNull(dir, "system property mcskylines.vectors must be set");
        return JsonParser.parseString(Files.readString(Path.of(dir, "coords.json"))).getAsJsonObject();
    }

    @Test
    void yOffsetMatchesVectors() throws Exception {
        assertEquals(load().get("yOffset").getAsDouble(), MinecraftFrame.Y_OFFSET, 0.0);
    }

    @TestFactory
    Stream<DynamicTest> coordCases() throws Exception {
        JsonObject root = load();
        double tol = root.get("tolerance").getAsDouble();
        List<DynamicTest> tests = new ArrayList<>();
        int i = 0;
        for (JsonElement el : root.getAsJsonArray("cases")) {
            JsonObject cs = el.getAsJsonObject().getAsJsonObject("cs");
            JsonObject mc = el.getAsJsonObject().getAsJsonObject("mc");
            tests.add(DynamicTest.dynamicTest("case_" + i++, () -> {
                MinecraftFrame.Pose p = MinecraftFrame.toMinecraft(cs.get("x").getAsDouble(),
                        cs.get("y").getAsDouble(), cs.get("z").getAsDouble(),
                        cs.get("eulerX").getAsDouble(), cs.get("eulerY").getAsDouble());
                assertEquals(mc.get("x").getAsDouble(), p.x(), tol, "x");
                assertEquals(mc.get("y").getAsDouble(), p.y(), tol, "y");
                assertEquals(mc.get("z").getAsDouble(), p.z(), tol, "z");
                assertEquals(mc.get("yaw").getAsDouble(), p.yaw(), tol, "yaw");
                assertEquals(mc.get("pitch").getAsDouble(), p.pitch(), tol, "pitch");
            }));
        }
        assertEquals(7, tests.size());
        return tests.stream();
    }

    @Test
    void wrap180() {
        assertEquals(-180.0, MinecraftFrame.wrap180(180), 0.0);
        assertEquals(-180.0, MinecraftFrame.wrap180(-180), 0.0);
        assertEquals(-180.0, MinecraftFrame.wrap180(540), 0.0);
        assertEquals(170.0, MinecraftFrame.wrap180(-190), 1e-9);
    }
}
