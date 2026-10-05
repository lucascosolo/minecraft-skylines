# Notes for city pairing and backup (needed before any mod change to a city)

From the decompiled game, 2026-10-05 (research report; file:line in Assembly-CSharp/ColossalManaged):

- The game keeps no "currently loaded save file" after load: `LoadingManager.LoadLevel(Package.Asset, ...)`
  (LoadingManager.cs:427) only reads the asset's stream into `SimulationManager.m_metaData`. The file
  path lives on the package passed by the load panel (`Package.packagePath`, Package.cs:660); cloud
  saves are `cloud:` assets read through `PlatformService.cloud` (PackageManager.cs:17,98,466-472),
  not files under `DataLocation.saveLocation` (`<localApplicationData>/Saves`, `.crp`).
- Saving: `SavePanel.SaveGame` (private, SavePanel.cs:442) serializes to `Saves/Temp/<guid>.ccs`, then
  `Package.Save` writes through `SafeFileStream` (temp file, rename; its `.bck` is transient).
  `OnSaveData` runs before the `.crp` is written. No public "saved" or "Save As" event.
- Stable per-city id: `SimulationMetaData.m_gameInstanceIdentifier` (GUID string, SimulationMetaData.cs:25),
  created with the city, kept across renames and Save As; saves older than data version 161 get a
  new random GUID on every load (not stable for them).

Chosen direction (to confirm in the implementation chunk): when the player first enables Minecraft
for a city, write a **separate backup save** through the game's own save routine (works for local
and cloud saves alike and captures the state right before the mod changes anything), named
"<city> (before Minecraft) <timestamp>", verify it exists and has a plausible size, and only then
store our `saveId` in the city. Find a public route to the save routine (SavePanel instance method,
LoadingManager.SaveGame-style API) in the decompile before considering Harmony. Pairing key:
our own `saveId` (stored in the save, so Save As forks share it, like the game's GUID); a fork
detection rule (same saveId, different file) is part of that chunk.
