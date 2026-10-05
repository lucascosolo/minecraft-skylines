using System;

namespace Skylines.Host
{
    /// <summary>launch.cfg <c>autoload</c>: a save name, or <c>new:&lt;map&gt;</c> for a new game on that map. Pure logic: no Unity.</summary>
    public static class AutoloadTarget
    {
        /// <summary>Prefix of a new-game value (case-insensitive).</summary>
        public const string NewGamePrefix = "new:";

        /// <summary>The map name of a <c>new:</c> value (trimmed; "" when blank), or null when the value names a save.</summary>
        public static string NewGameMap(string autoload)
        {
            string v = (autoload ?? "").Trim();
            if (!v.StartsWith(NewGamePrefix, StringComparison.OrdinalIgnoreCase)) return null;
            return v.Substring(NewGamePrefix.Length).Trim();
        }
    }
}
