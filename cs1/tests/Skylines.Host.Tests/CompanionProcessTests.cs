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

        [Fact]
        public void ConfiguredValuesAreSeparatePositionalParametersNotScriptText()
        {
            string[] argv = CompanionProcess.BuildShellArgv("/bin/echo", Nasty, "/l/log.txt");
            Assert.Equal("-c", argv[0]);
            Assert.Equal(CompanionProcess.ShellScript, argv[1]);
            Assert.Equal("sh", argv[2]);
            Assert.Equal("/l/log.txt", argv[3]);
            Assert.Equal("/bin/echo", argv[4]);
            Assert.Equal(Nasty, argv[5..]);
            Assert.Contains("\"$@\"", argv[1]);
            Assert.DoesNotContain("/bin/echo", argv[1]);
            Assert.DoesNotContain("PWNED", argv[1]);
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
            }
            finally
            {
                Environment.SetEnvironmentVariable("LD_PRELOAD", null);
                Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", null);
            }
        }

        [Fact]
        public void StartsChildLogsOutputAndReportsExit()
        {
            string dir = Directory.CreateTempSubdirectory("companion").FullName;
            string log = Path.Combine(dir, "sub", "c.log");
            var c = new CompanionProcess(log);
            Assert.False(c.IsRunning);
            Assert.Null(c.StartedAt);

            string error = c.Start("/bin/sh", new[] { "-c", "echo out-$1-$FOO; echo err >&2; pwd; exit 7", "sh" },
                dir, new Dictionary<string, string> { { "FOO", "bar" } });
            Assert.Null(error);
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
        public void HostileArgumentsArriveVerbatimAndRunNothing()
        {
            string dir = Directory.CreateTempSubdirectory("companion").FullName;
            string log = Path.Combine(dir, "c.log");
            var c = new CompanionProcess(log);
            var args = new List<string> { "-c", "for a in \"$@\"; do printf '[%s]\\n' \"$a\"; done", "sh" };
            args.AddRange(Nasty);
            Assert.Null(c.Start("/bin/sh", args, dir, null));
            Wait(() => !c.IsRunning);

            Assert.Equal(0, c.ExitCode);
            string text = File.ReadAllText(log);
            foreach (string a in Nasty) Assert.Contains("[" + a + "]\n", text);
            Assert.False(File.Exists(Path.Combine(dir, "PWNED")));
        }

        [Fact]
        public void MissingProgramAndBadWorkingDirAreErrorsNotExceptions()
        {
            string dir = Directory.CreateTempSubdirectory("companion").FullName;
            var bad = new CompanionProcess(Path.Combine(dir, "a.log"));
            Assert.NotNull(bad.Start("/bin/sh", null, Path.Combine(dir, "missing"), null));
            Assert.False(bad.IsRunning);

            var gone = new CompanionProcess(Path.Combine(dir, "b.log"));
            Assert.Null(gone.Start("/no/such/program", null, dir, null));
            Wait(() => !gone.IsRunning);
            Assert.Equal(127, gone.ExitCode);
        }

        private static void Wait(Func<bool> done)
        {
            for (int i = 0; i < 100 && !done(); i++) Thread.Sleep(50);
            Assert.True(done());
        }
    }
}
