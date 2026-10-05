# CS1 API notes: verified versus hypothesis

Every CS1 or Unity API the mod relies on gets a row here. **Status `hypothesis` means recalled
from general CS1 modding knowledge and not yet checked**; such an API must not be relied on until
it is `verified` against the owner's assemblies (`~/.cache/minecraft-skylines/refs/cs1/Managed/`,
collected by `tools/collect-cs1-refs.sh`), with the assembly and exact signature recorded.

Assembly versions checked against: _none yet_ (the assemblies are not available to the sandbox as of
2026-10-05).

| Area | API | Status | Needed for | Notes |
|---|---|---|---|---|
| Mod entry | `ICities.IUserMod` (`Name`, `Description`) | hypothesis | M1 | Mod discovered from a DLL in `Addons/Mods/<name>/` |
| Lifecycle | `ICities.LoadingExtensionBase.OnLevelLoaded(LoadMode)`, `OnLevelUnloading()` | hypothesis | M1 | city load/unload status |
| Per-frame hook | `ICities.ThreadingExtensionBase.OnUpdate(float realTimeDelta, float simulationTimeDelta)` | hypothesis | M1 | drains the bridge once a frame on the main thread; alternative is our own `MonoBehaviour.Update` |
| Mod data in saves | `ICities.SerializableDataExtensionBase`, `ISerializableData.SaveData(string, byte[])` / `LoadData(string)` | hypothesis | M1 (`saveId`), M4 | save identity |
| Game version | `BuildConfig.applicationVersion` (or similar) | hypothesis | M1 | sent in `HOST_STATUS.gameVersion` |
| UI status | `ColossalFramework.UI.UIView`, `UILabel` | hypothesis | M1 | connection status label; fallback is Unity `OnGUI` |
| Logging | `UnityEngine.Debug.Log` → Player.log; plus our own file | hypothesis | M1 | |
| Camera | `CameraController` (MonoBehaviour on the main camera); disable it and drive `Camera.main.transform` | hypothesis | M2 | the pattern first-person camera mods use |
| Input | `UnityEngine.Input` (keys, mouse deltas), cursor lock via `Cursor.lockState` | hypothesis | M2 | Unity 5.6 API |
| Terrain heights | `TerrainManager.instance`, raw height array (`ushort`, 1/64 m units), `SampleRawHeightSmooth(Vector3)` | hypothesis | M2 | 1081×1081 raw grid at 16 m for the full 9×9-tile map: to verify |
| Terrain edits | `TerrainModify.UpdateArea(...)` | hypothesis | M5 | pits only |
| Terrain rendering | how terrain patches are meshed and drawn; whether a region can be suppressed | **unknown** | M5/M6 (spike T1) | highest-risk unknown |
| Roads | `NetManager.instance.m_segments.m_buffer`, `NetSegment` (start/end node, directions, `Info.m_halfWidth`), `NetNode.m_position` | hypothesis | M2 | road and bridge surfaces as triangles |
| Buildings | `BuildingManager.instance.m_buildings.m_buffer`, `Building.m_position/m_angle`, `BuildingInfo.m_mesh` readability | hypothesis | M2 | collision from meshes if readable |
| Custom drawing | `Graphics.DrawMesh` from a render hook (`RenderManager` renderable managers, or `Camera.onPreCull`) | hypothesis | M3 | which shader to use for MC blocks is open |
| Patching | Harmony 2 via `CitiesHarmony.API` (workshop 2040656402) | hypothesis | M2+ | only where no public hook exists |
| Engine | Unity 5.6.7f1, Mono with .NET 3.5 profile | hypothesis | all | read from `refs/environment.txt` |
