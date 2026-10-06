using System.Globalization;

namespace MinecraftSkylines.Mod.Render
{
    /// <summary>
    /// Live-switchable camera clip planes for Minecraft mode (launch.cfg <c>clip_preset</c>, Ctrl+Shift+F).
    /// A smaller far/near ratio gives the depth buffer more precision, which stops distant surfaces flickering.
    /// 0 near 0.1 with the city camera's far plane; 1 to 4 pair a larger near with a shorter far.
    /// Pure logic, no Unity.
    /// </summary>
    public static class ClipPreset
    {
        /// <summary>Number of presets; valid values are 0 to Count - 1.</summary>
        public const int Count = 5;

        /// <summary>Preset used until the owner picks one.</summary>
        public const int Default = 2;

        private static readonly float[] s_near = { 0.1f, 0.15f, 0.25f, 0.4f, 0.6f };
        private static readonly float[] s_far = { 0f, 4000f, 3000f, 2000f, 1500f };

        /// <summary>True for 0 to Count - 1.</summary>
        public static bool IsValid(int preset)
        {
            return preset >= 0 && preset < Count;
        }

        /// <summary>The preset after <paramref name="preset"/>, wrapping; an invalid input gives <see cref="Default"/>.</summary>
        public static int Next(int preset)
        {
            return IsValid(preset) ? (preset + 1) % Count : Default;
        }

        /// <summary>Near clip plane in metres; an invalid preset is treated as <see cref="Default"/>.</summary>
        public static float Near(int preset)
        {
            return s_near[IsValid(preset) ? preset : Default];
        }

        /// <summary>Far clip plane in metres; preset 0 returns <paramref name="cityFar"/> (the camera's far plane at entry).</summary>
        public static float Far(int preset, float cityFar)
        {
            int p = IsValid(preset) ? preset : Default;
            return p == 0 ? cityFar : s_far[p];
        }

        /// <summary>The status box text, e.g. "clip: near 0.25 m, far 3000 m".</summary>
        public static string Describe(int preset, float cityFar)
        {
            return "clip: near " + Near(preset).ToString("0.##", CultureInfo.InvariantCulture)
                + " m, far " + Far(preset, cityFar).ToString("0", CultureInfo.InvariantCulture) + " m";
        }
    }
}
