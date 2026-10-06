package dev.mcskylines.bridge;

import static org.junit.jupiter.api.Assertions.*;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HexFormat;
import java.util.List;
import java.util.stream.Stream;
import org.junit.jupiter.api.DynamicTest;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.TestFactory;

class FrameVectorsTest {
    private static JsonObject load() throws Exception {
        String dir = System.getProperty("mcskylines.vectors");
        assertNotNull(dir, "system property mcskylines.vectors must be set");
        return JsonParser.parseString(Files.readString(Path.of(dir, "frames.json"))).getAsJsonObject();
    }

    private static byte[] hex(String s) {
        return HexFormat.of().parseHex(s);
    }

    private static long u64(JsonElement e) {
        return Long.parseUnsignedLong(e.getAsString());
    }

    private static Object expected(int type, JsonObject f) {
        return switch (type) {
            case 1 -> new Hello(f.get("magic").getAsInt(), f.get("bridgeVersion").getAsInt(),
                    f.get("appProtocol").getAsString(), f.get("appMajor").getAsInt(), f.get("appMinor").getAsInt(),
                    f.get("peerName").getAsString(), f.get("peerVersion").getAsString(), u64(f.get("sessionNonce")));
            case 2 -> new Welcome(f.get("accepted").getAsBoolean(), f.get("rejectCode").getAsInt(),
                    f.get("rejectReason").getAsString(), f.get("bridgeVersion").getAsInt(),
                    f.get("appProtocol").getAsString(), f.get("appMajor").getAsInt(), f.get("appMinor").getAsInt(),
                    f.get("peerName").getAsString(), f.get("peerVersion").getAsString(),
                    f.get("heartbeatIntervalMs").getAsLong(), f.get("peerTimeoutMs").getAsLong(),
                    u64(f.get("sessionId")));
            case 3 -> new Heartbeat(f.get("seq").getAsLong(), u64(f.get("senderUptimeMs")));
            case 4 -> new Goodbye(f.get("code").getAsInt(), f.get("reason").getAsString());
            default -> throw new AssertionError("unexpected type " + type);
        };
    }

    private static byte[] encodeBody(Object o) {
        if (o instanceof Hello h) return h.encode();
        if (o instanceof Welcome w) return w.encode();
        if (o instanceof Heartbeat h) return h.encode();
        if (o instanceof Goodbye g) return g.encode();
        throw new AssertionError("not a bridge message: " + o);
    }

    @TestFactory
    Stream<DynamicTest> validVectors() throws Exception {
        JsonArray valid = load().getAsJsonArray("valid");
        List<DynamicTest> tests = new ArrayList<>();
        for (JsonElement el : valid) {
            JsonObject v = el.getAsJsonObject();
            String name = v.get("name").getAsString();
            int type = v.get("type").getAsInt();
            byte[] bytes = hex(v.get("hex").getAsString());
            tests.add(DynamicTest.dynamicTest(name, () -> {
                Frame frame = FrameCodec.decode(bytes);
                assertEquals(type, frame.type());
                Object body = Messages.decodeBody(frame);
                if (type >= 0x0100) {
                    Frame app = assertInstanceOf(Frame.class, body);
                    assertEquals(type, app.type());
                    assertArrayEquals(bytes, FrameCodec.encode(type, app.payload()));
                    return;
                }
                assertEquals(expected(type, v.getAsJsonObject("fields")), body);
                if (!name.equals("heartbeat_trailing_bytes")) {
                    assertArrayEquals(bytes, FrameCodec.encode(type, encodeBody(body)));
                }
            }));
        }
        return tests.stream();
    }

    @TestFactory
    Stream<DynamicTest> invalidVectors() throws Exception {
        List<DynamicTest> tests = new ArrayList<>();
        for (JsonElement el : load().getAsJsonArray("invalid")) {
            JsonObject v = el.getAsJsonObject();
            byte[] bytes = hex(v.get("hex").getAsString());
            if (bytes.length >= 6 && ((bytes[4] & 0xFF) | (bytes[5] & 0xFF) << 8) >= FrameCodec.APP_MIN) {
                continue; // a well-formed frame with an invalid application payload: the app layer's tests reject it
            }
            tests.add(DynamicTest.dynamicTest(v.get("name").getAsString(),
                    () -> assertThrows(ProtocolException.class,
                            () -> Messages.decodeBody(FrameCodec.decode(bytes)))));
        }
        return tests.stream();
    }

    @Test
    void vectorCounts() throws Exception {
        JsonObject root = load();
        assertEquals(47, root.getAsJsonArray("valid").size()); // 17 of 1.1 + 6 of 1.2 + 4 of 1.3 + 2 of 1.4 + 8 of 1.5 + 2 of 1.6 + 2 of 1.7 + 2 of 1.8 + 4 of 1.9
        assertEquals(13, root.getAsJsonArray("invalid").size()); // 6 of them application-level (3 of 1.5, 1 of 1.8, 2 of 1.9)
    }
}
