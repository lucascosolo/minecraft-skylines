using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Skylines.Host;
using Xunit;

namespace Skylines.Host.Tests
{
    public class CompanionProcessTests
    {
        private static readonly string[] Nasty =
        {
            "plain", "with space", "q\"uote", "back\\slash", "trail\\", "$(touch PWNED)", "`touch PWNED`", "a;touch PWNED", "'single'", "$HOME", "*", "",
        };

        private static string NewDir()
        {
            return Directory.CreateTempSubdirectory("companion").FullName;
        }

        [Fact]
        public void ConfiguredValuesAreSeparatePositionalParametersNotScriptText()
        {
            string[] argv = CompanionProcess.BuildShellArgv("/bin/echo", Nasty, "/w d", "/l/log.txt");
            Assert.Equal("-c", argv[0]);
            Assert.Equal(CompanionProcess.ShellScript, argv[1]);
            Assert.Equal("sh", argv[2]);
            Assert.Equal("/l/log.txt", argv[3]);
            Assert.Equal("/w d", argv[4]);
            Assert.Equal("/bin/echo", argv[5]);
            Assert.Equal(Nasty, argv[6..]);
            Assert.Contains("\"$@\"", argv[1]);
            Assert.DoesNotContain("/bin/echo", argv[1]);
            Assert.DoesNotContain("/w d", argv[1]);
            Assert.DoesNotContain("PWNED", argv[1]);
        }

        [Fact]
        public void ShellArgvUsesEmptyWorkingDirAndAllowsNullArgs()
        {
            string[] argv = CompanionProcess.BuildShellArgv("cmd", null, null, "/l");
            Assert.Equal(new[] { "-c", CompanionProcess.ShellScript, "sh", "/l", "", "cmd" }, argv);
        }

        [Fact]
        public void StartInfoRunsBinShellAndScrubsSteamLibraryVariables()
        {
            Environment.SetEnvironmentVariable("LD_PRELOAD", "x.so");
            Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", "/steam");
            try
            {
                ProcessStartInfo psi = CompanionProcess.BuildStartInfo("c", new[] { "a" }, "/work", new Dictionary<string, string> { { "FOO", "bar" } }, "/l");
                Assert.Equal("/bin/sh", psi.FileName);
                Assert.False(psi.UseShellExecute);
                Assert.Equal("/work", psi.WorkingDirectory);
                Assert.False(psi.EnvironmentVariables.ContainsKey("LD_PRELOAD"));
                Assert.False(psi.EnvironmentVariables.ContainsKey("LD_LIBRARY_PATH"));
                Assert.Equal("bar", psi.EnvironmentVariables["FOO"]);
                Assert.Equal(CompanionProcess.JoinArguments(CompanionProcess.BuildShellArgv("c", new[] { "a" }, "/work", "/l")), psi.Arguments);
            }
            finally
            {
                Environment.SetEnvironmentVariable("LD_PRELOAD", null);
                Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", null);
            }
        }

        [Fact]
        public void BuildEnvironmentScrubsLdVariablesAppliesExtrasAndKeepsNamesUnique()
        {
            Environment.SetEnvironmentVariable("LD_PRELOAD", "x.so");
            Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", "/steam");
            Environment.SetEnvironmentVariable("SKY_TEST_KEEP", "old");
            try
            {
                string[] env = CompanionProcess.BuildEnvironment(new Dictionary<string, string> { { "SKY_TEST_KEEP", "new" }, { "SKY_TEST_EXTRA", "e=f" } });
                var names = new List<string>();
                foreach (string e in env) names.Add(e.Substring(0, e.IndexOf('=')));
                Assert.DoesNotContain("LD_PRELOAD", names);
                Assert.DoesNotContain("LD_LIBRARY_PATH", names);
                Assert.Equal(names.Count, new HashSet<string>(names).Count);
                Assert.Contains("SKY_TEST_KEEP=new", env);
                Assert.DoesNotContain("SKY_TEST_KEEP=old", env);
                Assert.Contains("SKY_TEST_EXTRA=e=f", env);
                Assert.Contains(names, n => n == "PATH");
                Assert.NotEmpty(CompanionProcess.BuildEnvironment(null));
            }
            finally
            {
                Environment.SetEnvironmentVariable("LD_PRELOAD", null);
                Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", null);
                Environment.SetEnvironmentVariable("SKY_TEST_KEEP", null);
            }
        }

        [Fact]
        public void IsLinuxIsTrueOnThisHost()
        {
            Assert.True(CompanionProcess.IsLinux());
        }

        [Fact]
        public void DescribeIncludesTypesMessagesNativeErrorCodeAndInner()
        {
            var e = new InvalidOperationException("outer-msg", new System.ComponentModel.Win32Exception(2, "inner-msg"));
            string text = CompanionProcess.Describe(e);
            Assert.Contains("System.InvalidOperationException", text);
            Assert.Contains("outer-msg", text);
            Assert.Contains("System.ComponentModel.Win32Exception", text);
            Assert.Contains("inner-msg", text);
            Assert.Contains("NativeErrorCode=2", text);
        }

        [Fact]
        public void DescribeIncludesStackTraceWhenThrown()
        {
            Exception caught = null;
            try { throw new ArgumentException("boom-msg"); }
            catch (Exception e) { caught = e; }
            string text = CompanionProcess.Describe(caught);
            Assert.Contains("System.ArgumentException", text);
            Assert.Contains("boom-msg", text);
            Assert.Contains(nameof(DescribeIncludesStackTraceWhenThrown), text);
        }

