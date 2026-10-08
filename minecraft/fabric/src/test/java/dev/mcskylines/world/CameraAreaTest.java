package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.Test;

class CameraAreaTest {
    @Test
    void radii() {
        assertEquals(2, CameraArea.TICK_RADIUS);
        assertEquals(CameraArea.TICK_RADIUS + 2, CameraArea.TICKET_RADIUS);
    }

    @Test
    void atFloorsTowardNegativeInfinity() {
        assertEquals(new CameraArea(-1, -1), CameraArea.at(-0.5, -0.5));
        assertEquals(new CameraArea(0, 0), CameraArea.at(15.99, 0));
        assertEquals(new CameraArea(1, 1), CameraArea.at(16, 16));
        assertEquals(new CameraArea(-1, 0), CameraArea.at(-16, 0));
        assertEquals(new CameraArea(-2, 0), CameraArea.at(-16.01, 0));
    }

    @Test
    void ticksIsChebyshevRadiusTwo() {
        CameraArea a = new CameraArea(10, -3);
        int count = 0;
        for (int dx = -5; dx <= 5; dx++) {
            for (int dz = -5; dz <= 5; dz++) {
                boolean in = Math.max(Math.abs(dx), Math.abs(dz)) <= 2;
                assertEquals(in, a.ticks(10 + dx, -3 + dz), dx + "," + dz);
                if (a.ticks(10 + dx, -3 + dz)) count++;
            }
        }
        assertEquals(25, count);
    }

    @Test
    void ticksAtUsesContainingChunk() {
        CameraArea a = new CameraArea(0, 0);
        assertTrue(a.ticksAt(47.99, 0));
        assertFalse(a.ticksAt(48, 0));
        assertTrue(a.ticksAt(-32, -32));
        assertFalse(a.ticksAt(-32.01, 0));
    }

    @Test
    void boundsAreThe80MetreSquare() {
        CameraArea a = new CameraArea(3, -4);
        assertEquals(16.0, a.minX());
        assertEquals(96.0, a.maxX());
        assertEquals(-96.0, a.minZ());
        assertEquals(-16.0, a.maxZ());
        assertEquals(80.0, a.maxX() - a.minX());
    }
}
