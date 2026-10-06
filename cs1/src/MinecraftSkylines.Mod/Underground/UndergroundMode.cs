namespace MinecraftSkylines.Mod.Underground
{
    /// <summary>
    /// The live-switchable ways road tunnels and their cars are made visible in Minecraft mode (the owner picks one).
    /// Pure logic, no Unity: <see cref="UndergroundRenderer"/> applies the answers.
    /// 0 off; 1 main camera always draws the MetroTunnels layer; 2 same, only while the eye is underground;
    /// 3 the game's own underground view (TransportManager.TunnelsVisible), only while the eye is underground.
    /// </summary>
    public static class UndergroundMode
    {
        /// <summary>Number of modes; valid values are 0 to Count - 1.</summary>
        public const int Count = 4;

        /// <summary>Mode used until the owner picks one.</summary>
        public const int Default = 2;

        /// <summary>The eye counts as underground when this far (metres) below the terrain.</summary>
        public const float Margin = 0.5f;

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

        /// <summary>True when the eye is more than <see cref="Margin"/> below the terrain height at its position.</summary>
        public static bool IsUnderground(float eyeY, float terrainY)
        {
            return eyeY < terrainY - Margin;
        }

        /// <summary>True when the main camera should include the MetroTunnels layer.</summary>
        public static bool WantsLayer(int mode, bool underground)
        {
            return mode == 1 || (mode == 2 && underground);
        }

        /// <summary>True when TransportManager.TunnelsVisible should be set.</summary>
        public static bool WantsTunnelsVisible(int mode, bool underground)
        {
            return mode == 3 && underground;
        }

        /// <summary>One-line description for the log and the status box.</summary>
        public static string Describe(int mode)
        {
            switch (mode)
            {
                case 0: return "0 off";
                case 1: return "1 main camera always draws MetroTunnels";
                case 2: return "2 main camera draws MetroTunnels while underground";
                case 3: return "3 game underground view (TunnelsVisible) while underground";
                default: return mode + " (invalid)";
            }
        }
    }
}
