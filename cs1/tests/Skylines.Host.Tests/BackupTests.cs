using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Skylines.Host.Saves;
using Xunit;

namespace Skylines.Host.Tests
{
    public class BackupNamingTests
    {
        static readonly DateTime Now = new DateTime(2026, 3, 7, 14, 5, 0);

        [Fact]
        public void BaseNameFormat()
        {
            Assert.Equal("Springfield (pre-edit) 2026-03-07 1405", BackupNaming.BaseName("Springfield", "pre-edit", Now));
        }

        [Fact]
        public void BaseNameUses24HourClock()
        {
            Assert.EndsWith("2026-03-07 2309", BackupNaming.BaseName("A", "b", new DateTime(2026, 3, 7, 23, 9, 0)));
        }

        [Fact]
        public void BaseNameTrimsCity()
        {
            Assert.Equal("Town (x) 2026-03-07 1405", BackupNaming.BaseName("  Town  ", "x", Now));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void BlankCityBecomesCity(string city)
        {
            Assert.Equal("City (x) 2026-03-07 1405", BackupNaming.BaseName(city, "x", Now));
        }

        [Fact]
        public void BaseNameIgnoresCulture()
        {
            var old = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
                Assert.Equal("A (b) 2026-03-07 1405", BackupNaming.BaseName("A", "b", Now));
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = old; }
        }

        [Fact]
        public void UniqueReturnsBaseWhenFree()
        {
            Assert.Equal("n", BackupNaming.Unique("n", s => false));
        }

        [Fact]
        public void UniqueAppendsCounterFromTwo()
        {
            var taken = new HashSet<string> { "n", "n (2)" };
            Assert.Equal("n (3)", BackupNaming.Unique("n", taken.Contains));
        }

        [Fact]
        public void UniqueSkipsOnlyTakenNames()
        {
            var taken = new HashSet<string> { "n" };
            Assert.Equal("n (2)", BackupNaming.Unique("n", taken.Contains));
        }

        [Fact]
        public void UniqueThrowsWhenAllAttemptsTaken()
        {
            Assert.Equal(1000, BackupNaming.MaxAttempts);
            Assert.Throws<InvalidOperationException>(() => BackupNaming.Unique("n", s => true));
        }

        [Fact]
        public void UniqueSucceedsOnLastAttempt()
        {
            var taken = new HashSet<string> { "n" };
            for (int i = 2; i < BackupNaming.MaxAttempts; i++) taken.Add("n (" + i + ")");
            Assert.Equal("n (" + BackupNaming.MaxAttempts + ")", BackupNaming.Unique("n", taken.Contains));
        }
    }

    public class BackupVerifierTests
    {
        static FileFacts Good(long len = 100000) { return new FileFacts { Exists = true, Length = len, HasMagic = true }; }

        static BackupVerifier Make(Func<FileFacts> f, double start = 0)
        {
            return new BackupVerifier("p", start, p => f());
        }

        [Fact]
        public void Constants()
        {
            Assert.Equal(65536, BackupVerifier.MinBytes);
            Assert.Equal(1.0, BackupVerifier.StableSeconds);
            Assert.Equal(120.0, BackupVerifier.TimeoutSeconds);
            Assert.Equal(5.0, BackupVerifier.GraceSeconds);
        }

        [Fact]
        public void StableGoodFileVerifies()
        {
            var v = Make(() => Good());
            Assert.Equal(BackupCheck.Pending, v.Poll(1, false));
            Assert.Equal(BackupCheck.Verified, v.Poll(2, false));
        }

        [Fact]
        public void SameLengthBeforeStableWindowIsPending()
        {
            var v = Make(() => Good());
            v.Poll(1, false);
            Assert.Equal(BackupCheck.Pending, v.Poll(1.5, false));
            Assert.Equal(BackupCheck.Verified, v.Poll(2.0, false));
        }

        [Fact]
        public void ChangedLengthRestartsSample()
        {
            long len = 100000;
            var v = Make(() => Good(len));
            v.Poll(1, false);
            len = 200000;
            Assert.Equal(BackupCheck.Pending, v.Poll(3, false));
            Assert.Equal(BackupCheck.Pending, v.Poll(3.5, false));
            Assert.Equal(BackupCheck.Verified, v.Poll(4, false));
        }

