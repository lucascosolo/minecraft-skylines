using System;
using System.IO;
using System.Text;
using ColossalFramework.IO;

namespace Skylines.Host
{
    /// <summary>
    /// Thread-safe mod log. Every line goes to the mod's own file under
    /// <c>&lt;DataLocation.localApplicationData&gt;/ModLogs/&lt;name&gt;.log</c> (appended, one header per
    /// session) and to Unity's Player.log. Safe to call from socket threads.
    /// </summary>
    public sealed class HostLog
    {
        private readonly object _sync = new object();
        private readonly string _prefix;
        private StreamWriter _file;

        /// <summary>Opens (appends to) the log file for <paramref name="name"/>.</summary>
        public HostLog(string name)
        {
            _prefix = "[" + name + "] ";
            try
            {
                string dir = Path.Combine(DataLocation.localApplicationData, "ModLogs");
                Directory.CreateDirectory(dir);
                FilePath = Path.Combine(dir, name + ".log");
                _file = new StreamWriter(FilePath, true, new UTF8Encoding(false));
                _file.AutoFlush = true;
                _file.WriteLine();
                _file.WriteLine("==== session " + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + " ====");
            }
            catch (Exception e)
            {
                _file = null;
                UnityEngine.Debug.LogWarning(_prefix + "cannot open log file: " + e.Message);
            }
        }

        /// <summary>Absolute path of the log file (null if it could not be opened).</summary>
        public string FilePath { get; private set; }

        /// <summary>Writes one line with a UTC timestamp.</summary>
        public void Info(string message)
        {
            Write("INFO ", message);
        }

        /// <summary>Writes one warning line.</summary>
        public void Warn(string message)
        {
            Write("WARN ", message);
        }

        /// <summary>Writes an error line with the exception's stack trace.</summary>
        public void Error(string message, Exception e)
        {
            Write("ERROR", message + (e == null ? "" : ": " + e));
        }

        private void Write(string level, string message)
        {
            string line = DateTime.UtcNow.ToString("HH:mm:ss.fff") + " " + level + " " + message;
            lock (_sync)
            {
                if (_file != null)
                {
                    try { _file.WriteLine(line); }
                    catch (Exception) { _file = null; }
                }
            }
            // Debug.Log is thread-safe in Unity 5.6.
            UnityEngine.Debug.Log(_prefix + line);
        }

        /// <summary>Flushes and closes the file; later writes go to Player.log only.</summary>
        public void Close()
        {
            lock (_sync)
            {
                if (_file != null)
                {
                    try { _file.Close(); } catch (Exception) { }
                    _file = null;
                }
            }
        }
    }
}
