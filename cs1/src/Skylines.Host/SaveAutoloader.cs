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
    /// named save exactly as the Load panel's Load button does (LoadPanel.LoadRoutine), or, for <c>new:&lt;map&gt;</c>,
    /// starts a new game on that map as the New Game panel's Start button does (NewGamePanel.StartNewGameRoutine),
    /// without their dialogs. Never saves. Main thread only.
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

        /// <summary>City name of a new game started by <c>autoload = new:&lt;map&gt;</c>.</summary>
        public const string NewGameCityName = "MCSK Self-Test";

        // Once per game start: survives a mod disable/enable (the static lives as long as the game's AppDomain).
        private static bool s_done;

        private static string s_newGameId;

        /// <summary>
        /// True while the loaded city is the new game this process started (its m_gameInstanceIdentifier is the one we
        /// generated), never for a save. Main thread, in a city.
        /// </summary>
        public static bool InOurNewGame()
        {
            SimulationMetaData meta = Singleton<SimulationManager>.exists ? Singleton<SimulationManager>.instance.m_metaData : null;
            return s_newGameId != null && meta != null && meta.m_gameInstanceIdentifier == s_newGameId;
        }

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
                string map = AutoloadTarget.NewGameMap(_wanted);
                return map == null ? LoadNamed() : StartNamedMap(map);
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

        private Outcome StartNamedMap(string wanted)
        {
            var assets = new List<Package.Asset>();
            var metas = new List<MapMetaData>();
            var candidates = new List<SaveCandidate>();
            // The New Game panel's list (NewGamePanel.Refresh, NewGamePanel.cs:468-477). Built-in maps are packages under
            // DataLocation.gameContentPath/Maps, loaded into the same PackageManager as user maps (PackageManager.cs:485).
            foreach (Package.Asset a in PackageManager.FilterAssets(UserAssetType.MapMetaData))
            {
                if (a == null || !a.isEnabled) continue;
                MapMetaData m;
                try { m = a.Instantiate<MapMetaData>(); }
                catch (Exception e) { _log.Warn("autoload: map '" + a.name + "' failed to load its metadata: " + e.Message); continue; }
                if (m == null || !m.isPublished) continue;
                assets.Add(a);
                metas.Add(m);
                candidates.Add(new SaveCandidate
                {
                    PackageName = a.package != null ? a.package.packageName : null,
                    AssetName = a.name,
                    CityName = m.mapName,
                    Timestamp = m.timeStamp,
                });
            }
            int i = SaveMatch.Pick(candidates, wanted);
            if (i < 0)
            {
                var sb = new StringBuilder();
                for (int k = 0; k < candidates.Count; k++)
                {
                    sb.Append(k == 0 ? "" : "; ").Append(candidates[k].PackageName).Append(" (asset '").Append(candidates[k].AssetName)
                      .Append("', map '").Append(candidates[k].CityName).Append("'").Append(metas[k].builtin ? ", built-in" : "").Append(')');
                }
                _log.Warn("autoload: no map named '" + wanted + "'; no new game started. Available (" + candidates.Count + "): " + sb);
                return Outcome.NotFound;
            }
            Package.Asset asset = assets[i];
            MapMetaData meta = metas[i];
            _log.Info("autoload: new game '" + NewGameCityName + "' on map '" + meta.mapName + "' (package " + candidates[i].PackageName
                + ", asset '" + asset.name + "', built-in " + meta.builtin + ") as NewGamePanel.StartNewGameRoutine does");
            StartNewGame(asset, meta);
            return Outcome.Loaded;
        }

        // NewGamePanel.StartNewGameRoutine (NewGamePanel.cs:845-871) minus the panel's Hide, with its defaults: invert traffic
        // unchecked, the map's own theme (the override dropdown preselects it, NewGamePanel.cs:255 and :793-803). The
        // disaster question (CheckForDisasterActivation, :823) only shows a dialog; skipping it leaves the setting as it is.
        private static void StartNewGame(Package.Asset asset, MapMetaData meta)
        {
            s_newGameId = Guid.NewGuid().ToString();
            var ngs = new SimulationMetaData
            {
                m_CityName = NewGameCityName,
                m_gameInstanceIdentifier = s_newGameId,
                m_invertTraffic = SimulationMetaData.MetaBool.False,
                m_disableAchievements = SimulationMetaData.MetaBool.False,
                m_startingDateTime = DateTime.Now,
                m_currentDateTime = DateTime.Now,
                m_newGameAppVersion = BuildConfig.APPLICATION_VERSION,
                m_updateMode = SimulationManager.UpdateMode.NewGameFromMap,
                m_MapName = meta.mapName,
            };
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

        /// <summary>
        /// Holds the game's autosave for the rest of this level (AutoSaveTimer.Pause, LoadingManager.cs:99; the counter
        /// survives the Start that follows OnLevelLoaded, LoadingManager.cs:1834, and nothing is written to settings).
        /// </summary>
        public static void PauseAutosave()
        {
            Singleton<LoadingManager>.instance.autoSaveTimer.Pause();
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
