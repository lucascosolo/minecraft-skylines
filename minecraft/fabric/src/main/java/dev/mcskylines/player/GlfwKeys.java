package dev.mcskylines.player;

import java.util.HashMap;
import java.util.Map;

/** GLFW key and mouse-button codes (the protocol's) to the SDL3 codes Minecraft 26.3 uses. */
public final class GlfwKeys {
	private static final Map<Integer, Integer> SCANCODES = new HashMap<>();

	static {
		range(65, 90, 4); // A..Z
		range(49, 57, 30); // 1..9
		range(290, 301, 58); // F1..F12
		range(321, 329, 89); // KP_1..KP_9
		range(302, 313, 104); // F13..F24
		int[] pairs = {
			48, 39, 257, 40, 256, 41, 259, 42, 258, 43, 32, 44, 45, 45, 61, 46, 91, 47, 93, 48, 92, 49, 59, 51, 39, 52,
			96, 53, 44, 54, 46, 55, 47, 56, 280, 57, 283, 70, 281, 71, 284, 72, 260, 73, 268, 74, 266, 75, 261, 76,
			269, 77, 267, 78, 262, 79, 263, 80, 264, 81, 265, 82, 282, 83, 331, 84, 332, 85, 333, 86, 334, 87, 335, 88,
			320, 98, 330, 99, 162, 100, 336, 103, 348, 118,
			341, 224, 340, 225, 342, 226, 343, 227, 345, 228, 344, 229, 346, 230, 347, 231};
		for (int i = 0; i < pairs.length; i += 2) {
			SCANCODES.put(pairs[i], pairs[i + 1]);
		}
	}

	private GlfwKeys() {
	}

	private static void range(int firstGlfw, int lastGlfw, int firstScancode) {
		for (int k = firstGlfw; k <= lastGlfw; k++) {
			SCANCODES.put(k, firstScancode + k - firstGlfw);
		}
	}

	/** SDL3 scancode for a GLFW key code, or -1 when it has none. */
	public static int toScancode(int glfwKey) {
		return SCANCODES.getOrDefault(glfwKey, -1);
	}

	/** SDL3 mouse button (1 left, 2 middle, 3 right, 4, 5 ...) for a GLFW button (0 left, 1 right, 2 middle ...), or -1. */
	public static int toSdlButton(int glfwButton) {
		return switch (glfwButton) {
			case 0 -> 1;
			case 1 -> 3;
			case 2 -> 2;
			case 3, 4, 5, 6, 7 -> glfwButton + 1;
			default -> -1;
		};
	}
}
