package dev.mcskylines.bridge.overlay;

import java.nio.ByteBuffer;
import java.nio.file.Path;
import java.util.concurrent.locks.LockSupport;

/**
 * {@code overlay-demo <path> <width> <height> <frames>}: publishes {@code frames} frames at about 60 Hz into an
 * overlay file of exactly {@code width x height}, generation 1, flags 0 (top-down). Frame n (1-based, = frameId) has
 * every pixel RGBA = (n & 0xFF, (n >> 8) & 0xFF, row * 255 / (height - 1), 255): red/green carry the frame number,
 * blue is 0 on the top row and 255 on the bottom row (orientation check), alpha opaque (valid premultiplied).
 */
public final class OverlayDemo {
	private OverlayDemo() {
	}

	public static void main(String[] args) throws Exception {
		if (args.length != 4) {
			System.err.println("usage: overlay-demo <path> <width> <height> <frames>");
			System.exit(2);
		}
		Path path = Path.of(args[0]);
		int w = Integer.parseInt(args[1]);
		int h = Integer.parseInt(args[2]);
		int frames = Integer.parseInt(args[3]);
		byte[] row = new byte[w * 4];
		try (OverlayWriter writer = OverlayWriter.open(path, w, h, 1)) {
			long next = System.nanoTime();
			for (long n = 1; n <= frames; n++) {
				ByteBuffer px = writer.backPixels();
				for (int y = 0; y < h; y++) {
					byte b = (byte) (h == 1 ? 0 : y * 255 / (h - 1));
					for (int x = 0; x < w; x++) {
						row[4 * x] = (byte) n;
						row[4 * x + 1] = (byte) (n >> 8);
						row[4 * x + 2] = b;
						row[4 * x + 3] = (byte) 0xFF;
					}
					px.put(y * row.length, row);
				}
				writer.publish(w, h, 0, n);
				next += 1_000_000_000L / 60;
				LockSupport.parkNanos(next - System.nanoTime());
			}
		}
		System.out.println("published " + frames + " frames " + w + "x" + h + " to " + path.toAbsolutePath());
	}
}
