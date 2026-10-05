# CS1 API notes: verified versus hypothesis

Every CS1 or Unity API the mod relies on gets a row here. **`verified` = the signature was read
in the owner's assemblies** (decompiled with ilspycmd 11.1 into
`~/.cache/minecraft-skylines/decompile/`, local only, never committed) **and the code compiles
against them.** It does not mean the behaviour was observed in the running game; that column is
separate.

Assemblies: Cities: Skylines Steam build 22724702, native Linux, Unity 5.6.7f1
(`refs/environment.txt`, collected 2026-10-05; SHA-256 of each DLL recorded there).

| Area | API | Signature checked | Seen working in game | Notes |
|---|---|---|---|---|
| Mod entry | `ICities.IUserMod { string Name; string Description; }` | verified | no | |
| Enable/disable | public `OnEnabled()` / `OnDisabled()` found by reflection | verified (`ColossalFramework.Plugins.PluginManager` calls them at startup and on toggle) | no | |
| Mod DLL loading | every `*.dll` in the mod folder, recursively | verified (`PluginManager`: `Directory.GetFiles(modPath, "*.dll", AllDirectories)`) | no | our four DLLs go in one folder |
| Mods folder | `DataLocation.modsPath` = `localApplicationData/Addons/Mods`; Linux `localApplicationData` = `${XDG_DATA_HOME:-~/.local/share}/Colossal Order/Cities_Skylines` | verified | no | `tools/install-cs1-mod.sh` |
| City lifecycle | `LoadingExtensionBase.OnLevelLoaded(LoadMode)`, `OnLevelUnloading()` | verified | no | |
| Per-frame in a city | `ThreadingExtensionBase.OnUpdate(float realTimeDelta, float simulationTimeDelta)` | verified | — | not used: runs only in a city; we use our own `MonoBehaviour` (`Skylines.Host.MainThreadPump`) so the link works in the main menu |
| Save data | `SerializableDataExtensionBase.OnLoadData()/OnSaveData()`, `serializableDataManager` (`ISerializableData`: `string[] EnumerateData()`, `byte[] LoadData(string)`, `void SaveData(string, byte[])`) | verified | no | `Skylines.Host.SaveIdentity` |
| Game version | `BuildConfig.applicationVersion` (static string) | verified | no | |
| City name | `SimulationManager.instance.m_metaData.m_CityName` | verified | no | |
| Pause | `SimulationManager.SimulationPaused` (bool property) | verified | no | |
| Loading | `LoadingManager.instance.m_currentlyLoading` (volatile bool) | verified | no | |
| Singletons | `Singleton<T>.instance`, `Singleton<T>.exists` | verified | no | |
| Log folder | `DataLocation.localApplicationData` | verified | no | our log: `<that>/ModLogs/MinecraftSkylines.log` |
| Camera (M2) | `ICities.ICamera`: `SetInteractive(bool)`, `SetPosition(x,y,z,bool)`, `SetRotation(h,v,tilt)`, `SetFieldOfView(float)`, `SetNearClipPlane(float)` via `CameraExtensionBase.OnCreated(ICamera)` | verified (interface) | no | an official camera API; check its implementation before relying on it for per-frame control |
| Terrain heights (M2) | `ICities.ITerrain`: `heightMapResolution`, `cellSize`, `RawToHeight`, `GetHeights(x,z,w,l,ushort[])`; `TerrainManager.m_finalHeights` etc. | verified (interface; raw grid 1081×1081, 16 m cells, ushort/64 = m, max 1024 m) | no | detail grid at 4 m also exists |
| Terrain rendering (M5/M6) | `TerrainManager.EndRenderingImpl` draws each `TerrainPatch` with `Graphics.DrawMesh(sharedFlatMesh, matrix, m_terrainMaterial, m_terrainLayer, null, 0, m_materialBlock1, true, true)`; heights come from a texture | read in code | no | no CPU terrain mesh to cut; see MILESTONES spike T1 |
| Terrain clip mask (M5/M6) | `SurfaceCell.m_clipped` (4 m cells) written into `_SurfaceTexA` red channel; set by `TerrainModify` surface `Clip` via `ApplyQuad` | read in code | no | what the shader does with it is **unverified** (shader not decompilable) |
| Terrain edits (M5) | `TerrainModify.BeginUpdateArea()`, `UpdateArea(float minX, float minZ, float maxX, float maxZ, bool heights, bool surface, bool zones)`, `EndUpdateArea()` | verified | no | |
| Terrain raycast | `TerrainManager.RayCast(Segment3, out Vector3)` on `m_finalHeights` (16 m cells) | read in code | no | CS1's own picking ignores our voxels |
| Custom drawing (M3) | `RenderManager.RegisterRenderableManager(IRenderableManager)`; `IRenderableManager.EndRendering(RenderManager.CameraInfo)` etc. | verified (signatures) | no | where we draw MC block meshes |
| UI | Unity IMGUI (`GUI.Box`, `GUIStyle`, `Event.current`) from `MonoBehaviour.OnGUI` | compiled against UnityEngine 5.6 | no | chosen over ColossalFramework.UI for M1: works in menus, no CS UI dependency |
| Patching | Harmony 2 via the CitiesHarmony workshop item 2040656402 | hypothesis | — | **not subscribed** on this PC; avoid until a hook needs it |
| Roads, buildings (M2) | `NetManager.m_segments`, `BuildingManager.m_buildings`, mesh readability | hypothesis | no | to verify when M2 starts |
