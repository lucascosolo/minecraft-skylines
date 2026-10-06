using System.IO;

namespace Skylines.Host.Saves
{
    /// <summary>
    /// The bytes a file had at one moment, to put back later through a temporary file and an atomic replace.
    /// Never deletes anything: a file that did not exist is left as it is found.
    /// </summary>
    public sealed class FileSnapshot
    {
        private readonly byte[] _bytes;

        private FileSnapshot(string path, byte[] bytes)
        {
            Path = path;
            _bytes = bytes;
        }

        /// <summary>The file.</summary>
        public string Path { get; private set; }

        /// <summary>The file existed when captured.</summary>
        public bool Existed { get { return _bytes != null; } }

        /// <summary>Reads the file now (a missing file is remembered as missing).</summary>
        public static FileSnapshot Capture(string path)
        {
            return new FileSnapshot(path, File.Exists(path) ? File.ReadAllBytes(path) : null);
        }

        /// <summary>
        /// Writes the captured bytes back if the file existed and now differs. Returns true if it wrote.
        /// </summary>
        public bool Restore()
        {
            if (_bytes == null) return false;
            if (File.Exists(Path) && Same(File.ReadAllBytes(Path), _bytes)) return false;
            string tmp = Path + ".restore-tmp";
            File.WriteAllBytes(tmp, _bytes);
            if (File.Exists(Path)) File.Replace(tmp, Path, null);
            else File.Move(tmp, Path);
            return true;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