        [Fact]
        public void ConstructorsReportSpawnMode()
        {
            Assert.True(new CompanionProcess("/l", true).UsesPosixSpawn);
            Assert.False(new CompanionProcess("/l", false).UsesPosixSpawn);
            Assert.Equal(CompanionProcess.IsLinux(), new CompanionProcess("/l").UsesPosixSpawn);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void StartsChildLogsOutputAndReportsExit(bool posix)
        {
            string dir = NewDir();
            string log = Path.Combine(dir, "sub", "c.log");
            var c = new CompanionProcess(log, posix);
            Assert.False(c.IsRunning);
            Assert.Null(c.ExitCode);
            Assert.Equal(0, c.Pid);
            Assert.Null(c.StartedAt);

            string error = c.Start("/bin/sh", new[] { "-c", "echo out-$1-$FOO; echo err >&2; pwd; exit 7", "sh" },
                dir, new Dictionary<string, string> { { "FOO", "bar" } });
            Assert.Null(error);
            Assert.Null(c.LastErrorDetail);
            Assert.NotNull(c.StartedAt);
            Wait(() => !c.IsRunning);

            Assert.Equal(7, c.ExitCode);
            string text = File.ReadAllText(log);
            Assert.Contains("out--bar", text);
            Assert.Contains("err", text);
            Assert.Contains(Path.GetFileName(dir), text);
            Assert.Equal("already started", c.Start("/bin/true", null, null, null));
        }

        [Fact]
        public void PosixSpawnReportsPidAndReapsChild()
        {
            string dir = NewDir();
            var c = new CompanionProcess(Path.Combine(dir, "c.log"), true);
            Assert.Null(c.Start("/bin/sh", new[] { "-c", "exit 3" }, dir, null));
            Assert.True(c.Pid > 0);
            int pid = c.Pid;
            Wait(() => !c.IsRunning);
            Assert.Equal(3, c.ExitCode);
            Assert.False(Directory.Exists("/proc/" + pid), "child was not reaped (zombie)");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SignalDeathReports128PlusSignal(bool posix)
        {
            string dir = NewDir();
            var c = new CompanionProcess(Path.Combine(dir, "c.log"), posix);
            Assert.Null(c.Start("/bin/sh", new[] { "-c", "kill -KILL $$" }, dir, null));
            Wait(() => !c.IsRunning);
            Assert.Equal(137, c.ExitCode);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ScrubsLdPreloadInTheChildsRealEnvironment(bool posix)
        {
            string dir = NewDir();
            string log = Path.Combine(dir, "c.log");
            Environment.SetEnvironmentVariable("LD_PRELOAD", "/nonexistent/sky-test.so");
            Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", "/nonexistent/sky-lib");
            try
            {
                var c = new CompanionProcess(log, posix);
                Assert.Null(c.Start("/bin/sh", new[] { "-c", "echo \"pre=[${LD_PRELOAD-unset}] lib=[${LD_LIBRARY_PATH-unset}]\"" }, dir, null));
                Wait(() => !c.IsRunning);
                Assert.Equal(0, c.ExitCode);
                Assert.Contains("pre=[unset] lib=[unset]", File.ReadAllText(log));
            }
            finally
            {
                Environment.SetEnvironmentVariable("LD_PRELOAD", null);
                Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", null);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void HostileArgumentsArriveVerbatimAndRunNothing(bool posix)
        {
            string dir = NewDir();
            string log = Path.Combine(dir, "c.log");
            var c = new CompanionProcess(log, posix);
            var args = new List<string> { "-c", "for a in \"$@\"; do printf '[%s]\\n' \"$a\"; done", "sh" };
            args.AddRange(Nasty);
            Assert.Null(c.Start("/bin/sh", args, dir, null));
            Wait(() => !c.IsRunning);

            Assert.Equal(0, c.ExitCode);
            string text = File.ReadAllText(log);
            foreach (string a in Nasty) Assert.Contains("[" + a + "]\n", text);
            Assert.False(File.Exists(Path.Combine(dir, "PWNED")));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void HostileWorkingDirNameIsHonouredAndRunsNothing(bool posix)
        {
            string root = NewDir();
            string work = Path.Combine(root, "a b'q\"$(touch PWNED)");
            Directory.CreateDirectory(work);
            string log = Path.Combine(root, "c.log");
            var c = new CompanionProcess(log, posix);
            Assert.Null(c.Start("/bin/sh", new[] { "-c", "pwd" }, work, null));
            Wait(() => !c.IsRunning);

            Assert.Equal(0, c.ExitCode);
            Assert.Contains(Path.GetFileName(work), File.ReadAllText(log));
            Assert.False(File.Exists(Path.Combine(work, "PWNED")));
            Assert.False(File.Exists(Path.Combine(root, "PWNED")));
            Assert.False(File.Exists("PWNED"));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void MissingProgramExits127(bool posix)
        {
            string dir = NewDir();
            var gone = new CompanionProcess(Path.Combine(dir, "b.log"), posix);
            Assert.Null(gone.Start("/no/such/program", null, dir, null));
            Wait(() => !gone.IsRunning);
            Assert.Equal(127, gone.ExitCode);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BadWorkingDirIsAnErrorAndSpawnsNothing(bool posix)
        {
            string dir = NewDir();
            string marker = Path.Combine(dir, "ran");
            var bad = new CompanionProcess(Path.Combine(dir, "a.log"), posix);
            string error = bad.Start("/bin/sh", new[] { "-c", "touch \"" + marker + "\"" }, Path.Combine(dir, "missing"), null);
            Assert.False(string.IsNullOrEmpty(error));
            Assert.False(bad.IsRunning);
            Assert.Null(bad.ExitCode);
            Assert.Equal(0, bad.Pid);
            Thread.Sleep(300);
            Assert.False(File.Exists(marker));
        }

        private static void Wait(Func<bool> done)
        {
            for (int i = 0; i < 200 && !done(); i++) Thread.Sleep(50);
            Assert.True(done());
        }
    }
}
