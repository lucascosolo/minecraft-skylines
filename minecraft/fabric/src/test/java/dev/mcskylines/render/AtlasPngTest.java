package dev.mcskylines.render;

import static org.junit.jupiter.api.Assertions.*;

import java.awt.image.BufferedImage;
import java.io.ByteArrayInputStream;
import java.util.Random;
import javax.imageio.ImageIO;
import org.junit.jupiter.api.Test;

class AtlasPngTest {
    private static byte[] noise(int w, int h, long seed) {
        byte[] b = new byte[w * h * 4];
        new Random(seed).nextBytes(b);
        return b;
    }

    @Test
    void encodeRoundTripsThroughImageIo() throws Exception {
        int w = 37, h = 19;
        byte[] rgba = noise(w, h, 1);
        ImageIO.setUseCache(false); // no temp cache file (the sandbox has no writable java.io.tmpdir)
        BufferedImage img = ImageIO.read(new ByteArrayInputStream(AtlasPng.encode(w, h, rgba)));
        assertNotNull(img);
        assertEquals(w, img.getWidth());
        assertEquals(h, img.getHeight());
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                int o = (y * w + x) * 4;
                int argb = ((rgba[o + 3] & 0xFF) << 24) | ((rgba[o] & 0xFF) << 16) | ((rgba[o + 1] & 0xFF) << 8)
                        | (rgba[o + 2] & 0xFF);
                assertEquals(argb, img.getRGB(x, y), "pixel " + x + "," + y);
            }
        }
    }

    @Test
    void halveAveragesBlocksRoundingDown() {
        // 4x2: block 0 = pixels (0,0),(1,0),(0,1),(1,1); block 1 = columns 2,3
        byte[] in = new byte[4 * 2 * 4];
        int[][] px = {
            {10, 20, 30, 40}, {11, 21, 31, 41}, {100, 0, 255, 255}, {101, 1, 255, 255},
            {12, 22, 32, 42}, {13, 23, 33, 44}, {102, 2, 255, 255}, {103, 3, 254, 253},
        };
        for (int i = 0; i < 8; i++) for (int c = 0; c < 4; c++) in[i * 4 + c] = (byte) px[i][c];
        byte[] out = AtlasPng.halve(4, 2, in);
        // block 0: R (10+11+12+13)/4=11.5->11, G 21.5->21, B 31.5->31, A 41.5->41... (40+41+42+44)/4=41.75->41
        // block 1: R (100+101+102+103)/4=101.5->101, G 1.5->1, B (255*3+254)/4=254.75->254, A (255*3+253)/4=254.5->254
        assertArrayEquals(new byte[] {11, 21, 31, 41, 101, 1, (byte) 254, (byte) 254}, out);
    }

    @Test
    void fitSmallImageIsUntouched() {
        byte[] rgba = new byte[8 * 8 * 4];
        AtlasPng.Fitted f = AtlasPng.fit(8, 8, rgba, 100_000);
        assertEquals(0, f.halvings());
        assertEquals(8, f.width());
        assertEquals(8, f.height());
        assertTrue(f.png().length <= 100_000);
    }

    @Test
    void fitHalvesNoiseUntilItFits() {
        AtlasPng.Fitted f = AtlasPng.fit(256, 256, noise(256, 256, 7), 100_000);
        assertTrue(f.halvings() >= 1);
        assertTrue(f.png().length <= 100_000);
        assertEquals(256 >> f.halvings(), f.width());
        assertEquals(256 >> f.halvings(), f.height());
    }
}
