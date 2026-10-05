using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Skylines.Host
{
    /// <summary>
    /// Starts one configured command as a hidden background child, appends its stdout and stderr to a
    /// log file, and reports on that child only. It never kills or signals anything (the companion
    /// quits itself) and never looks up another process.
    /// The child always runs through <c>/bin/sh -c</c> (redirect, <c>cd</c>, <c>exec</c>). On Linux it is
    /// spawned with libc <c>posix_spawn</c>: the game's Mono 2.x <see cref="Process.Start(ProcessStartInfo)"/>
    /// threw a bare Win32Exception on the owner's machine. Elsewhere (macOS has no <c>libc.so.6</c>) and if
    /// libc cannot be bound, <see cref="Process.Start(ProcessStartInfo)"/> is used.
    /// </summary>
    public sealed class CompanionProcess
    {
        /// <summary>
        /// Constant script: <c>$1</c> is the log file, <c>$2</c> the working directory (empty: stay), the rest
        /// ("$@" after the shift) is the command and its arguments. Configured values only ever arrive as
        /// positional parameters, never inside this text.
        /// </summary>
        public const string ShellScript =
            "log=$1; dir=$2; shift 2; exec >>\"$log\" 2>&1; if [ -n \"$dir\" ]; then cd -- \"$dir\" || exit 126; fi; exec \"$@\"";

        private const string Shell = "/bin/sh";
        private const string Libc = "libc.so.6";
        private const int WNOHANG = 1;
        private const int ECHILD = 10;
        private const short POSIX_SPAWN_SETSIGDEF = 0x04;
        private const short POSIX_SPAWN_SETSIGMASK = 0x08;

        /// <summary>Environment variables removed for the child: Steam injects its overlay and runtime libraries, which can break Java.</summary>
        private static readonly string[] s_scrubbedEnv = { "LD_PRELOAD", "LD_LIBRARY_PATH" };

        private Process _process;
        private int _pid;
        private bool _reaped;
        private int? _exitCode;

        /// <summary>Log file the child appends to.</summary>
        public string LogPath { get; private set; }

        /// <summary>True when Linux <c>posix_spawn</c> is used instead of <see cref="Process.Start(ProcessStartInfo)"/>.</summary>
        public bool UsesPosixSpawn { get; private set; }

        /// <summary>UTC time <see cref="Start"/> succeeded; null before.</summary>
        public DateTime? StartedAt { get; private set; }

        /// <summary>Child pid after a successful start, else 0.</summary>
        public int Pid { get { return _pid; } }

        /// <summary>Full description of the last start failure (exception chain, native error, stack); null after success.</summary>
        public string LastErrorDetail { get; private set; }

        /// <summary>Set when the posix_spawn path could not bind libc and Process.Start was used instead.</summary>
        public string Notice { get; private set; }

        /// <summary>True while the child we started has not exited.</summary>
        public bool IsRunning
        {
            get
            {
                if (_process != null)
                {
                    try { return !_process.HasExited; }
                    catch (Exception) { return false; }
                }
                Poll();
                return _pid > 0 && !_reaped;
            }
        }

        /// <summary>Exit code once the child has exited (128+signal after a signal); null while running, never started, or unknown.</summary>
        public int? ExitCode
        {
            get
            {
                if (_process != null)
                {
                    try { return _process.HasExited ? (int?)_process.ExitCode : null; }
                    catch (Exception) { return null; }
                }
                Poll();
                return _exitCode;
            }
        }

        /// <summary>Creates an idle companion that will log to <paramref name="logPath"/>; posix_spawn when <see cref="IsLinux"/>.</summary>
        public CompanionProcess(string logPath) : this(logPath, IsLinux())
        {
        }

        /// <summary>Creates an idle companion; <paramref name="usePosixSpawn"/> selects the Linux libc path.</summary>
        public CompanionProcess(string logPath, bool usePosixSpawn)
        {
            LogPath = logPath;
            UsesPosixSpawn = usePosixSpawn;
        }

        /// <summary>Unix but not macOS (old Mono reports macOS as Unix too).</summary>
        public static bool IsLinux()
        {
            PlatformID p = Environment.OSVersion.Platform;
            return (p == PlatformID.Unix || (int)p == 128) && !Directory.Exists("/System/Library/CoreServices");
        }

        /// <summary>Starts the child. Returns null on success, else a short error message (details in <see cref="LastErrorDetail"/>). Never throws; starts at most once.</summary>
        public string Start(string command, IList<string> args, string workingDir, IDictionary<string, string> env)
        {
            if (_pid > 0)
            {
                return "already started";
            }
            try
            {
                if (!string.IsNullOrEmpty(workingDir) && !Directory.Exists(workingDir))
                {
                    LastErrorDetail = "working directory not found: " + workingDir;
                    return LastErrorDetail;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(LogPath)));
                File.AppendAllText(LogPath, "==== companion start " + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + ": " + command + " ====\n");
                if (UsesPosixSpawn)
                {
                    try
                    {
                        _pid = Spawn(BuildShellArgv(command, args, workingDir, LogPath), BuildEnvironment(env));
                    }
                    catch (Exception e)
                    {
                        if (!(e is DllNotFoundException) && !(e is EntryPointNotFoundException)) throw;
                        Notice = "posix_spawn unavailable, used Process.Start: " + Describe(e);
                    }
                }
                if (_pid == 0)
                {
                    _process = Process.Start(BuildStartInfo(command, args, workingDir, env, LogPath));
                    if (_process == null) throw new InvalidOperationException("Process.Start returned null");
                    _pid = _process.Id;
                }
                StartedAt = DateTime.UtcNow;
                LastErrorDetail = null;
                return null;
            }
            catch (Exception e)
            {
                _process = null;
                _pid = 0;
                LastErrorDetail = Describe(e);
                var w = e as Win32Exception;
                return e.GetType().Name + ": " + e.Message + (w != null ? " (native error " + w.NativeErrorCode + ")" : "");
            }
        }

        /// <summary>Type, message, Win32 native error and stack of an exception and each inner exception.</summary>
        public static string Describe(Exception e)
        {
            var sb = new StringBuilder();
            for (Exception x = e; x != null; x = x.InnerException)
            {
                if (x != e) sb.Append("caused by: ");
                sb.Append(x.GetType().FullName).Append(": ").Append(x.Message);
                var w = x as Win32Exception;
                if (w != null) sb.Append(" NativeErrorCode=").Append(w.NativeErrorCode);
                sb.Append('\n');
                if (x.StackTrace != null) sb.Append(x.StackTrace).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>The argv handed to <c>/bin/sh</c>: <c>-c script sh logPath workingDir command args...</c> (each configured value one element, verbatim).</summary>
        public static string[] BuildShellArgv(string command, IList<string> args, string workingDir, string logPath)
        {
            var argv = new List<string> { "-c", ShellScript, "sh", logPath, workingDir ?? "", command };
            if (args != null) argv.AddRange(args);
            return argv.ToArray();
        }

        /// <summary>The child's environment as NAME=value: ours without the scrubbed variables, plus <paramref name="extra"/>.</summary>
        public static string[] BuildEnvironment(IDictionary<string, string> extra)
        {
            var vars = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (DictionaryEntry kv in Environment.GetEnvironmentVariables())
            {
                vars[(string)kv.Key] = (string)kv.Value;
            }
            foreach (string name in s_scrubbedEnv) vars.Remove(name);
            if (extra != null)
            {
                foreach (KeyValuePair<string, string> kv in extra) vars[kv.Key] = kv.Value;
            }
            var result = new List<string>();
            foreach (KeyValuePair<string, string> kv in vars)
            {
                if (kv.Key.Length > 0 && kv.Key.IndexOf('=') < 0) result.Add(kv.Key + "=" + kv.Value);
            }
            return result.ToArray();
        }

        /// <summary>Start info for the non-Linux path: no shell window, working directory, scrubbed and extra environment.</summary>
        public static ProcessStartInfo BuildStartInfo(string command, IList<string> args, string workingDir, IDictionary<string, string> env, string logPath)
        {
            var psi = new ProcessStartInfo(Shell);
            psi.Arguments = JoinArguments(BuildShellArgv(command, args, workingDir, logPath));
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

        /// <summary>
        /// posix_spawn of /bin/sh with the given argv tail and envp. The child gets an empty signal mask and
        /// default signal dispositions (the game's threads may block or ignore signals). Frees everything it allocates.
        /// </summary>
        private static int Spawn(string[] shellArgv, string[] envp)
        {
            var owned = new List<IntPtr>();
            IntPtr attr = IntPtr.Zero;
            bool attrReady = false;
            try
            {
                var argv = new string[shellArgv.Length + 1];
                argv[0] = Shell;
                shellArgv.CopyTo(argv, 1);
                IntPtr argvPtr = NullTerminatedArray(argv, owned);
                IntPtr envPtr = NullTerminatedArray(envp, owned);

                attr = Alloc(1024, owned); // glibc posix_spawnattr_t is 336 bytes on x86_64
                IntPtr emptySet = Alloc(128, owned); // glibc sigset_t is 128 bytes
                IntPtr fullSet = Alloc(128, owned);
                Check(posix_spawnattr_init(attr), "posix_spawnattr_init");
                attrReady = true;
                if (sigemptyset(emptySet) != 0 || sigfillset(fullSet) != 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "sigset setup failed");
                Check(posix_spawnattr_setsigmask(attr, emptySet), "posix_spawnattr_setsigmask");
                Check(posix_spawnattr_setsigdefault(attr, fullSet), "posix_spawnattr_setsigdefault");
                Check(posix_spawnattr_setflags(attr, POSIX_SPAWN_SETSIGMASK | POSIX_SPAWN_SETSIGDEF), "posix_spawnattr_setflags");

                int pid;
                Check(posix_spawn(out pid, Encoding.UTF8.GetBytes(Shell + "\0"), IntPtr.Zero, attr, argvPtr, envPtr), "posix_spawn " + Shell);
                return pid;
            }
            finally
            {
                if (attrReady) posix_spawnattr_destroy(attr);
                foreach (IntPtr p in owned) Marshal.FreeHGlobal(p);
            }
        }

        private static void Check(int rc, string what)
        {
            if (rc != 0) throw new Win32Exception(rc, what + " failed: error " + rc);
        }

        private static IntPtr Alloc(int bytes, List<IntPtr> owned)
        {
            IntPtr p = Marshal.AllocHGlobal(bytes);
            owned.Add(p);
            for (int i = 0; i < bytes; i++) Marshal.WriteByte(p, i, 0);
            return p;
        }

        private static IntPtr NullTerminatedArray(string[] items, List<IntPtr> owned)
        {
            IntPtr array = Alloc(IntPtr.Size * (items.Length + 1), owned);
            for (int i = 0; i < items.Length; i++)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(items[i]);
                IntPtr s = Alloc(bytes.Length + 1, owned);
                Marshal.Copy(bytes, 0, s, bytes.Length);
                Marshal.WriteIntPtr(array, i * IntPtr.Size, s);
            }
            return array;
        }

        /// <summary>Non-blocking waitpid on our child: reaps it once it exits, so it never lingers as a zombie.</summary>
        private void Poll()
        {
            if (_pid <= 0 || _reaped || _process != null)
            {
                return;
            }
            try
            {
                int status;
                int r = waitpid(_pid, out status, WNOHANG);
                if (r == _pid)
                {
                    _reaped = true;
                    int sig = status & 0x7f;
                    _exitCode = sig == 0 ? (status >> 8) & 0xff : 128 + sig;
                }
                else if (r == -1 && Marshal.GetLastWin32Error() == ECHILD)
                {
                    _reaped = true; // someone else reaped it: gone, exit code unknown
                }
            }
            catch (Exception)
            {
                _reaped = true;
            }
        }

        [DllImport(Libc, SetLastError = true)]
        private static extern int waitpid(int pid, out int status, int options);

        [DllImport(Libc)]
        private static extern int posix_spawn(out int pid, byte[] path, IntPtr fileActions, IntPtr attr, IntPtr argv, IntPtr envp);

        [DllImport(Libc)]
        private static extern int posix_spawnattr_init(IntPtr attr);

        [DllImport(Libc)]
        private static extern int posix_spawnattr_destroy(IntPtr attr);

        [DllImport(Libc)]
        private static extern int posix_spawnattr_setflags(IntPtr attr, short flags);

        [DllImport(Libc)]
        private static extern int posix_spawnattr_setsigmask(IntPtr attr, IntPtr sigset);

        [DllImport(Libc)]
        private static extern int posix_spawnattr_setsigdefault(IntPtr attr, IntPtr sigset);

        [DllImport(Libc, SetLastError = true)]
        private static extern int sigemptyset(IntPtr set);

        [DllImport(Libc, SetLastError = true)]
        private static extern int sigfillset(IntPtr set);
    }
}
