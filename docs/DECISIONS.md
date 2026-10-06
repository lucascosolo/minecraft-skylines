# Decisions

Newest last. Each entry says what was chosen, what was rejected, and why.

## 2026-10-05: play happens entirely inside Cities: Skylines

The owner confirmed the SkyCraft model: CS1 is the only window the player uses and it draws
everything; Minecraft Java runs hidden in the background and supplies the player's physics,
inventory, crafting and block logic. Rejected: playing in Minecraft with the city imported as
blocks (loses CS1's simulation and visuals, which is the point).

## 2026-10-05: target the original Cities: Skylines and Minecraft Java 26.3 + Fabric

Minecraft 26.3 is the newest stable release on meta.fabricmc.net (checked 2026-10-05) and is what
SkyCraft targets, so its MIT-licensed Fabric code (collision, digging, mesh export, input replay)
can be reused with the fewest changes. 26.x ships unobfuscated, so Mixins use Mojang names. The
CS1 version is pinned once the owner's install is inspected.

## 2026-10-05: SKBR over TCP loopback instead of SkyCraft's shared memory

Rejected for the control plane: SkyCraft's Win32 named shared memory. It is Windows-only, CS1's
.NET 3.5 profile has no `MemoryMappedFiles`, and detecting a dead peer needs heartbeats on both
sides anyway. Chosen: a framed TCP protocol (`protocol/bridge-v1.md`) with a handshake, heartbeats
and GOODBYE; I/O on background threads so neither game's main thread blocks. Shared memory stays
the plan for the GUI overlay's pixels (milestone 3), negotiated over the bridge.

## 2026-10-05: CS1 hosts the listening socket

CS1 is the game the player starts and the one that owns the city; Minecraft is started beside it
and connects, retrying. Mirrors SkyCraft (Skyrim creates the mapping, Minecraft opens it).

## 2026-10-05: reusable layers (the owner's requirement)

`Skylines.Bridge` (transport) and the Java `bridge` module know nothing about either game;
`Skylines.Host` knows CS1 but not Minecraft; only `MinecraftSkylines.*` and the Fabric mod know
Minecraft. A future "CS1 + another game" project lifts the first three. See `ARCHITECTURE.md`.

## 2026-10-05: one block = one metre, z flipped

CS1 units are metres; a Minecraft block is a metre (the player is 1.8 blocks, a CS1 citizen is
about 1.8 m). No scale factor, so collision and placement need no rounding policy. Unity is
left-handed, Minecraft right-handed: `mc.z = -cs.z`, `yaw = unityY + 180`. Verified against the
two games' forward-vector formulas in `gen_vectors.py`.

## 2026-10-05: protocol tested by an independent reference

The Python reference (`protocol/reference/`) generates golden vectors and runs a black-box
conformance suite against each implementation; the suite itself was validated against a Python
reference peer (27/27). This substitutes for separately authored unit tests on the codecs: the
expectations come from a third implementation, not from the code under test.

## 2026-10-05: quarantine instead of deletion; no clean tasks

Owner's rule: no `rm`, no deletion through other means. Unwanted files go to `_quarantine/`.
MSBuild's `IncrementalClean` (which deletes stale outputs) is overridden to do nothing.

## 2026-10-05: player safety, layman install, frictionless uninstall (owner's requirements)

The mod must be installable by ordinary CS1/Minecraft players, must never put their cities at risk,
and uninstalling must return them to normal play with little friction.

- **Nothing changes a city until the player switches it to Minecraft mode.** A city merely loaded
  with the mod enabled is saved exactly as it would be without the mod (milestone 1 writes no data
  into saves at all).
- **First switch per city: automatic backup, then pairing.** The mod copies the city's save file to a
  new backup file (never overwriting anything), verifies the copy, and only then assigns the
  `saveId` and allows terrain changes. Where an automatic copy is impossible (e.g. a cloud-only save
  whose file cannot be located), the player gets a strong recommendation to back up and must
  confirm explicitly; until then digging stays off. The owner chose persisted terrain edits plus a
  backup over runtime-only changes, because persisted edits are far simpler.
- **What persists after uninstall, read in the decompiled save code:** mod data entries
  (`SimulationManager.m_serializableDataStorage`) are kept and ignored by the vanilla game (17 bytes
  for the `saveId`); terrain **heights** persist (`TerrainManager.Data` saves `rawHeights` and
  `blockHeights`), so pits stay dug, which is what the backup is for; the 4 m terrain **clip mask**
  is not saved (rebuilt on load), so tunnel entrances close up harmlessly without the mod.
- **Every mod callback is exception-guarded** so a bug in the mod cannot abort a game load or save.
- **Distribution (later milestone; publishing needs the owner's explicit yes):** CS1 side as a Steam
  Workshop item; Minecraft side as a ready-made instance the CS1 mod starts (SkyCraft bundles Prism
  Launcher this way; the player's Microsoft sign-in stays in the launcher). Must work on Windows and
  macOS as well as Linux, which the TCP bridge already allows. Avoid requiring Harmony unless a hook
  truly needs it.
- **Uninstall test** joins the exit criteria of every milestone that changes a city (M4-M7): save
  with the mod, disable it, reload in vanilla: it loads cleanly and only documented changes remain.

## 2026-10-05: Minecraft is started with its own JDK from the home folder

Steam runs Cities: Skylines inside its Linux runtime container, where the host's `/usr` (and
with it `/usr/bin/java` and `/usr/lib/jvm`) is not visible, while the home folder is (owner's
runs: "no java on PATH", then "JAVA_HOME is set to an invalid directory"). Chosen: a self-contained
Eclipse Temurin 25 JDK under `~/.cache/minecraft-skylines/jdk/` (`tools/fetch-jdk.sh`, SHA-256
checked; needs only glibc 2.17). Rejected: reaching the host's Java through the container's
`/run/host` (host-glibc binaries inside an older runtime), or asking Steam to escape the
container (not available by default). This mirrors what a player release needs anyway (SkyCraft's
bundled launcher brings its own Java).

## 2026-10-06: the city save is the authority for Minecraft blocks; pairing only after a verified backup

Owner chose save/pairing (M4) before realistic tunnels. The host keeps each city's block edits and
writes them into the save; Minecraft's world (`skylines-city`) is a cache rebuilt on every city open,
so older saves, Save As forks and quitting without saving behave for blocks like the rest of the city,
and no Minecraft world ever needs removing. Rejected: one Minecraft world per city (reverting a save
would not revert blocks; worlds pile up). A city gets mod data only after the player says yes and a
backup save written by the game's own routine is verified on disk. Details: `docs/plans/m4.md`.

## 2026-10-06: the player belongs to the city (protocol 1.11)

The player's own Minecraft data (gzip NBT of `ServerPlayer.saveWithoutId`: inventory, armour, offhand, item
components, health, food, XP, effects, slot, game mode, spawn point, fall distance, air) is an opaque blob the host
keeps in the city's save under `MinecraftSkylines.PlayerData` (`u8 version 1, u32 length, bytes, u32 CRC-32`;
unreadable: fresh player, raw bytes kept under `.unreadable`), sent with `PLAYER_DATA` after every `CITY_OPEN` and
returned by the guest before every `EDIT_SYNC_ACK` and when it changed (checked every 10 s). Rejected: a
field-by-field record of our own (item components and future fields need Minecraft's own codecs anyway; the NBT
carries `DataVersion`, so Minecraft's data fixer upgrades it). Identity and placement (UUID, position, rotation,
motion, dimension) are never applied from it. Survival is the default; creative only via `DEBUG_COMMAND /gamemode`.
Respawn: immediate; without a bed or anchor of its own the guest sends `RESPAWN_REQUEST` and the host re-enters at
the x, z of its last `ENTER_PLAYER_MODE` on the highest walkable surface computed afresh. Rejected: the host sending
a fallback spawn with `CITY_OPEN` (the entry spot changes with every entry, and a host teleport brings collision
loading and the hold with it). Advancements and statistics stay in the cache world (separate files), not per city.

