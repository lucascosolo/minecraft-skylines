using System;
using System.Collections.Generic;
using System.Text;
using ColossalFramework;
using ICities;
using MinecraftSkylines.Protocol;
using Skylines.Bridge;
using Skylines.Core.Saves;
using Skylines.Core.Voxels;
using Skylines.Host;
using Skylines.Host.Saves;

namespace MinecraftSkylines.Mod.City
{
    /// <summary>
    /// Pairs a city with Minecraft and keeps its blocks in the save (docs/plans/m4.md). The city's edit set is the
    /// authority: sent as CITY_OPEN + BLOCK_EDITS whenever a paired city is loaded and the link is up (again after
    /// every reconnect), updated from the guest's BLOCK_EDITS, and written into the save in <see cref="OnSaveData"/>
    /// after the EDIT_SYNC barrier. Main thread except <see cref="OnLoadData"/> and <see cref="OnSaveData"/>; the
    /// edit set and the open state are shared under <see cref="_sync"/>.
    /// </summary>
    internal sealed class CityLink
    {
        public const string EditsKey = "MinecraftSkylines.CityEdits";
        public const string UnreadableKey = "MinecraftSkylines.CityEdits.unreadable";
        public const string PlayerKey = "MinecraftSkylines.PlayerData";
        public const string PlayerUnreadableKey = "MinecraftSkylines.PlayerData.unreadable";
        private const string BackupLabel = "before Minecraft";
        private const int BarrierTimeoutMs = 2000;

        private enum Pairing { None, Confirming, BackingUp }

        private readonly HostLog _log;
        private readonly SaveIdentity _saveId;
        private readonly PlayerMode _player;
        private readonly CityBackup _backup = new CityBackup();
        private readonly EditSyncBarrier _barrier = new EditSyncBarrier();
        private readonly object _sync = new object();

        // Under _sync.
        private VoxelEditSet _edits = new VoxelEditSet();
        private byte[] _unreadable;
        private byte[] _playerData;
        private byte[] _playerUnreadable;
        private uint _openSeq;
        private bool _open;
        private bool _ready;
        private BridgeHost _host;

        private Pairing _pairing;
        private int _generation;
        private string _loadMode = "";
        private bool _wantEnter;
        private string _note = "";
        private long _staleBatches;
        private long _stalePlayer;

        public CityLink(HostLog log, SaveIdentity saveId, PlayerMode player)
        {
            _log = log;
            _saveId = saveId;
            _player = player;
        }

        public bool Paired { get { return _saveId.Id != Guid.Empty; } }

        /// <summary>The city was started from a map (never saved), so it has nothing a backup could protect.</summary>
        public bool StartedFromMap { get { return _loadMode == "NewGame" || _loadMode == "NewGameFromScenario"; } }

        public uint OpenSeq { get { lock (_sync) return _openSeq; } }

        /// <summary>The guest reported CITY_STATE ready for the current open.</summary>
        public bool Ready { get { lock (_sync) return _open && _ready; } }

        public int EditCount { get { lock (_sync) return _edits.Count; } }

        /// <summary>Main thread: one edit position changed (true = it has an edit now), after the guest's BLOCK_EDITS.</summary>
        public Action<int, int, int, string> EditChanged;

        /// <summary>Every edit, sorted.</summary>
        public List<VoxelEdit> SortedEdits()
        {
            lock (_sync) return _edits.Sorted();
        }

        public bool TryGetEdit(int x, int y, int z, out string state)
        {
            lock (_sync) return _edits.TryGet(x, y, z, out state);
        }

