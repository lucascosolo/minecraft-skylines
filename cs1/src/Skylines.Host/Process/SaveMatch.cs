using System;
using System.Collections.Generic;

namespace Skylines.Host
{
    /// <summary>One save as listed by the game: package (file) name, asset name, city name and save time.</summary>
    public sealed class SaveCandidate
    {
        /// <summary>Package name; for a local save the .crp file name without extension.</summary>
        public string PackageName;

        /// <summary>Asset name, the name the Load panel lists.</summary>
        public string AssetName;

        /// <summary>SaveGameMetaData.cityName.</summary>
        public string CityName;

        /// <summary>SaveGameMetaData.timeStamp.</summary>
        public DateTime Timestamp;
    }

    /// <summary>Picks the save launch.cfg <c>autoload</c> names. Pure logic: no Unity.</summary>
    public static class SaveMatch
    {
        /// <summary>
        /// Index of the save whose package name, else asset name, else city name equals <paramref name="wanted"/>
        /// (trimmed, case-insensitive); the first of those tiers with a match decides, the newest save in it wins.
        /// -1 for an empty name or no match.
        /// </summary>
        public static int Pick(IList<SaveCandidate> saves, string wanted)
        {
            if (wanted == null || wanted.Trim().Length == 0) return -1;
            wanted = wanted.Trim();
            for (int tier = 0; tier < 3; tier++)
            {
                int best = -1;
                for (int i = 0; i < saves.Count; i++)
                {
                    SaveCandidate s = saves[i];
                    string name = tier == 0 ? s.PackageName : tier == 1 ? s.AssetName : s.CityName;
                    if (name == null || !string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)) continue;
                    if (best < 0 || s.Timestamp > saves[best].Timestamp) best = i;
                }
                if (best >= 0) return best;
            }
            return -1;
        }
    }
}