        [Fact]
        public void VerifiedIsSticky()
        {
            long len = 100000;
            var v = Make(() => Good(len));
            v.Poll(1, false);
            v.Poll(2, false);
            len = 1;
            Assert.Equal(BackupCheck.Verified, v.Poll(3, true));
            Assert.Equal(BackupCheck.Verified, v.Poll(500, false));
        }

        [Fact]
        public void TimeoutFails()
        {
            var v = Make(() => Good(), 10);
            Assert.Equal(BackupCheck.Failed, v.Poll(10 + 120.5, false));
        }

        [Fact]
        public void TimeoutBoundaryIsExclusive()
        {
            var v = Make(() => Good(), 0);
            Assert.NotEqual(BackupCheck.Failed, v.Poll(120.0, false));
        }

        [Fact]
        public void TimeoutBeatsSaving()
        {
            var v = Make(() => Good());
            Assert.Equal(BackupCheck.Failed, v.Poll(121, true));
        }

        [Fact]
        public void FailedIsSticky()
        {
            var v = Make(() => Good());
            Assert.Equal(BackupCheck.Failed, v.Poll(121, false));
            Assert.Equal(BackupCheck.Failed, v.Poll(122, false));
        }

        [Fact]
        public void SavingIsPendingEvenForGoodFile()
        {
            var v = Make(() => Good());
            v.Poll(1, false);
            Assert.Equal(BackupCheck.Pending, v.Poll(5, true));
        }

        [Fact]
        public void SavingResetsStabilitySample()
        {
            var v = Make(() => Good());
            v.Poll(1, false);
            v.Poll(2, true);
            // first good poll after saving is a fresh sample
            Assert.Equal(BackupCheck.Pending, v.Poll(10, false));
            Assert.Equal(BackupCheck.Verified, v.Poll(11, false));
        }

        [Theory]
        [InlineData(false, 100000L, true)]
        [InlineData(true, 100L, true)]
        [InlineData(true, 65535L, true)]
        [InlineData(true, 100000L, false)]
        public void BadFileIsPendingThenFailsAfterGrace(bool exists, long len, bool magic)
        {
            var f = new FileFacts { Exists = exists, Length = len, HasMagic = magic };
            var v = Make(() => f);
            Assert.Equal(BackupCheck.Pending, v.Poll(1, false));
            Assert.Equal(BackupCheck.Pending, v.Poll(5.9, false));
            Assert.Equal(BackupCheck.Failed, v.Poll(6, false));
        }

        [Fact]
        public void ExactlyMinBytesIsGood()
        {
            var v = Make(() => Good(BackupVerifier.MinBytes));
            v.Poll(1, false);
            Assert.Equal(BackupCheck.Verified, v.Poll(2, false));
        }

        [Fact]
        public void FileAppearingWithinGraceIsAccepted()
        {
            bool ready = false;
            var v = Make(() => ready ? Good() : new FileFacts());
            Assert.Equal(BackupCheck.Pending, v.Poll(1, false));
            Assert.Equal(BackupCheck.Pending, v.Poll(4, false));
            ready = true;
            Assert.Equal(BackupCheck.Pending, v.Poll(5, false));
            Assert.Equal(BackupCheck.Verified, v.Poll(6, false));
        }

        [Fact]
        public void SavingResetsGraceTimer()
        {
            var v = Make(() => new FileFacts());
            v.Poll(1, false);
            v.Poll(4, true);
            Assert.Equal(BackupCheck.Pending, v.Poll(8, false));
            Assert.Equal(BackupCheck.Pending, v.Poll(12.9, false));
            Assert.Equal(BackupCheck.Failed, v.Poll(13, false));
        }

        [Fact]
        public void GoodPollResetsGraceTimer()
        {
            bool good = false;
            var v = Make(() => good ? Good() : new FileFacts());
            v.Poll(1, false);
            good = true;
            v.Poll(3, false);
            good = false;
            Assert.Equal(BackupCheck.Pending, v.Poll(7, false)); // bad since 7, not since 1
            Assert.Equal(BackupCheck.Pending, v.Poll(11.9, false));
            Assert.Equal(BackupCheck.Failed, v.Poll(12, false));
        }

