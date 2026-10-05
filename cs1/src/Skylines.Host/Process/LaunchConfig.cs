using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Skylines.Host
{
    /// <summary>When the companion is started ahead of Ctrl+Shift+M (launch.cfg <c>prewarm</c>).</summary>
    public enum PrewarmMode
    {
        /// <summary>When the mod is enabled at the main menu (<c>game_start</c>, the default).</summary>
        GameStart,

        /// <summary>When a city finishes loading (<c>city_load</c>).</summary>
        CityLoad,

        /// <summary>Only on Ctrl+Shift+M (<c>off</c>).</summary>
        Off,
    }

    /// <summary>When the in-game self-test runs by itself (launch.cfg <c>selftest</c>).</summary>
    public enum SelfTestMode
    {
        /// <summary>Only on Ctrl+Shift+T (<c>off</c>, the default).</summary>
        Off,

        /// <summary>Once after each city load (<c>city_load</c>).</summary>
        CityLoad,
    }

    /// <summary>
    /// Parsed <c>launch.cfg</c>: a plain <c>key = value</c> file (<c>#</c> starts a comment line).
    /// Keys: <c>command</c>, <c>args</c> (space separated, double quotes group, backslash escapes a quote
    /// or backslash inside quotes), <c>working_dir</c>, <c>env.NAME</c> (repeatable) and
    /// <c>connect_timeout_seconds</c> (default 180), <c>prewarm</c> (<c>game_start</c> default, <c>city_load</c>, <c>off</c>), <c>selftest</c> (<c>off</c> default, <c>city_load</c>),
    /// <c>autoload</c> (a save name to load from the main menu once per game start; empty default = off) and
    /// <c>selftest_quit</c> (<c>false</c> default, <c>true</c>: quit the game after the self-test report). Pure logic: no Unity, no process start.
    /// </summary>
    public sealed class LaunchConfig
    {
        /// <summary>Default for <c>connect_timeout_seconds</c>.</summary>
        public const int DefaultConnectTimeoutSeconds = 180;

        /// <summary>Program to run.</summary>
        public string Command = "";

        /// <summary>Arguments, already split.</summary>
        public readonly List<string> Args = new List<string>();

        /// <summary>Working directory; empty means the game's own.</summary>
        public string WorkingDir = "";

        /// <summary>Extra environment variables for the child.</summary>
        public readonly Dictionary<string, string> Env = new Dictionary<string, string>();

        /// <summary>Seconds to wait for the companion to connect.</summary>
        public int ConnectTimeoutSeconds = DefaultConnectTimeoutSeconds;

        /// <summary>When to start the companion before the shortcut is pressed.</summary>
        public PrewarmMode Prewarm = PrewarmMode.GameStart;

        /// <summary>When the self-test starts by itself.</summary>
        public SelfTestMode SelfTest = SelfTestMode.Off;

        /// <summary>Save to load from the main menu once per game start; empty means off.</summary>
        public string Autoload = "";

        /// <summary>Quit the game after the self-test writes its report (unattended runs).</summary>
        public bool SelfTestQuit;

        /// <summary>Material variant for drawn block meshes, 0-3 (an in-game experiment knob).</summary>
        public int BlockMaterial;

        /// <summary>Problems found while parsing (unknown keys, bad numbers, missing command).</summary>
        public readonly List<string> Problems = new List<string>();

        /// <summary>True when a command is configured.</summary>
        public bool IsUsable { get { return Command.Length > 0; } }

        /// <summary>Reads and parses a file; a read failure becomes a problem, never an exception.</summary>
        public static LaunchConfig Load(string path)
        {
            try
            {
                return Parse(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                var c = new LaunchConfig();
                c.Problems.Add("cannot read " + path + ": " + e.Message);
                return c;
            }
        }

        /// <summary>Parses file contents.</summary>
        public static LaunchConfig Parse(string text)
        {
            var c = new LaunchConfig();
            string[] lines = (text ?? "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    c.Problems.Add("line " + (i + 1) + ": expected key = value");
                    continue;
                }
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                c.Set(key, value, i + 1);
            }
            if (!c.IsUsable)
            {
                c.Problems.Add("no 'command' set");
            }
            return c;
        }

        /// <summary>Splits on spaces; double quotes group words; inside quotes a backslash escapes a quote or backslash.</summary>
        public static List<string> SplitArgs(string line)
        {
            var result = new List<string>();
            var cur = new StringBuilder();
            bool inWord = false, quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (quoted && ch == '\\' && i + 1 < line.Length && (line[i + 1] == '"' || line[i + 1] == '\\'))
                {
                    cur.Append(line[++i]);
                }
                else if (ch == '"')
                {
                    quoted = !quoted;
                    inWord = true;
                }
                else if (!quoted && (ch == ' ' || ch == '\t'))
                {
                    if (inWord) { result.Add(cur.ToString()); cur.Length = 0; inWord = false; }
                }
                else
                {
                    cur.Append(ch);
                    inWord = true;
                }
            }
            if (inWord) result.Add(cur.ToString());
            return result;
        }

        private void Set(string key, string value, int lineNo)
        {
            switch (key)
            {
                case "command": Command = value; break;
                case "args": Args.AddRange(SplitArgs(value)); break;
                case "working_dir": WorkingDir = value; break;
                case "connect_timeout_seconds":
                    int s;
                    if (int.TryParse(value, out s) && s > 0) ConnectTimeoutSeconds = s;
                    else Problems.Add("line " + lineNo + ": connect_timeout_seconds must be a positive integer");
                    break;
                case "prewarm":
                    switch (value.ToLowerInvariant())
                    {
                        case "game_start": Prewarm = PrewarmMode.GameStart; break;
                        case "city_load": Prewarm = PrewarmMode.CityLoad; break;
                        case "off": Prewarm = PrewarmMode.Off; break;
                        default: Problems.Add("line " + lineNo + ": prewarm must be game_start, city_load or off"); break;
                    }
                    break;
                case "selftest":
                    switch (value.ToLowerInvariant())
                    {
                        case "off": SelfTest = SelfTestMode.Off; break;
                        case "city_load": SelfTest = SelfTestMode.CityLoad; break;
                        default: Problems.Add("line " + lineNo + ": selftest must be off or city_load"); break;
                    }
                    break;
                case "autoload": Autoload = value; break;
                case "selftest_quit":
                    switch (value.ToLowerInvariant())
                    {
                        case "true": SelfTestQuit = true; break;
                        case "false": SelfTestQuit = false; break;
                        default: Problems.Add("line " + lineNo + ": selftest_quit must be true or false"); break;
                    }
                    break;
                case "block_material":
                    {
                        int m;
                        if (int.TryParse(value, out m) && m >= 0 && m <= 3) BlockMaterial = m;
                        else Problems.Add("line " + lineNo + ": block_material must be 0, 1, 2 or 3");
                    }
                    break;
                default:
                    if (key.StartsWith("env.", StringComparison.Ordinal) && key.Length > 4) Env[key.Substring(4)] = value;
                    else Problems.Add("line " + lineNo + ": unknown key '" + key + "'");
                    break;
            }
        }
    }
}
