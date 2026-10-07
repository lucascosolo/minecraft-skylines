// Adapted from SkyCraft (https://github.com/chasmlol/SkyCraft), MIT License, Copyright (c) 2026 chasmlol.
// See minecraft/THIRD-PARTY-NOTICES.md for the full licence text.
package dev.mcskylines.player;

import dev.mcskylines.protocol.Input;
import net.minecraft.client.Minecraft;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonInfo;
import org.lwjgl.sdl.SDLKeyboard;

/**
 * Replays the host's INPUT events into Minecraft's own handlers as if its (hidden) window had focus, and keeps a
 * virtual keyboard so InputConstants.isKeyDown() answers from it. Port of SkyCraft's InputBridge; codes arrive as
 * GLFW codes and are mapped to the SDL3 codes Minecraft 26.3 uses.
 */
public final class InputReplay {
	private static final boolean[] KEYS = new boolean[512];
	private static final boolean[] BUTTONS = new boolean[9];
	private static int modifiers;
	private static int hostWidth, hostHeight;
	private static double cursorX, cursorY;

	private InputReplay() {
	}

	/** VIEWPORT: cursor positions arrive in host pixels. */
	public static void setHostViewport(int width, int height) {
		hostWidth = width;
		hostHeight = height;
	}

	public static boolean isKeyDown(int scancode) {
		return scancode >= 0 && scancode < KEYS.length && KEYS[scancode];
	}

	static void dispatch(Minecraft mc, Input.Event e) {
		long handle = mc.getWindow().handle();
		boolean down = e.action() == 1;
		switch (e.kind()) {
			case Input.KEY -> {
				int sc = GlfwKeys.toScancode(e.code());
				if (sc > 0 && sc < KEYS.length) {
					key(mc, handle, sc, down);
				}
			}
			case Input.MOUSE_BUTTON -> {
				int button = GlfwKeys.toSdlButton(e.code());
				if (button > 0) {
					BUTTONS[button] = down;
					mc.mouseHandler.onButton(handle, new MouseButtonInfo(button, modifiers), down ? 1 : 0);
				}
			}
			case Input.SCROLL -> mc.mouseHandler.onScroll(handle, 0.0, e.code() / 120.0);
			case Input.TEXT -> {
				if (mc.gui.screen() != null && Character.isValidCodePoint(e.code())) {
					mc.keyboardHandler.textInput(handle, new String(Character.toChars(e.code())));
				}
			}
			case Input.RELEASE_ALL -> releaseAll(mc);
			case Input.CURSOR -> {
				if (hostWidth > 0 && hostHeight > 0) {
					double x = e.cursorX() * (double) mc.getWindow().getScreenWidth() / hostWidth;
					double y = e.cursorY() * (double) mc.getWindow().getScreenHeight() / hostHeight;
					mc.mouseHandler.onMove(handle, x, y, x - cursorX, y - cursorY);
					cursorX = x;
					cursorY = y;
				}
			}
			default -> {
			}
		}
	}

	private static void key(Minecraft mc, long handle, int scancode, boolean down) {
		boolean wasDown = KEYS[scancode];
		KEYS[scancode] = down;
		updateModifiers();
		int action = down ? (wasDown ? -1 : 1) : 0; // -1 = repeat
		if (action == 1 && scancode == 7 && (modifiers & 0x00C0) != 0 && (modifiers & 0x0003) != 0) {
			try {
				dev.mcskylines.debug.ShadowDump.run(mc);
			} catch (RuntimeException ex) {
				org.slf4j.LoggerFactory.getLogger("mcskylines").warn("[MinecraftSkylines] shadow dump failed", ex);
			}
		}
		int keycode = SDLKeyboard.SDL_GetKeyFromScancode(scancode, (short) modifiers, true);
		mc.keyboardHandler.keyPress(handle, action, new KeyEvent(scancode, keycode, modifiers));
	}

	private static void updateModifiers() {
		int m = 0;
		if (KEYS[225]) m |= 0x0001; // SDL_KMOD_LSHIFT
		if (KEYS[229]) m |= 0x0002; // SDL_KMOD_RSHIFT
		if (KEYS[224]) m |= 0x0040; // SDL_KMOD_LCTRL
		if (KEYS[228]) m |= 0x0080; // SDL_KMOD_RCTRL
		if (KEYS[226]) m |= 0x0100; // SDL_KMOD_LALT
		if (KEYS[230]) m |= 0x0200; // SDL_KMOD_RALT
		modifiers = m;
	}

	/** Lifts every key and button the host left held (EXIT_PLAYER_MODE, link loss, RELEASE_ALL). */
	static void releaseAll(Minecraft mc) {
		long handle = mc.getWindow().handle();
		for (int sc = 0; sc < KEYS.length; sc++) {
			if (KEYS[sc]) {
				KEYS[sc] = false;
				updateModifiers();
				mc.keyboardHandler.keyPress(handle, 0, new KeyEvent(sc, SDLKeyboard.SDL_GetKeyFromScancode(sc, (short) 0, true), modifiers));
			}
		}
		for (int button = 1; button < BUTTONS.length; button++) {
			if (BUTTONS[button]) {
				BUTTONS[button] = false;
				mc.mouseHandler.onButton(handle, new MouseButtonInfo(button, 0), 0);
			}
		}
	}
}
