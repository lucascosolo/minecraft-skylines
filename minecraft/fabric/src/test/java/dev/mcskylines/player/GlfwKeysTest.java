package dev.mcskylines.player;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;

class GlfwKeysTest {
	private static void range(int glfwFirst, int glfwLast, int scanFirst) {
		for (int k = glfwFirst; k <= glfwLast; k++) {
			assertEquals(scanFirst + (k - glfwFirst), GlfwKeys.toScancode(k), "glfw key " + k);
		}
	}

	private static void table(int[][] pairs) {
		for (int[] p : pairs) {
			assertEquals(p[1], GlfwKeys.toScancode(p[0]), "glfw key " + p[0]);
		}
	}

	@Test
	void lettersMapToHidA_Z() {
		range(65, 90, 4);
		assertEquals(4, GlfwKeys.toScancode(65));
		assertEquals(29, GlfwKeys.toScancode(90));
	}

	@Test
	void digitsMapWithZeroLast() {
		range(49, 57, 30);
		assertEquals(39, GlfwKeys.toScancode(48));
	}

	@Test
	void functionKeysF1ToF12AndF13ToF24() {
		range(290, 301, 58);
		range(302, 313, 104);
	}

	@Test
	void keypadDigitsAndOperators() {
		range(321, 329, 89);
		table(new int[][] { { 320, 98 }, { 330, 99 }, { 331, 84 }, { 332, 85 }, { 333, 86 }, { 334, 87 }, { 335, 88 }, { 336, 103 } });
	}

	@Test
	void editingNavigationAndPunctuation() {
		table(new int[][] {
			{ 257, 40 }, { 256, 41 }, { 259, 42 }, { 258, 43 }, { 32, 44 }, { 45, 45 }, { 61, 46 }, { 91, 47 }, { 93, 48 },
			{ 92, 49 }, { 59, 51 }, { 39, 52 }, { 96, 53 }, { 44, 54 }, { 46, 55 }, { 47, 56 }, { 280, 57 },
			{ 283, 70 }, { 281, 71 }, { 284, 72 }, { 260, 73 }, { 268, 74 }, { 266, 75 }, { 261, 76 }, { 269, 77 },
			{ 267, 78 }, { 262, 79 }, { 263, 80 }, { 264, 81 }, { 265, 82 }, { 282, 83 }, { 162, 100 }, { 348, 118 } });
	}

	@Test
	void modifiers() {
		table(new int[][] { { 341, 224 }, { 340, 225 }, { 342, 226 }, { 343, 227 }, { 345, 228 }, { 344, 229 }, { 346, 230 }, { 347, 231 } });
	}

	@Test
	void matchesMinecraftConstants() {
		assertEquals(26, GlfwKeys.toScancode('W'));
		assertEquals(44, GlfwKeys.toScancode(32));
		assertEquals(225, GlfwKeys.toScancode(340));
		assertEquals(41, GlfwKeys.toScancode(256));
		assertEquals(60, GlfwKeys.toScancode(292));
	}

	@Test
	void unmappedKeysReturnMinusOne() {
		for (int k : new int[] { -1, 0, -100, 314, 161, 1000, Integer.MAX_VALUE, Integer.MIN_VALUE, 64, 91 + 100, 337, 349 }) {
			assertEquals(-1, GlfwKeys.toScancode(k), "glfw key " + k);
		}
	}

	@Test
	void mouseButtonsMapToSdl() {
		int[] expected = { 1, 3, 2, 4, 5, 6, 7, 8 };
		for (int b = 0; b < expected.length; b++) {
			assertEquals(expected[b], GlfwKeys.toSdlButton(b), "glfw button " + b);
		}
	}

	@Test
	void unknownMouseButtonsReturnMinusOne() {
		for (int b : new int[] { -1, 8, 100, Integer.MAX_VALUE }) {
			assertEquals(-1, GlfwKeys.toSdlButton(b), "glfw button " + b);
		}
	}
}
