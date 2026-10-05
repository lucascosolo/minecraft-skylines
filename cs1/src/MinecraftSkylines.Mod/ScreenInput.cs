using System;

namespace MinecraftSkylines.Mod
{
    /// <summary>Pure helpers for driving a Minecraft screen from CS1 (no Unity, unit-tested on .NET 10).</summary>
    internal static class ScreenInput
    {
        /// <summary>Unity's Input.mousePosition (origin bottom-left) to host pixels with origin top-left, clamped to the screen.</summary>
        public static void ToHostPixels(float unityX, float unityY, int screenWidth, int screenHeight, out int x, out int y)
        {
            x = Clamp((int)Math.Floor(unityX), screenWidth);
            y = Clamp(screenHeight - 1 - (int)Math.Floor(unityY), screenHeight);
        }

        private static int Clamp(int v, int size)
        {
            return v < 0 ? 0 : v >= size ? Math.Max(0, size - 1) : v;
        }
    }

    /// <summary>Where an Esc press goes.</summary>
    internal enum EscapeRoute { ExitMode, ToGuest }

    /// <summary>
    /// Esc leaves Minecraft mode unless a Minecraft screen is open, in which case the press goes to
    /// Minecraft (which closes the screen). The decision is made once, at the press, and the release
    /// follows the press: a forwarded press always gets its release forwarded, even if the screen has
    /// closed in between, so Minecraft never sees a held Esc.
    /// </summary>
    internal sealed class EscapeRouter
    {
        public bool Holding { get; private set; }

        public EscapeRoute Down(bool screenOpen)
        {
            if (!screenOpen) return EscapeRoute.ExitMode;
            Holding = true;
            return EscapeRoute.ToGuest;
        }

        public bool Up()
        {
            bool was = Holding;
            Holding = false;
            return was;
        }

        public void Reset()
        {
            Holding = false;
        }
    }
}
