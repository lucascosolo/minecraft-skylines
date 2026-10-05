namespace Skylines.Core.Input
{
    /// <summary>Maps Unity <c>KeyCode</c> integer values to GLFW key and mouse-button codes.</summary>
    public static class GlfwKeyMap
    {
        /// <summary>GLFW key code for a Unity key code, or -1 when unmapped.</summary>
        public static int FromUnityKeyCode(int u)
        {
            if (u >= 97 && u <= 122) return u - 32;       // a..z -> A..Z
            if (u >= 48 && u <= 57) return u;             // Alpha0..9
            if (u >= 282 && u <= 293) return u + 8;       // F1..F12
            if (u >= 256 && u <= 265) return u + 64;      // Keypad0..9
            switch (u)
            {
                case 32: return 32;     // Space
                case 13: return 257;    // Return
                case 27: return 256;    // Escape
                case 9: return 258;     // Tab
                case 8: return 259;     // Backspace
                case 273: return 265;   // UpArrow
                case 274: return 264;   // DownArrow
                case 275: return 262;   // RightArrow
                case 276: return 263;   // LeftArrow
                case 304: return 340;   // LeftShift
                case 303: return 344;   // RightShift
                case 306: return 341;   // LeftControl
                case 305: return 345;   // RightControl
                case 308: return 342;   // LeftAlt
                case 307: return 346;   // RightAlt
                case 277: return 260;   // Insert
                case 127: return 261;   // Delete
                case 278: return 268;   // Home
                case 279: return 269;   // End
                case 280: return 266;   // PageUp
                case 281: return 267;   // PageDown
                case 45: return 45;     // Minus
                case 61: return 61;     // Equals
                case 91: return 91;     // LeftBracket
                case 93: return 93;     // RightBracket
                case 59: return 59;     // Semicolon
                case 39: return 39;     // Quote
                case 44: return 44;     // Comma
                case 46: return 46;     // Period
                case 47: return 47;     // Slash
                case 92: return 92;     // Backslash
                case 96: return 96;     // BackQuote
                default: return -1;
            }
        }

        /// <summary>GLFW mouse button (0 left, 1 right, 2 middle) for Mouse0..2, else -1.</summary>
        public static int MouseButtonFromUnityKeyCode(int u)
        {
            return u >= 323 && u <= 325 ? u - 323 : -1;
        }
    }
}