        /// <summary>Loading thread: the edit set from the save (an unreadable record loads as empty and is kept).</summary>
        public void OnLoadData(ISerializableData data)
        {
            byte[] raw = data.LoadData(EditsKey);
            byte[] keptBefore = data.LoadData(UnreadableKey);
            var edits = new VoxelEditSet();
            byte[] unreadable = keptBefore;
            string what = raw == null ? "no edit record" : null;
            if (raw != null)
            {
                try
                {
                    edits = VoxelEditRecord.Decode(raw);
                    what = edits.Count + " block edits";
                }
                catch (FormatException e)
                {
                    if (keptBefore != null) _log.Warn("blocks: dropping an older kept unreadable record (" + keptBefore.Length + " bytes) for the newer one");
                    unreadable = raw;
                    what = "UNREADABLE edit record (" + e.Message + ", " + raw.Length + " bytes): loaded as empty, kept unchanged under " + UnreadableKey;
                }
            }
            if (unreadable != null && unreadable == keptBefore) what += "; earlier unreadable record kept (" + keptBefore.Length + " bytes)";

            byte[] playerRaw = data.LoadData(PlayerKey);
            byte[] playerKeptBefore = data.LoadData(PlayerUnreadableKey);
            byte[] player = null;
            byte[] playerUnreadable = playerKeptBefore;
            if (playerRaw == null) what += "; no player record";
            else
            {
                try
                {
                    player = BlobRecord.Decode(playerRaw);
                    what += "; player data " + player.Length + " bytes";
                }
                catch (FormatException e)
                {
                    if (playerKeptBefore != null) _log.Warn("player: dropping an older kept unreadable record (" + playerKeptBefore.Length + " bytes) for the newer one");
                    playerUnreadable = playerRaw;
                    what += "; UNREADABLE player record (" + e.Message + ", " + playerRaw.Length + " bytes): loaded as a fresh player, kept unchanged under " + PlayerUnreadableKey;
                }
            }
            lock (_sync)
            {
                _edits = edits;
                _unreadable = unreadable;
                _playerData = player;
                _playerUnreadable = playerUnreadable;
                _open = false;
                _ready = false;
            }
            _log.Info("load data: " + what);
        }

        /// <summary>Simulation thread: barrier, then the edit set into the save. Writes nothing for an unpaired city.</summary>
        public void OnSaveData(ISerializableData data)
        {
            if (!Paired) return;
            BridgeHost host;
            uint seq;
            bool open;
            lock (_sync)
            {
                host = _host;
                seq = _openSeq;
                open = _open && _ready;
            }
            if (open && host != null && host.State == BridgeState.Connected)
            {
                // BridgeHost.Send only enqueues under a lock, so it is safe from this thread.
                uint token = _barrier.Begin();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                bool acked = host.Send(AppProtocol.EditSyncType, new EditSync { OpenSeq = seq, Token = token }.Encode())
                    && _barrier.Wait(token, BarrierTimeoutMs);
                if (acked) _log.Info("save data: edit sync " + token + " acknowledged after " + watch.ElapsedMilliseconds + " ms");
                else _log.Warn("save data: edit sync " + token + " not acknowledged within " + BarrierTimeoutMs + " ms; saving the edits received so far");
            }
            else
            {
                _log.Info("save data: no city open in Minecraft; saving the edit set as it is");
            }
            byte[] record, unreadable, player, playerUnreadable;
            int count;
            lock (_sync)
            {
                record = VoxelEditRecord.Encode(_edits);
                count = _edits.Count;
                unreadable = _unreadable;
                player = _playerData;
                playerUnreadable = _playerUnreadable;
            }
            data.SaveData(EditsKey, record);
            if (unreadable != null) data.SaveData(UnreadableKey, unreadable);
            if (player != null)
            {
                data.SaveData(PlayerKey, BlobRecord.Encode(player));
                _log.Info("save data: wrote player data (" + player.Length + " bytes)");
            }
            if (playerUnreadable != null) data.SaveData(PlayerUnreadableKey, playerUnreadable);
            _log.Info("save data: wrote " + count + " block edits (" + record.Length + " bytes)" + (unreadable != null ? " and the kept unreadable record" : ""));
        }

        public void OnLevelLoaded(string mode)
        {
            _loadMode = mode;
            _generation++;
            _note = "";
        }

        public void OnLevelUnloading(BridgeHost host)
        {
            uint seq;
            bool wasOpen;
            lock (_sync)
            {
                seq = _openSeq;
                wasOpen = _open;
                _open = false;
                _ready = false;
                _edits = new VoxelEditSet();
                _unreadable = null;
                _playerData = null;
                _playerUnreadable = null;
            }
            _barrier.Cancel();
            if (wasOpen && host != null && host.State == BridgeState.Connected)
            {
                host.Send(AppProtocol.CityCloseType, new CityClose { OpenSeq = seq }.Encode());
                _log.Info("blocks: CITY_CLOSE " + seq);
            }
            _generation++;
            _pairing = Pairing.None;
            _wantEnter = false;
            _loadMode = "";
        }

        public void OnDisconnect()
        {
            lock (_sync)
            {
                _open = false;
                _ready = false;
            }
            _barrier.Cancel();
            _wantEnter = false;
        }

        /// <summary>Per frame, after bridge events were handled and before player mode.</summary>
        public void Update(BridgeHost host, bool cityReady)
        {
            lock (_sync) _host = host;
            if (_backup.IsRunning) UpdateBackup();
            if (!cityReady || !Paired || host == null || host.State != BridgeState.Connected) return;
            if (host.NegotiatedAppMinor < 5) return;
            bool open;
            lock (_sync) open = _open;
            if (!open) SendOpen(host);
        }

