using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Skylines.Host
{
    /// <summary>
    /// Starts one configured command as a hidden background child, appends its stdout and stderr to a
    /// log file, and reports on that child only. It never kills or signals anything (the companion
    /// quits itself) and never looks up another process.
    /// Linux only for now: the child runs through <c>/bin/sh -c</c> so the redirect and <c>exec</c>
    /// work. Windows and macOS need their own branch in <see cref="BuildStartInfo"/> later.
    /// </summary>
    public sealed class CompanionProcess
    {
        /// <summary>
        /// Constant script: <c>$1</c> is the log file, the rest ("$@" after the shift) is the command and
        /// its arguments. Configured values only ever arrive as positional parameters, never inside this text.
        /// </summary>
        public const string ShellScript = "log=$1; shift; exec \"$@\" >>\"$log\" 2>&1";

        private Process _process;

        /// <summary>Environment variables removed for the child: Steam injects its overlay and runtime libraries, which can break Java.</summary>
        private static readonly string[] s_scrubbedEnv = { "LD_PRELOAD", "LD_LIBRARY_PATH" };

        /// <summary>Log file the child appends to.</summary>
        public string LogPath { get; private set; }

        /// <summary>UTC time <see cref="Start"/> succeeded; null before.</summary>
        public DateTime? StartedAt { get; private set; }

        /// <summary>True while the child we started has not exited.</summary>
        public bool IsRunning
        {
            get
            {
                try { return _process != null && !_process.HasExited; }
                catch (Exception) { return false; }
            }
        }

        /// <summary>Exit code once the child has exited; null while running or never started.</summary>
        public int? ExitCode
        {
            get
            {
                try { return _process != null && _process.HasExited ? (int?)_process.ExitCode : null; }
                catch (Exception) { return null; }
            }
        }

        /// <summary>Creates an idle companion that will log to <paramref name="logPath"/>.</summary>
        public CompanionProcess(string logPath)
        {
            LogPath = logPath;
        }

        /// <summary>Starts the child. Returns null on success, else an error message. Never throws; starts at most once.</summary>
        public string Start(string command, IList<string> args, string workingDir, IDictionary<string, string> env)
        {
            if (_process != null)
            {
                return "already started";
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(LogPath)));
                File.AppendAllText(LogPath, "==== companion start " + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + ": " + command + " ====\n");
                _process = Process.Start(BuildStartInfo(command, args, workingDir, env, LogPath));
                StartedAt = DateTime.UtcNow;
                return _process == null ? "process did not start" : null;
            }
            catch (Exception e)
            {
                _process = null;
                return e.Message;
            }
        }

        /// <summary>The argv handed to <c>/bin/sh</c>: <c>-c script sh logPath command args...</c> (each configured value one element, verbatim).</summary>
        public static string[] BuildShellArgv(string command, IList<string> args, string logPath)
        {
            var argv = new List<string> { "-c", ShellScript, "sh", logPath, command };
            if (args != null) argv.AddRange(args);
            return argv.ToArray();
        }

        /// <summary>Builds the start info: no shell window, working directory, scrubbed and extra environment.</summary>
        public static ProcessStartInfo BuildStartInfo(string command, IList<string> args, string workingDir, IDictionary<string, string> env, string logPath)
        {
            var psi = new ProcessStartInfo("/bin/sh");
            psi.Arguments = JoinArguments(BuildShellArgv(command, args, logPath));
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            if (!string.IsNullOrEmpty(workingDir)) psi.WorkingDirectory = workingDir;
            foreach (string name in s_scrubbedEnv) psi.EnvironmentVariables.Remove(name);
            if (env != null)
            {
                foreach (KeyValuePair<string, string> kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
            }
            return psi;
        }

        /// <summary>Joins argv into the single string <see cref="ProcessStartInfo.Arguments"/> wants, quoting every element.</summary>
        public static string JoinArguments(string[] argv)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < argv.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(QuoteArgument(argv[i]));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Quotes one argument as double quotes with backslashes before a quote (or the closing quote)
        /// doubled and quotes escaped: the rule .NET and Mono both use to split a command line. No shell
        /// is involved at this level, so <c>$</c>, backticks and <c>;</c> are inert.
        /// </summary>
        public static string QuoteArgument(string arg)
        {
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char ch in arg)
            {
                if (ch == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (ch == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1).Append('"');
                }
                else
                {
                    sb.Append('\\', backslashes).Append(ch);
                }
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2).Append('"');
            return sb.ToString();
        }
    }
}
