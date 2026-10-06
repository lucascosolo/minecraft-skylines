using System;
using System.Diagnostics;
using System.IO;
using ColossalFramework.IO;
using ColossalFramework.Packaging;
using ColossalFramework.UI;

namespace Skylines.Host.Saves
{
    /// <summary>
    /// Writes a separate backup of the loaded city through the game's own save routine (public
    /// <c>SavePanel.SaveGame(string)</c>, a local save), under a name no existing save uses, and verifies the file
    /// (<see cref="BackupVerifier"/>). The game's routine also points the launcher's continue_game.json at the
    /// backup; its previous bytes are put back once the save is over. Main thread only: call <see cref="Update"/>
    /// every frame while <see cref="IsRunning"/>.
    /// </summary>
    public sealed class CityBackup
    {
        /// <summary>Where a backup is.</summary>
        public enum Phase
        {
            /// <summary>No backup started.</summary>
            Idle,
            /// <summary>The game is writing it, or it is being verified.</summary>
            Saving,
            /// <summary>The file is complete.</summary>
            Verified,
            /// <summary>See <see cref="Detail"/>.</summary>
            Failed,
        }

        private readonly Stopwatch _clock = new Stopwatch();
        private BackupVerifier _verifier;
        private FileSnapshot _continue;

        /// <summary>Current phase.</summary>
        public Phase State { get; private set; }

        /// <summary>The backup's save name (as shown in the load menu); null before <see cref="Begin"/>.</summary>
        public string Name { get; private set; }

        /// <summary>The file the game writes.</summary>
        public string FilePath { get; private set; }

        /// <summary>Progress, or why it failed.</summary>
        public string Detail { get; private set; }

        /// <summary>True while saving or verifying.</summary>
        public bool IsRunning { get { return State == Phase.Saving; } }

        /// <summary>The save file the game writes for a local save name (SavePanel.GetSavePathName).</summary>
        public static string SavePathOf(string saveName)
        {
            return Path.Combine(DataLocation.saveLocation, PathUtils.AddExtension(PathEscaper.Escape(saveName), PackageManager.packageExtension));
        }

        /// <summary>The name a backup would get now: "&lt;city&gt; (&lt;label&gt;) yyyy-MM-dd HHmm", never an existing file.</summary>
        public static string NextName(string cityName, string label)
        {
            return BackupNaming.Unique(BackupNaming.BaseName(cityName, label, DateTime.Now), n => File.Exists(SavePathOf(n)));
        }

        /// <summary>
        /// Starts a backup named <paramref name="saveName"/> (from <see cref="NextName"/>). Refuses a name whose file
        /// exists now. Returns null, or why it could not start (then <see cref="State"/> is Failed).
        /// </summary>
        public string Begin(string saveName)
        {
            if (IsRunning) return "a backup is already running";
            Name = saveName;
            FilePath = SavePathOf(saveName);
            string error = Start();
            if (error != null)
            {
                State = Phase.Failed;
                Detail = error;
            }
            return error;
        }

        private string Start()
        {
            if (File.Exists(FilePath)) return "a save named '" + Name + "' already exists";
            if (SavePanel.isSaving) return "the game is already saving";
            SavePanel panel = UIView.library != null ? UIView.library.Get<SavePanel>("SavePanel") : null;
            if (panel == null) return "the game's save panel is not available";
            _continue = FileSnapshot.Capture(SavePanel.continueSaveDataLocation);
            if (!panel.SaveGame(Name)) return "the game refused to save (loading, or a save already in progress)";
            _clock.Reset();
            _clock.Start();
            _verifier = new BackupVerifier(FilePath, 0, BackupVerifier.ProbeFile);
            State = Phase.Saving;
            Detail = _verifier.Detail;
            return null;
        }

        /// <summary>Per frame while running.</summary>
        public void Update()
        {
            if (State != Phase.Saving) return;
            BackupCheck r;
            string why = null;
            try { r = _verifier.Poll(_clock.Elapsed.TotalSeconds, SavePanel.isSaving); }
            catch (Exception e) { r = BackupCheck.Failed; why = "could not read the backup: " + e.Message; }
            if (r == BackupCheck.Pending)
            {
                Detail = _verifier.Detail;
                return;
            }
            Detail = (why ?? _verifier.Detail) + RestoreContinue();
            State = r == BackupCheck.Verified ? Phase.Verified : Phase.Failed;
        }

        private string RestoreContinue()
        {
            try
            {
                if (_continue == null || !_continue.Existed) return "";
                return _continue.Restore() ? "; continue_game.json restored" : "";
            }
            catch (Exception e)
            {
                return "; could not restore continue_game.json: " + e.Message;
            }
        }
    }
}