        [Fact]
        public void ProbeReceivesPath()
        {
            string seen = null;
            var v = new BackupVerifier("the/path", 0, p => { seen = p; return Good(); });
            v.Poll(1, false);
            Assert.Equal("the/path", seen);
        }

        static string NewDir()
        {
            var d = Path.Combine(Path.GetTempPath(), "mcsk-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(d);
            return d;
        }

        [Fact]
        public void ProbeFileMissing()
        {
            var f = BackupVerifier.ProbeFile(Path.Combine(NewDir(), "none.crp"));
            Assert.False(f.Exists);
        }

        [Fact]
        public void ProbeFileWithMagic()
        {
            var p = Path.Combine(NewDir(), "a.crp");
            File.WriteAllBytes(p, Encoding.ASCII.GetBytes("CRAPxxxxxx"));
            var f = BackupVerifier.ProbeFile(p);
            Assert.True(f.Exists);
            Assert.Equal(10, f.Length);
            Assert.True(f.HasMagic);
        }

        [Fact]
        public void ProbeFileWithoutMagic()
        {
            var p = Path.Combine(NewDir(), "b.crp");
            File.WriteAllBytes(p, Encoding.ASCII.GetBytes("CRAxxxxx"));
            Assert.False(BackupVerifier.ProbeFile(p).HasMagic);
        }

        [Fact]
        public void ProbeFileShorterThanFourBytes()
        {
            var p = Path.Combine(NewDir(), "c.crp");
            File.WriteAllBytes(p, new byte[] { 0x43, 0x52 });
            var f = BackupVerifier.ProbeFile(p);
            Assert.True(f.Exists);
            Assert.False(f.HasMagic);
        }
    }

    public class FileSnapshotTests
    {
        static string NewPath()
        {
            var d = Path.Combine(Path.GetTempPath(), "mcsk-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(d);
            return Path.Combine(d, "city.crp");
        }

        [Fact]
        public void CaptureRecordsExistence()
        {
            var p = NewPath();
            Assert.False(FileSnapshot.Capture(p).Existed);
            File.WriteAllBytes(p, new byte[] { 1 });
            var s = FileSnapshot.Capture(p);
            Assert.True(s.Existed);
            Assert.Equal(p, s.Path);
        }

        [Fact]
        public void RestoreUnchangedReturnsFalse()
        {
            var p = NewPath();
            File.WriteAllBytes(p, new byte[] { 1, 2, 3 });
            var s = FileSnapshot.Capture(p);
            Assert.False(s.Restore());
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(p));
        }

        [Fact]
        public void RestoreWritesBackChangedBytes()
        {
            var p = NewPath();
            File.WriteAllBytes(p, new byte[] { 1, 2, 3 });
            var s = FileSnapshot.Capture(p);
            File.WriteAllBytes(p, new byte[] { 9, 9 });
            Assert.True(s.Restore());
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(p));
            Assert.False(File.Exists(p + ".restore-tmp"));
        }

        [Fact]
        public void RestoreRecreatesMissingFile()
        {
            var p = NewPath();
            File.WriteAllBytes(p, new byte[] { 4, 5 });
            var s = FileSnapshot.Capture(p);
            File.Move(p, p + ".moved");
            Assert.True(s.Restore());
            Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(p));
            Assert.False(File.Exists(p + ".restore-tmp"));
        }

        [Fact]
        public void RestoreOfNonexistentLeavesCreatedFileAlone()
        {
            var p = NewPath();
            var s = FileSnapshot.Capture(p);
            File.WriteAllBytes(p, new byte[] { 7 });
            Assert.False(s.Restore());
            Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(p));
        }

        [Fact]
        public void RestoreOfNonexistentWritesNothingWhenStillMissing()
        {
            var p = NewPath();
            var s = FileSnapshot.Capture(p);
            Assert.False(s.Restore());
            Assert.False(File.Exists(p));
            Assert.False(File.Exists(p + ".restore-tmp"));
        }
    }
}
