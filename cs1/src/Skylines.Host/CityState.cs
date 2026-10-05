using System;

namespace Skylines.Host
{
    /// <summary>
    /// A main-thread snapshot of the game's state that integrations report to their peer.
    /// <see cref="InCity"/> is driven by the mod's LoadingExtension (call <see cref="SetInCity"/>).
    /// </summary>
    public sealed class CityState : IEquatable<CityState>
    {
        private static bool s_inCity;

        /// <summary>A city (or map/scenario) is loaded and playable.</summary>
        public bool InCity;

        /// <summary>The game is loading a level.</summary>
        public bool Loading;

        /// <summary>The simulation is paused by the player.</summary>
        public bool SimulationPaused;

        /// <summary>The loaded city's name, empty outside a city.</summary>
        public string CityName = "";

        /// <summary>Called by the mod's LoadingExtension: true from OnLevelLoaded, false from OnLevelUnloading.</summary>
        public static void SetInCity(bool inCity)
        {
            s_inCity = inCity;
        }

        /// <summary>The game's version string, e.g. "1.21.1-f5".</summary>
        public static string GameVersion
        {
            get { return BuildConfig.applicationVersion; }
        }

        /// <summary>Reads the current state. Main thread only.</summary>
        public static CityState Capture()
        {
            var s = new CityState();
            s.InCity = s_inCity;
            s.Loading = LoadingManager.exists && LoadingManager.instance.m_currentlyLoading;
            if (s.InCity && SimulationManager.exists)
            {
                SimulationManager sim = SimulationManager.instance;
                s.SimulationPaused = sim.SimulationPaused;
                if (sim.m_metaData != null && sim.m_metaData.m_CityName != null)
                {
                    s.CityName = sim.m_metaData.m_CityName;
                }
            }
            return s;
        }

        /// <inheritdoc />
        public bool Equals(CityState other)
        {
            return other != null && InCity == other.InCity && Loading == other.Loading
                && SimulationPaused == other.SimulationPaused && CityName == other.CityName;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return Equals(obj as CityState);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (CityName ?? "").GetHashCode() ^ (InCity ? 1 : 0) ^ (Loading ? 2 : 0) ^ (SimulationPaused ? 4 : 0);
        }
    }
}
