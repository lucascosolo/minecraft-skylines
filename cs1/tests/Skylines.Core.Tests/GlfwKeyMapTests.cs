// Key code values are taken from two sources, not from memory:
//  * Unity: UnityEngine.KeyCode, decompiled with ilspycmd from
//    ~/.cache/minecraft-skylines/refs/cs1/Managed/UnityEngine.dll (CS1's Unity 5.x).
//  * GLFW: constants of org.lwjgl.glfw.GLFW (lwjgl-glfw 3.4.3, the version the Gradle cache holds for
//    lwjgl-*; the glfw jar itself was fetched from Maven Central) via `javap -constants`.
// Unity letters are lowercase ASCII (A = 97 .. Z = 122), GLFW letters are uppercase (65..90).
using System.Collections.Generic;
using Skylines.Core.Input;
using Xunit;

namespace Skylines.Core.Tests
{
    public class GlfwKeyMapTests
    {
        public static IEnumerable<object[]> Pairs()
        {
            for (int i = 0; i < 26; i++) yield return new object[] { 97 + i, 65 + i };      // A..Z
            for (int i = 0; i < 10; i++) yield return new object[] { 48 + i, 48 + i };      // Alpha0..9
            for (int i = 0; i < 12; i++) yield return new object[] { 282 + i, 290 + i };    // F1..F12
            for (int i = 0; i < 10; i++) yield return new object[] { 256 + i, 320 + i };    // Keypad0..9
            yield return new object[] { 32, 32 };      // Space
            yield return new object[] { 13, 257 };     // Return -> ENTER
            yield return new object[] { 27, 256 };     // Escape
            yield return new object[] { 9, 258 };      // Tab
            yield return new object[] { 8, 259 };      // Backspace
            yield return new object[] { 273, 265 };    // UpArrow -> UP
            yield return new object[] { 274, 264 };    // DownArrow -> DOWN
            yield return new object[] { 275, 262 };    // RightArrow -> RIGHT
            yield return new object[] { 276, 263 };    // LeftArrow -> LEFT
            yield return new object[] { 304, 340 };    // LeftShift
            yield return new object[] { 303, 344 };    // RightShift
            yield return new object[] { 306, 341 };    // LeftControl
            yield return new object[] { 305, 345 };    // RightControl
            yield return new object[] { 308, 342 };    // LeftAlt
            yield return new object[] { 307, 346 };    // RightAlt
            yield return new object[] { 277, 260 };    // Insert
            yield return new object[] { 127, 261 };    // Delete
            yield return new object[] { 278, 268 };    // Home
            yield return new object[] { 279, 269 };    // End
            yield return new object[] { 280, 266 };    // PageUp
            yield return new object[] { 281, 267 };    // PageDown
            yield return new object[] { 45, 45 };      // Minus
            yield return new object[] { 61, 61 };      // Equals -> EQUAL
            yield return new object[] { 91, 91 };      // LeftBracket
            yield return new object[] { 93, 93 };      // RightBracket
            yield return new object[] { 59, 59 };      // Semicolon
            yield return new object[] { 39, 39 };      // Quote -> APOSTROPHE
            yield return new object[] { 44, 44 };      // Comma
            yield return new object[] { 46, 46 };      // Period
            yield return new object[] { 47, 47 };      // Slash
            yield return new object[] { 92, 92 };      // Backslash
            yield return new object[] { 96, 96 };      // BackQuote -> GRAVE_ACCENT
        }

        [Theory]
        [MemberData(nameof(Pairs))]
        public void MapsUnityKeyCodeToGlfwKey(int unity, int glfw)
        {
            Assert.Equal(glfw, GlfwKeyMap.FromUnityKeyCode(unity));
        }

        [Theory]
        [InlineData(0)]            // KeyCode.None
        [InlineData(12)]           // Clear: not in the required set
        [InlineData(1000)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        [InlineData(323)]          // Mouse0 is a mouse button, not a key
        [InlineData(326)]          // Mouse3
        public void UnmappedKeyCodesReturnMinusOne(int unity)
        {
            Assert.Equal(-1, GlfwKeyMap.FromUnityKeyCode(unity));
        }

        [Theory]
        [InlineData(323, 0)]       // Mouse0
        [InlineData(324, 1)]       // Mouse1
        [InlineData(325, 2)]       // Mouse2
        [InlineData(326, -1)]      // Mouse3
        [InlineData(322, -1)]
        [InlineData(0, -1)]
        [InlineData(97, -1)]       // a key, not a button
        [InlineData(-1, -1)]
        public void MouseButtonFromUnityKeyCode(int unity, int expected)
        {
            Assert.Equal(expected, GlfwKeyMap.MouseButtonFromUnityKeyCode(unity));
        }
    }
}
