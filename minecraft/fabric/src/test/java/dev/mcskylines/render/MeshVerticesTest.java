package dev.mcskylines.render;

import static org.junit.jupiter.api.Assertions.*;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import org.junit.jupiter.api.Test;

class MeshVerticesTest {
    @Test
    void putWritesWireLayout() {
        MeshVertices mv = new MeshVertices();
        int packedLight = (5 << 4) | (11 << 20);
        mv.put(1.5f, -2f, 3.25f, 0.5f, 0.75f, 0x80112233, packedLight, 2);
        assertEquals(1, mv.vertexCount());
        byte[] b = mv.toBytes();
        assertEquals(32, b.length);
        ByteBuffer bb = ByteBuffer.wrap(b).order(ByteOrder.LITTLE_ENDIAN);
        assertEquals(1.5f, bb.getFloat(0));
        assertEquals(-2f, bb.getFloat(4));
        assertEquals(3.25f, bb.getFloat(8));
        assertEquals(0.5f, bb.getFloat(12));
        assertEquals(0.75f, bb.getFloat(16));
        assertEquals(0x11, b[20] & 0xFF);
        assertEquals(0x22, b[21] & 0xFF);
        assertEquals(0x33, b[22] & 0xFF);
        assertEquals(0x80, b[23] & 0xFF);
        assertEquals(5 | (11 << 8), bb.getInt(24));
        assertEquals(2, bb.getInt(28));
    }

    @Test
    void resetClearsAndToBytesIsACopy() {
        MeshVertices mv = new MeshVertices();
        mv.put(0, 0, 0, 0, 0, 0, 0, 0);
        byte[] first = mv.toBytes();
        first[0] = 99;
        assertEquals(0, mv.toBytes()[0]);
        mv.reset();
        assertEquals(0, mv.vertexCount());
        assertEquals(0, mv.toBytes().length);
    }

    @Test
    void growsWithoutLimit() {
        MeshVertices mv = new MeshVertices();
        for (int i = 0; i < 100_000; i++) mv.put(i, 0, 0, 0, 0, 0xFFFFFFFF, 0, 0);
        assertEquals(100_000, mv.vertexCount());
        byte[] b = mv.toBytes();
        assertEquals(3_200_000, b.length);
        assertEquals(99_999f, ByteBuffer.wrap(b).order(ByteOrder.LITTLE_ENDIAN).getFloat(99_999 * 32));
    }

    @Test
    void unshadeDividesChannelsKeepsAlpha() {
        assertEquals(0xFFFFFFFF, MeshVertices.unshade(0xFF808080, 0.5f));
        assertEquals(0x80505050, MeshVertices.unshade(0x80404040, 0.8f));
        assertEquals(0xFFFFFFFF, MeshVertices.unshade(0xFFC0C0C0, 0.5f));
    }

    @Test
    void unshadeLeavesColorAloneForNeutralShade() {
        assertEquals(0xFF123456, MeshVertices.unshade(0xFF123456, 1.0f));
        assertEquals(0xFF123456, MeshVertices.unshade(0xFF123456, 0.999f));
        assertEquals(0xFF123456, MeshVertices.unshade(0xFF123456, 0f));
        assertEquals(0xFF123456, MeshVertices.unshade(0xFF123456, -1f));
    }
}
