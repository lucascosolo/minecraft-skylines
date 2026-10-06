using System;
using System.IO;

namespace Skylines.Host.Saves
{
    /// <summary>What a backup check found.</summary>
    public enum BackupCheck
    {
        /// <summary>Not decided yet; poll again.</summary>
        Pending,
        /// <summary>The save file is complete.</summary>
        Verified,
        /// <summary>The save did not appear or is not a valid package; see <see cref="BackupVerifier.Detail"/>.</summary>
        Failed,
    }

    /// <summary>What the verifier needs to know about a file.</summary>
    public struct FileFacts
    {
        /// <summary>The file exists.</summary>
        public bool Exists;
        /// <summary>Its size in bytes.</summary>
        public long Length;
        /// <summary>It starts with the package magic "CRAP".</summary>
        public bool HasMagic;
    }

    /// <summary>
    /// Decides when a save the game is writing is complete: the game no longer saving, the file present, at least
    /// <see cref="MinBytes"/>, starting with "CRAP", and the same size in two checks <see cref="StableSeconds"/>
    /// apart. Fails after <see cref="TimeoutSeconds"/>, or <see cref="GraceSeconds"/> after the game stopped saving
    /// with the file still missing or invalid. Unity-free; poll it from the main thread.
    /// </summary>
    public sealed class BackupVerifier
    {
        /// <summary>Smallest plausible save package.</summary>
        public const long MinBytes = 64 * 1024;
        /// <summary>Gap between the two size checks.</summary>
        public const double StableSeconds = 1.0;
        /// <summary>Longest wait overall.</summary>
        public const double TimeoutSeconds = 120.0;
        /// <summary>Longest wait for a valid file once the game reports it is no longer saving.</summary>
        public const double GraceSeconds = 5.0;

        private readonly string _path;
        private readonly double _start;
        private readonly Func<string, FileFacts> _probe;
        private double _badSince = -1;
        private double _sampleAt = -1;
        private long _sampleLength;
        private BackupCheck _result = BackupCheck.Pending;

        /// <summary>Starts checking <paramref name="path"/> at time <paramref name="startSeconds"/>.</summary>
        public BackupVerifier(string path, double startSeconds, Func<string, FileFacts> probe)
        {
            _path = path;
            _start = startSeconds;
            _probe = probe;
            Detail = "waiting for the game to save";
        }

        /// <summary>Progress or the reason for failure.</summary>
        public string Detail { get; private set; }

        /// <summary>One check; a decided result stays.</summary>
        public BackupCheck Poll(double nowSeconds, bool gameSaving)
        {
            if (_result != BackupCheck.Pending) return _result;
            if (nowSeconds - _start > TimeoutSeconds) return Fail("timed out after " + TimeoutSeconds + " s (" + Detail + ")");
            if (gameSaving)
            {
                _badSince = -1;
                _sampleAt = -1;
                Detail = "the game is saving";
                return BackupCheck.Pending;
            }
            FileFacts f = _probe(_path);
            string bad = !f.Exists ? "the file does not exist"
                : f.Length < MinBytes ? "the file has only " + f.Length + " bytes"
                : !f.HasMagic ? "the file is not a save package"
                : null;
            if (bad != null)
            {
                _sampleAt = -1;
                if (_badSince < 0) _badSince = nowSeconds;
                Detail = bad;
                if (nowSeconds - _badSince >= GraceSeconds) return Fail(bad);
                return BackupCheck.Pending;
            }
            _badSince = -1;
            if (_sampleAt < 0 || f.Length != _sampleLength)
            {
                _sampleAt = nowSeconds;
                _sampleLength = f.Length;
                Detail = "checking the file (" + f.Length + " bytes)";
                return BackupCheck.Pending;
            }
            if (nowSeconds - _sampleAt < StableSeconds) return BackupCheck.Pending;
            Detail = "verified (" + f.Length + " bytes)";
            _result = BackupCheck.Verified;
            return _result;
        }

        /// <summary>Reads the facts of a real file.</summary>
        public static FileFacts ProbeFile(string path)
        {
            var f = new FileFacts();
            var info = new FileInfo(path);
            if (!info.Exists) return f;
            f.Exists = true;
            f.Length = info.Length;
            var head = new byte[4];
            using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                int n = 0, r;
                while (n < 4 && (r = s.Read(head, n, 4 - n)) > 0) n += r;
                f.HasMagic = n == 4 && head[0] == 'C' && head[1] == 'R' && head[2] == 'A' && head[3] == 'P';
            }
            return f;
        }

        private BackupCheck Fail(string why)
        {
            Detail = why;
            _result = BackupCheck.Failed;
            return _result;
        }
    }
}
