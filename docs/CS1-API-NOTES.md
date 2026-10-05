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
| Terrain clip probe (T1) | `TerrainManager.RegisterTerrainManager(ITerrainManager)` (public static); `ITerrainManager.TerrainUpdated(TerrainArea, TerrainArea, TerrainArea)`, `AfterTerrainUpdate(...)` | verified (signature) | no | no unregister API; `Skylines.Host.Terrain.TerrainClipMask` stays registered, inert when empty |
| Terrain clip probe (T1) | `TerrainModify.ApplyQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Edges, Heights, Surface)`; `Edges.None`, `Heights.None`, `Surface.Clip` | verified (signature) | no | only effective inside `TerrainUpdated` during a surface recompute (TerrainModify.cs:646) |
| Terrain clip probe (T1) | `TerrainModify.UpdateArea(float minX, float minZ, float maxX, float maxZ, bool heights, bool surface, bool zones)` | verified (signature) | no | called surface-only, from the simulation thread |
| Terrain clip probe (T1) | `SimulationManager.AddAction(Action)` → `AsyncAction` | verified (signature) | no | runs on the sim thread inside Begin/EndUpdateArea, also while paused |
| Terrain clip probe (T1) | `TerrainManager.RayCast(Segment3, out Vector3)`; `ColossalFramework.Math.Segment3(Vector3, Vector3)` | verified (signature) | no | cursor ray from `Camera.main.ScreenPointToRay(Input.mousePosition)` |
| Terrain clip probe (T1) | `TerrainManager.GetSurfaceCell(int x, int z)` (4320² detail cells); `SurfaceCell.m_clipped/m_ruined/m_gravel/m_field/m_pavementA/m_pavementB` (byte) | verified (signature) | no | falls back to raw surface where the patch has no detail |
| Terrain clip probe (T1) | `TerrainManager.m_patches` (`TerrainPatch[]`, 9×9, index z*9+x), `TerrainPatch.m_simDetailIndex/m_tmpDetailIndex/m_rndDetailIndex` (int), `TerrainManager.m_detailPatchCount` | verified (signature) | no | detail patches = owned tiles (`SetDetailedPatch`) |
| Terrain clip probe (T1) | Unity `Camera.main`, `Camera.ScreenPointToRay`, `Input.mousePosition`, `Input.GetKey/GetKeyDown(KeyCode)` | compiled against UnityEngine 5.6 | no | |
| Patching | Harmony 2 via the CitiesHarmony workshop item 2040656402 | hypothesis | — | **not subscribed** on this PC; avoid until a hook needs it |
| Roads, buildings (M2) | `NetManager.m_segments`, `BuildingManager.m_buildings`, mesh readability | hypothesis | no | to verify when M2 starts |
| Collision (M2) | `TerrainManager.SampleDetailHeightSmooth(Vector3 worldPos)` (public, TerrainManager.cs:1449) | verified (compiles) | no | used instead of the `(float x, float z)` overload (TerrainManager.cs:2552), which takes detail-grid coordinates (`x/4+2160`) and returns raw ushort units (m * 64); the Vector3 overload converts both. `Skylines.Host.Geometry.TerrainSampler` |
| Collision (M2) | `NetManager.m_segments.m_buffer` (`NetSegment[]`), `NetManager.m_nodes.m_buffer` (`NetNode[]`), `NetManager.MAX_SEGMENT_COUNT` = 36864 | verified | no | read on the main thread while the simulation thread may write; guarded by try/catch in the streamer |
| Collision (M2) | `NetManager.m_segmentGrid` (`ushort[72900]`, 270x270 cells of 64 m, index `z*270+x`, cell = `(int)(world/64+135)` clamped), chained by `NetSegment.m_nextGridSegment` | verified (NetManager.InitializeSegment, NetManager.cs:3635) | no | segments are filed under the midpoint of their end nodes, so `NetGeometry` searches 256 m beyond the rectangle and then tests `NetSegment.m_bounds` |
| Collision (M2) | `NetSegment.m_flags` (`Created`, `Deleted`), `m_startNode`, `m_endNode`, `m_startDirection`, `m_endDirection`, `m_bounds` (Bounds), `Info` (NetInfo) | verified | no | `NetSegment.Flags.Created`/`Deleted` tested before use |
| Collision (M2) | `NetSegment.CalculateMiddlePoints(Vector3 startPos, Vector3 startDir, Vector3 endPos, Vector3 endDir, bool smoothStart, bool smoothEnd, out Vector3 middlePos1, out Vector3 middlePos2)` (public static, NetSegment.cs:2416) | verified | no | same call the game makes for segment name data (NetSegment.cs:384): bezier = start node pos, middlePos1, middlePos2, end node pos. Centre line only; `GenerateBezier` (NetSegment.cs:1918) returns left/right edge curves with corner adjustments and was not needed |
| Collision (M2) | `NetInfo.m_halfWidth` (float), `NetInfo.m_surfaceLevel` (float), `NetInfo.m_netAI` (NetAI) | verified | no | surface y = node y + `m_surfaceLevel` (as `GenerateBezier` does) |
| Collision (M2) | `NetAI.IsUnderground()`, `NetAI.IsBridge()` (public virtual bool, NetAI.cs:290, :1030) | verified | no | bridge: `RoadBridgeAI`, `PedestrianBridgeAI` return true from `IsBridge`; underground: `RoadTunnelAI`, `PedestrianTunnelAI`, `TrainTrackTunnelAI`, `MetroTrackTunnelAI` return true, `PedestrianPathAI`/`PedestrianWayAI` return `m_underground`. Elevated roads use `RoadBridgeAI`. Only AIs deriving from `RoadBaseAI`, `PedestrianPathAI`, `PedestrianBridgeAI`, `PedestrianWayAI`, `TrainTrackBaseAI`, `MetroTrackBaseAI` are exported (power lines, pipes, cable cars are not) |
| Collision (M2) | `NetNode.m_position`, `NetNode.m_flags` (`NetNode.Flags.Underground`), `NetNode.GetSegment(int 0..7)` | verified | no | junction discs |
