package dev.mcskylines.render;

import java.io.ByteArrayOutputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.util.zip.CRC32;
import java.util.zip.Deflater;
import java.util.zip.DeflaterOutputStream;

/** PNG encoding of an RGBA8 image (top row first) and the halving used to fit BLOCK_ATLAS under the frame limit. */
public final class AtlasPng {
	public record Fitted(int width, int height, byte[] png, int halvings) {
	}

	private AtlasPng() {
	}

	public static Fitted fit(int width, int height, byte[] rgba, int maxPngBytes) {
		int halvings = 0;
		byte[] png = encode(width, height, rgba);
		while (png.length > maxPngBytes && width > 1 && height > 1) {
			rgba = halve(width, height, rgba);
			width /= 2;
			height /= 2;
			halvings++;
			png = encode(width, height, rgba);
		}
		return new Fitted(width, height, png, halvings);
	}

	public static byte[] halve(int width, int height, byte[] rgba) {
		int w = width / 2, h = height / 2;
		byte[] out = new byte[w * h * 4];
		for (int y = 0; y < h; y++) {
			for (int x = 0; x < w; x++) {
				int a = ((2 * y) * width + 2 * x) * 4, b = ((2 * y + 1) * width + 2 * x) * 4;
				for (int c = 0; c < 4; c++) {
					int sum = (rgba[a + c] & 0xFF) + (rgba[a + 4 + c] & 0xFF) + (rgba[b + c] & 0xFF) + (rgba[b + 4 + c] & 0xFF);
					out[(y * w + x) * 4 + c] = (byte) (sum >> 2);
				}
			}
		}
		return out;
	}

	public static byte[] encode(int width, int height, byte[] rgba) {
		try {
			ByteArrayOutputStream raw = new ByteArrayOutputStream(rgba.length / 4);
			Deflater deflater = new Deflater(Deflater.BEST_SPEED);
			try (DeflaterOutputStream z = new DeflaterOutputStream(raw, deflater, 1 << 16)) {
				int stride = width * 4;
				for (int y = 0; y < height; y++) {
					z.write(0); // filter: none
					z.write(rgba, y * stride, stride);
				}
			} finally {
				deflater.end();
			}
			ByteArrayOutputStream png = new ByteArrayOutputStream(raw.size() + 64);
			png.write(new byte[] {(byte) 0x89, 'P', 'N', 'G', '\r', '\n', 0x1A, '\n'});
			ByteArrayOutputStream ihdr = new ByteArrayOutputStream();
			DataOutputStream d = new DataOutputStream(ihdr);
			d.writeInt(width);
			d.writeInt(height);
			d.write(new byte[] {8, 6, 0, 0, 0}); // 8-bit RGBA, deflate, no filter method extras, no interlace
			chunk(png, "IHDR", ihdr.toByteArray());
			chunk(png, "IDAT", raw.toByteArray());
			chunk(png, "IEND", new byte[0]);
			return png.toByteArray();
		} catch (IOException e) {
			throw new UncheckedIOException(e);
		}
	}

	private static void chunk(ByteArrayOutputStream out, String type, byte[] data) throws IOException {
		DataOutputStream d = new DataOutputStream(out);
		byte[] t = type.getBytes(StandardCharsets.US_ASCII);
		d.writeInt(data.length);
		d.write(t);
		d.write(data);
		CRC32 crc = new CRC32();
		crc.update(t);
		crc.update(data);
		d.writeInt((int) crc.getValue());
	}
}
