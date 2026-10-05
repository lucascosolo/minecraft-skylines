using System;
using ICities;

namespace Skylines.Host
{
    /// <summary>
    /// A stable id stored inside a city save (as serializable mod data), so a companion process
    /// can keep state that belongs to exactly this city. Thread-safe: OnLoadData/OnSaveData may run
    /// off the main thread.
    /// </summary>
    /// <remarks>
    /// Record layout under <see cref="DataKey"/>: byte version (1), then 16 bytes of the Guid in
    /// <see cref="Guid.ToByteArray"/> order. Within this record only; protocols carry RFC 4122 order.
    /// </remarks>
    public sealed class SaveIdentity
    {
        private const byte RecordVersion = 1;
        private readonly object _sync = new object();
        private Guid _id = Guid.Empty;
        private bool _loadedFromSave;

        /// <summary>Creates an identity stored under <paramref name="dataKey"/> (make it unique per mod).</summary>
        public SaveIdentity(string dataKey)
        {
            DataKey = dataKey;
        }

        /// <summary>The serializable-data key the id is stored under.</summary>
        public string DataKey { get; private set; }

        /// <summary>The current city's id; <see cref="Guid.Empty"/> before a city is loaded.</summary>
        public Guid Id
        {
            get { lock (_sync) return _id; }
        }

        /// <summary>True if the id came from the save rather than being newly assigned.</summary>
        public bool LoadedFromSave
        {
            get { lock (_sync) return _loadedFromSave; }
        }

        /// <summary>From SerializableDataExtensionBase.OnLoadData. Returns a description for the log.</summary>
        public string Load(ISerializableData data)
        {
            byte[] raw = data.LoadData(DataKey);
            lock (_sync)
            {
                _id = Guid.Empty;
                _loadedFromSave = false;
                if (raw == null)
                {
                    return "no saved id (city never saved with this mod)";
                }
                if (raw.Length < 17 || raw[0] != RecordVersion)
                {
                    return "unreadable saved id record (" + raw.Length + " bytes, version " + (raw.Length > 0 ? raw[0] : -1) + "); a new id will be assigned";
                }
                var g = new byte[16];
                Array.Copy(raw, 1, g, 0, 16);
                _id = new Guid(g);
                _loadedFromSave = true;
                return "loaded id " + _id;
            }
        }

        /// <summary>From OnLevelLoaded: assigns a new id if the save had none. Returns true if assigned.</summary>
        public bool EnsureAssigned()
        {
            lock (_sync)
            {
                if (_id != Guid.Empty)
                {
                    return false;
                }
                _id = Guid.NewGuid();
                return true;
            }
        }

        /// <summary>From OnSaveData: writes the id into the save.</summary>
        public void Save(ISerializableData data)
        {
            Guid id = Id;
            if (id == Guid.Empty)
            {
                return;
            }
            var raw = new byte[17];
            raw[0] = RecordVersion;
            Array.Copy(id.ToByteArray(), 0, raw, 1, 16);
            data.SaveData(DataKey, raw);
        }

        /// <summary>From OnLevelUnloading.</summary>
        public void Clear()
        {
            lock (_sync)
            {
                _id = Guid.Empty;
                _loadedFromSave = false;
            }
        }
    }
}
