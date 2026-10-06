namespace Skylines.Host.Overlay
{
    /// <summary>
    /// The live-switchable ways the Minecraft overlay is drawn (an in-game brightness experiment; the owner picks one).
    /// Pure logic, no Unity: <see cref="OverlayPresenter"/> applies the answers.
    /// </summary>
    public static class OverlayMode
    {
        /// <summary>Number of modes; valid values are 0 to Count - 1.</summary>
        public const int Count = 5;

        /// <summary>Mode used until the owner picks one.</summary>
        public const int Default = 3; // premultiplied, linear texture: the owner picked 3 (or 4) in game, 2026-10-06

        /// <summary>True for 0 to Count - 1.</summary>
        public static bool IsValid(int mode)
        {
            return mode >= 0 && mode < Count;
        }

        /// <summary>The mode after <paramref name="mode"/>, wrapping; an invalid input gives <see cref="Default"/>.</summary>
        public static int Next(int mode)
        {
            return IsValid(mode) ? (mode + 1) % Count : Default;
        }

        /// <summary>True when the mode draws with the premultiplied particle shader (modes 0, 1 and 3).</summary>
        public static bool UsesPremultiplyShader(int mode)
        {
            return mode == 0 || mode == 1 || mode == 3;
        }

        /// <summary>True when the texture is created with Unity's linear flag set (modes 3 and 4); the default texture is not linear.</summary>
        public static bool LinearTexture(int mode)
        {
            return mode == 3 || mode == 4;
        }

        /// <summary>Grey level of the colour passed to Graphics.DrawTexture: 0.5 is its documented neutral value, 1 is white.</summary>
        public static float DrawColorGrey(int mode)
        {
            return mode == 1 ? 1f : 0.5f;
        }

        /// <summary>One-line description for the log and the status box.</summary>
        public static string Describe(int mode)
        {
            switch (mode)
            {
                case 0: return "0 premultiply shader, colour 0.5 (Unity's neutral)";
                case 1: return "1 premultiply shader, colour white";
                case 2: return "2 straight alpha GUI.DrawTextureWithTexCoords";
                case 3: return "3 premultiply shader, colour 0.5, linear texture";
                case 4: return "4 straight alpha, linear texture";
                default: return mode + " (invalid)";
            }
        }
    }
}
