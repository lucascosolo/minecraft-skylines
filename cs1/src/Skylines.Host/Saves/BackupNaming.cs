using System;
using System.Globalization;

namespace Skylines.Host.Saves
{
    /// <summary>Names for backup saves: "&lt;city&gt; (&lt;label&gt;) yyyy-MM-dd HHmm", made unique with " (2)", " (3)", ...</summary>
    public static class BackupNaming
    {
        /// <summary>Most suffixes tried before giving up.</summary>
        public const int MaxAttempts = 1000;

        /// <summary>The name before uniqueness; an empty or blank city name becomes "City".</summary>
        public static string BaseName(string city, string label, DateTime now)
        {
            string c = city == null || city.Trim().Length == 0 ? "City" : city.Trim();
            return c + " (" + label + ") " + now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The first of <paramref name="baseName"/>, "baseName (2)", "baseName (3)", ... for which
        /// <paramref name="taken"/> is false; throws <see cref="InvalidOperationException"/> after <see cref="MaxAttempts"/>.
        /// </summary>
        public static string Unique(string baseName, Func<string, bool> taken)
        {
            for (int n = 1; n <= MaxAttempts; n++)
            {
                string name = n == 1 ? baseName : baseName + " (" + n + ")";
                if (!taken(name)) return name;
            }
            throw new InvalidOperationException("no free backup name for '" + baseName + "'");
        }
    }
}
