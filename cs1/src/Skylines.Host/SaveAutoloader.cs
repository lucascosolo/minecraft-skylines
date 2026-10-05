using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ColossalFramework;
using ColossalFramework.Packaging;
using ColossalFramework.PlatformServices;
using ColossalFramework.UI;
using UnityEngine.SceneManagement;

namespace Skylines.Host
{
    /// <summary>
    /// launch.cfg <c>autoload</c>: once per game start, when the main menu has been idle for a few seconds, loads the
    /// named save exactly as the Load panel's Load button does (LoadPanel.LoadRoutine), without its dialogs.
    /// Never saves. Main thread only.
    /// </summary>
    public sealed class SaveAutoloader
    {
        /// <summary>What one <see cref="Update"/> did.</summary>
        public enum Outcome
        {
            /// <summary>Nothing done this frame.</summary>
            None,
            /// <summary>LoadLevel was called.</summary>
            Loaded,
            /// <summary>No save has that name; the names were logged.</summary>
            NotFound,
            /// <summary>Enumerating or loading threw; logged.</summary>
            Failed,
        }

        private const double SettleSeconds = 5;

        // Once per game start: survives a mod disable/enable (the static lives as long as the game's AppDomain).
        private static bool s_done;

        private readonly HostLog _log;
        private readonly string _wanted;
        private readonly Stopwatch _menuIdle = new Stopwatch();

        /// <param name="log">Mod log.</param>
        /// <param name="wanted">launch.cfg <c>autoload</c>; empty means off.</param>
        public SaveAutoloader(HostLog log, string wanted)
        {
            _log = log;
            _wanted = (wanted ?? "").Trim();
        }

        /// <summary>Per frame. Returns Loaded, NotFound or Failed on the one frame it acts, None otherwise.</summary>
        public Outcome Update()
        {
            if (s_done || _wanted.Length == 0) return Outcome.None;
            if (!MainMenuIdle())
            {
                _menuIdle.Reset();
                return Outcome.None;
            }
            if (!_menuIdle.IsRunning) _menuIdle.Start();
            if (_menuIdle.Elapsed.TotalSeconds < SettleSeconds) return Outcome.None;
            s_done = true;
            try
            {
                return LoadNamed();
            }
            catch (Exception e)
            {
                _log.Error("autoload '" + _wanted + "'", e);
                return Outcome.Failed;
            }
        }

        /// <summary>Quits the way the pause menu's and main menu's Quit buttons do (PauseMenu.cs:336, MainMenu.cs:294): no save.</summary>
        public static void QuitGame()
        {
            Singleton<LoadingManager>.instance.QuitApplication();
        }

        // Main menu up and nothing loading: the intro coroutine has finished (LoadIntroComplete clears m_currentlyLoading,
        // LoadingManager.cs:1762), no city is loaded (m_loadingComplete false) and the MainMenu scene is loaded
        // (LoadingManager.cs:508/553). LoadRoutine itself refuses while saving or loading (LoadPanel.cs:474).
        private static bool MainMenuIdle()
        {
            if (!Singleton<LoadingManager>.exists) return false;
            LoadingManager lm = Singleton<LoadingManager>.instance;
            return !lm.m_currentlyLoading && !lm.m_loadingComplete && !lm.m_applicationQuitting && !SavePanel.isSaving
                && SceneManager.GetSceneByName("MainMenu").isLoaded;
        }

        private Outcome LoadNamed()
        {
            var assets = new List<Package.Asset>();
            var metas = new List<SaveGameMetaData>();
            var candidates = new List<SaveCandidate>();
            // The Load panel's list (LoadPanel.Refresh, LoadPanel.cs:352-359).
            foreach (Package.Asset a in PackageManager.FilterAssets(UserAssetType.SaveGameMetaData))
            {
                if (PackageHelper.IsDemoModeSave(a) || a == null || !a.isEnabled) continue;
                SaveGameMetaData m;
                try { m = a.Instantiate<SaveGameMetaData>(); }
                catch (Exception e) { _log.Warn("autoload: save '" + a.name + "' failed to load its metadata: " + e.Message); continue; }
                if (m == null) continue;
                assets.Add(a);
                metas.Add(m);
                candidates.Add(new SaveCandidate
                {
                    PackageName = a.package != null ? a.package.packageName : null,
                    AssetName = a.name,
                    CityName = m.cityName,
                    Timestamp = m.timeStamp,
                });
            }
            int i = SaveMatch.Pick(candidates, _wanted);
            if (i < 0)
            {
                var sb = new StringBuilder();
                foreach (SaveCandidate c in candidates)
                {
                    sb.Append(sb.Length == 0 ? "" : "; ").Append(c.PackageName).Append(" (asset '").Append(c.AssetName)
                      .Append("', city '").Append(c.CityName).Append("', ").Append(c.Timestamp.ToString("yyyy-MM-dd HH:mm")).Append(')');
                }
                _log.Warn("autoload: no save named '" + _wanted + "'; nothing loaded. Available (" + candidates.Count + "): " + sb);
                return Outcome.NotFound;
            }
            Package.Asset asset = assets[i];
            SaveGameMetaData meta = metas[i];
            _log.Info("autoload: loading '" + asset.name + "' (package " + candidates[i].PackageName + ", city '" + meta.cityName
                + "', saved " + meta.timeStamp.ToString("yyyy-MM-dd HH:mm") + ") as LoadPanel.LoadRoutine does");
            Load(asset, meta);
            return Outcome.Loaded;
        }

        // LoadPanel.LoadRoutine (LoadPanel.cs:472-505) minus its UI calls (CloseEverything, PrintModsInfo, Hide).
        // The theme override dropdown defaults to the save's own mapThemeRef (SelectThemeOverride, LoadSavePanelBase.cs:174),
        // so an untouched panel loads with meta.mapThemeRef.
        private static void Load(Package.Asset asset, SaveGameMetaData meta)
        {
            SavePanel.lastLoadedName = asset.name;
            SavePanel.lastCloudSetting = asset.package != null && PackageManager.IsCloudPath(asset.package.packagePath);
            var ngs = new SimulationMetaData
            {
                m_CityName = meta.cityName,
                m_updateMode = SimulationManager.UpdateMode.LoadGame,
                m_environment = ForcedEnvironment(),
            };
            if (asset.package != null && asset.package.GetPublishedFileID() != PublishedFileId.invalid)
            {
                ngs.m_disableAchievements = SimulationMetaData.MetaBool.True;
            }
            if (meta.mapThemeRef != null)
            {
                Package.Asset theme = PackageManager.FindAssetByName(meta.mapThemeRef, UserAssetType.MapThemeMetaData);
                if (theme != null)
                {
                    ngs.m_MapThemeMetaData = theme.Instantiate<MapThemeMetaData>();
                    ngs.m_MapThemeMetaData.SetSelfRef(theme);
                }
            }
            Singleton<LoadingManager>.instance.LoadLevel(asset, "Game", "InGame", ngs);
        }

        // LoadPanel.m_forceEnvironment is a serialized prefab field; read it from the live panel as
        // GameKeyShortcuts.cs:1024 finds it.
        private static string ForcedEnvironment()
        {
            LoadPanel panel = UIView.library != null ? UIView.library.Get<LoadPanel>("LoadPanel") : null;
            return panel != null ? panel.m_forceEnvironment : null;
        }
    }
}