        /// <summary>Handles the guest's messages of this milestone; false for any other type.</summary>
        public bool Handle(ushort type, byte[] payload)
        {
            if (type == AppProtocol.BlockEditsType)
            {
                BlockEdits m = BlockEdits.Decode(payload);
                lock (_sync)
                {
                    if (!_open || m.OpenSeq != _openSeq)
                    {
                        _staleBatches++;
                        return true;
                    }
                    foreach (BlockEdit e in m.Edits) _edits.Set(e.X, e.Y, e.Z, m.Palette[e.State]);
                }
                Action<int, int, int, string> changed = EditChanged;
                if (changed != null)
                    foreach (BlockEdit e in m.Edits) changed(e.X, e.Y, e.Z, m.Palette[e.State]);
                return true;
            }
            if (type == AppProtocol.PlayerDataType)
            {
                PlayerData d = PlayerData.Decode(payload);
                lock (_sync)
                {
                    if (!_open || d.OpenSeq != _openSeq) _stalePlayer++;
                    else if (d.Data.Length > 0) _playerData = d.Data;
                }
                return true;
            }
            if (type == AppProtocol.RespawnRequestType)
            {
                BridgeHost host;
                lock (_sync) host = _host;
                _player.Respawn(host);
                return true;
            }
            if (type == AppProtocol.TreeFelledType)
            {
                TreeFelled f = TreeFelled.Decode(payload);
                lock (_sync)
                {
                    if (!_open || f.OpenSeq != _openSeq) return true;
                }
                ReleaseTree(f.TreeId);
                return true;
            }
            if (type == AppProtocol.TreeGrownType)
            {
                TreeGrown g = TreeGrown.Decode(payload);
                lock (_sync)
                {
                    if (!_open || g.OpenSeq != _openSeq) return true;
                }
                GrowTree(g);
                return true;
            }
            if (type == AppProtocol.EditSyncAckType)
            {
                _barrier.Acknowledge(EditSync.Decode(payload).Token);
                return true;
            }
            if (type == AppProtocol.CityStateType)
            {
                CityStateUpdate s = CityStateUpdate.Decode(payload);
                bool ready;
                lock (_sync)
                {
                    if (!_open || s.OpenSeq != _openSeq) return true;
                    _ready = s.State == CityStateUpdate.Ready;
                    ready = _ready;
                }
                _log.Info("blocks: CITY_STATE " + s.State + " for open " + s.OpenSeq + ", " + s.AppliedCount + " applied");
                if (ready && _wantEnter)
                {
                    _wantEnter = false;
                    _player.RequestEnter();
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Player mode asks before entering (Ctrl+Shift+M or a finished start). Null lets it proceed; otherwise
        /// the note to show. Unpaired: asks to enable Minecraft (backup first). Paired: waits for CITY_STATE ready.
        /// </summary>
        public string EnterGate(BridgeHost host, bool connected)
        {
            if (connected && host.NegotiatedAppMinor < 5)
                return "Minecraft side is too old for per-city blocks (app minor " + host.NegotiatedAppMinor + ", needs 5)";
            if (!Paired)
            {
                if (_pairing == Pairing.BackingUp) return "writing the backup save first";
                if (_pairing == Pairing.None) AskToEnable();
                return "confirm in the dialog";
            }
            if (!connected || Ready) return null;
            _wantEnter = true;
            return "waiting for Minecraft to load this city's blocks";
        }

        /// <summary>
        /// Self-test only: pairs a city started from a map without the dialog or a backup (nothing to protect).
        /// Returns null, or why it did not pair.
        /// </summary>
        public string PairForSelfTest()
        {
            if (Paired) return null;
            if (!StartedFromMap) return "the city was loaded from a save (load mode " + _loadMode + "); the self-test never pairs a saved city without a backup";
            _saveId.EnsureAssigned();
            _log.Info("blocks: self-test paired this city without a backup (started from a map, load mode " + _loadMode + ", never saved); save id " + _saveId.Id);
            return null;
        }

        /// <summary>Self-test: sends EDIT_SYNC on the main thread; poll <see cref="SyncAcked"/>. 0 if nothing is open.</summary>
        public uint SendEditSync(BridgeHost host)
        {
            uint seq;
            lock (_sync)
            {
                if (!_open) return 0;
                seq = _openSeq;
            }
            uint token = _barrier.Begin();
            return host.Send(AppProtocol.EditSyncType, new EditSync { OpenSeq = seq, Token = token }.Encode()) ? token : 0;
        }

        public bool SyncAcked(uint token)
        {
            return token != 0 && _barrier.Wait(token, 0);
        }

        public string OverlayText(bool inCity)
        {
            if (!inCity) return "";
            var sb = new StringBuilder("Blocks: ");
            if (_pairing == Pairing.BackingUp) sb.Append("backup '").Append(_backup.Name).Append("': ").Append(_backup.Detail);
            else if (_pairing == Pairing.Confirming) sb.Append("confirm enabling Minecraft in the dialog");
            else if (!Paired) sb.Append("Minecraft not enabled for this city  [Ctrl+Shift+M enables]");
            else
            {
                lock (_sync)
                {
                    sb.Append(_edits.Count).Append(" in this city; Minecraft ")
                      .Append(!_open ? "not open" : _ready ? "ready (open " + _openSeq + ")" : "loading them (open " + _openSeq + ")");
                    if (_unreadable != null) sb.Append("; unreadable record kept (").Append(_unreadable.Length).Append(" bytes)");
                    sb.Append(_playerData != null ? "; player " + _playerData.Length + " bytes" : "; fresh player");
                    if (_playerUnreadable != null) sb.Append("; unreadable player data kept");
                }
                if (_staleBatches > 0) sb.Append("; ").Append(_staleBatches).Append(" stale batches dropped");
            }
            if (_note.Length > 0) sb.Append("  (").Append(_note).Append(')');
            return sb.ToString();
        }

        // BulldozeTool.DeleteTreeImpl: a tree that exists (m_flags != 0) and is not Burning (0x80) is released with
        // TreeManager.ReleaseTree; as every simulation-state change, on the simulation thread. Repeats find m_flags == 0.
        private static void ReleaseTree(uint id)
        {
            if (id == 0 || id >= TreeManager.MAX_TREE_COUNT) return;
            Singleton<SimulationManager>.instance.AddAction(delegate
            {
                TreeManager tm = Singleton<TreeManager>.instance;
                ushort flags = tm.m_trees.m_buffer[id].m_flags;
                if (flags != 0 && (flags & (ushort)TreeInstance.Flags.Burning) == 0) tm.ReleaseTree(id);
            });
        }

        // TREE_GROWN: a sapling grew in Minecraft. As TreeTool.CreateTree does (TreeManager.CreateTree, single: true),
        // on the simulation thread; the prefab comes from the pure TreeRecord.PickPrefab over the loaded TreeInfos
        // (height = generated mesh height at the mean scale, the notion CollisionStreamer's TREES export uses).
        private void GrowTree(TreeGrown g)
        {
            Singleton<SimulationManager>.instance.AddAction(delegate
            {
                try
                {
                    int count = PrefabCollection<TreeInfo>.LoadedCount();
                    var infos = new List<TreeInfo>();
                    var names = new List<string>();
                    var heights = new List<float>();
                    for (uint i = 0; i < count; i++)
                    {
                        TreeInfo info = PrefabCollection<TreeInfo>.GetLoaded(i);
                        if (info == null || info.m_generatedInfo == null) continue;
                        infos.Add(info);
                        names.Add(info.name);
                        heights.Add(info.m_generatedInfo.m_size.y * 0.5f * (info.m_minScale + info.m_maxScale));
                    }
                    int pick = TreeRecord.PickPrefab(names, heights, g.Kind, g.Seed);
                    if (pick < 0)
                    {
                        _log.Warn("trees: TREE_GROWN kind " + g.Kind + " has no loaded prefab");
                        return;
                    }
                    var rnd = new ColossalFramework.Math.Randomizer(g.Seed);
                    uint tree;
                    var pos = new UnityEngine.Vector3(g.X, g.Y, -g.Z);
                    if (!Singleton<TreeManager>.instance.CreateTree(out tree, ref rnd, infos[pick], pos, true))
                    {
                        _log.Warn("trees: TREE_GROWN at " + g.X + "," + g.Z + " not created (tree limit)");
                        return;
                    }
                    _log.Info("trees: grew " + names[pick] + " #" + tree + " at CS " + pos.x + "," + pos.z);
                    CollisionStreamer.RequestResendAtCs(pos.x, pos.z);
                }
                catch (Exception ex)
                {
                    _log.Warn("trees: TREE_GROWN failed: " + ex.Message);
                }
            });
        }

        private void SendOpen(BridgeHost host)
        {
            uint seq;
            List<VoxelEdit> edits;
            byte[] player;
            lock (_sync)
            {
                player = _playerData;
                seq = ++_openSeq;
                edits = _edits.Sorted();
                _open = true;
                _ready = false;
            }
            string city = CityState.Capture().CityName ?? "";
            bool ok = host.Send(AppProtocol.CityOpenType, new CityOpen { OpenSeq = seq, SaveId = _saveId.Id, CityName = city, EditCount = (uint)edits.Count }.Encode());
            if (ok && host.NegotiatedAppMinor >= 11)
                ok = host.Send(AppProtocol.PlayerDataType, new PlayerData { OpenSeq = seq, Data = player ?? new byte[0] }.Encode());
            List<VoxelBatch> batches = VoxelEditSet.Batches(edits, VoxelEditSet.MaxBatchEdits);
            if (batches.Count == 0) batches.Add(new VoxelBatch());
            for (int i = 0; ok && i < batches.Count; i++)
            {
                VoxelBatch b = batches[i];
                var m = new BlockEdits
                {
                    OpenSeq = seq,
                    Flags = i == batches.Count - 1 ? BlockEdits.FlagLast : (byte)0,
                    Palette = b.Palette.ToArray(),
                    Edits = new BlockEdit[b.Positions.Count],
                };
                for (int k = 0; k < m.Edits.Length; k++) m.Edits[k] = new BlockEdit(b.Positions[k].X, b.Positions[k].Y, b.Positions[k].Z, b.States[k]);
                ok = host.Send(AppProtocol.BlockEditsType, m.Encode());
            }
            if (ok)
            {
                _log.Info("blocks: CITY_OPEN " + seq + " for '" + city + "' (" + _saveId.Id + "), " + edits.Count + " edits in " + batches.Count + " batches");
                return;
            }
            lock (_sync) _open = false;
            _log.Warn("blocks: could not send CITY_OPEN " + seq + "; retrying");
        }

        private void AskToEnable()
        {
            string name;
            try { name = CityBackup.NextName(CityState.Capture().CityName, BackupLabel); }
            catch (Exception e)
            {
                _log.Error("backup name", e);
                GameDialogs.Error("Minecraft not enabled", "No name for the backup save could be found: " + e.Message + "\nThe city was not changed.");
                return;
            }
            int generation = _generation;
            _pairing = Pairing.Confirming;
            _log.Info("blocks: asking to enable Minecraft (backup would be '" + name + "')");
            GameDialogs.Confirm("Enable Minecraft for this city?",
                "Before anything changes, a backup of the city as it is now will be saved as\n\n\"" + name + "\"\n\n"
                + "(it appears in the load menu). Afterwards this city keeps the blocks you build in Minecraft, "
                + "stored in the city's own save: save the city to keep them.",
                yes => OnAnswer(yes, name, generation));
        }

        private void OnAnswer(bool yes, string name, int generation)
        {
            if (_pairing != Pairing.Confirming || generation != _generation) return;
            _pairing = Pairing.None;
            if (!yes)
            {
                _note = "not enabled";
                _log.Info("blocks: player declined enabling Minecraft");
                return;
            }
            string error = _backup.Begin(name);
            if (error != null)
            {
                BackupFailed(error);
                return;
            }
            _pairing = Pairing.BackingUp;
            _log.Info("blocks: writing backup save '" + name + "' to " + _backup.FilePath);
        }

        private void UpdateBackup()
        {
            _backup.Update();
            if (_backup.IsRunning) return;
            if (_pairing != Pairing.BackingUp)
            {
                _log.Info("blocks: backup '" + _backup.Name + "' finished after its city was unloaded (" + _backup.Detail + "); nothing paired");
                return;
            }
            _pairing = Pairing.None;
            if (_backup.State != CityBackup.Phase.Verified)
            {
                BackupFailed(_backup.Detail);
                return;
            }
            _saveId.EnsureAssigned();
            _note = "";
            _log.Info("blocks: backup '" + _backup.Name + "' " + _backup.Detail + "; city paired, save id " + _saveId.Id);
            _wantEnter = true;
            _player.RequestEnter();
        }

        private void BackupFailed(string why)
        {
            _note = "backup failed";
            _log.Warn("blocks: backup failed: " + why + "; city stays unpaired");
            GameDialogs.Error("Minecraft not enabled", "The backup save could not be written or verified:\n" + why + "\n\nThe city was not changed and Minecraft stays off for it.");
        }
    }
}
