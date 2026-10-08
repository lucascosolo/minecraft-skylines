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

## 2026-10-06: trees are Minecraft trees (protocol 1.12)

The host lists every drawn `TreeManager` tree per collision region (`TREES`, after that region's `COLLISION_REGION`:
id, trunk base, height, radius, kind from the `TreeInfo` name) and the guest places a Minecraft tree for each; the
player felling one sends `TREE_FELLED` and the host releases it as the bulldozer does (`TreeManager.ReleaseTree` via
`SimulationManager.AddAction`). The kind mapping lives in `MinecraftSkylines.Protocol` (`TreeRecord.KindOf`), the
region collection in `Skylines.Host` (`ObstacleGeometry.TreeSamples`, Minecraft-free). Rejected: a separate tree
scan (the obstacle tree loop already visits exactly the trees of a region); sending building and lane decoration
trees (not `TreeManager` trees, nothing to release).

## 2026-10-06: the shadow world (guest), and entities on the host's triangles

`ShadowWorld` (Fabric, `dev.mcskylines.shadow`) fills the city world with real blocks rebuilt per chunk from the
streamed collision triangles and `TREES`: ground (grass/sand/stone top by slope and the 1.10 water grid, dirt or sand,
stone, deepslate below y 0, bedrock at y -64, Minecraft-like ore bands and stone pockets, seeded by the city's saveId
and position), gray concrete under roads (a one-block deck on bridges), `minecraft:barrier` in the lowest 4 cells of
building volumes, short/tall grass on grass-topped open ground, and trees. A cell is solid when its centre is under
the surface. Ground is a 6-cell crust that deepens by 6 under the 3x3 columns wherever the player digs at its floor.
Shadow blocks are not edits: every one is in the touched set (so the reconcile on restart or another city reverts
it), the edit set wins over them, and a player emptying one is recorded as `minecraft:cave_air` (the host stores plain
air as "no edit"). Built lazily, at most ~4000 changed cells per server tick. SECTION_MESH skips them.
Owner rule: shadow blocks never take part in entity movement collision where the triangles are loaded
(`ShadowCollisionMixin` on `EntityCollisionContext.getCollisionShape`); queries without an entity (path-finding node
evaluation, spawn checks, fluids) still see them. Mobs, items and other non-player entities collide server-side with
the triangles and moving obstacles through `PlayerCollider` (`EntityTriCollideMixin`). Path nodes sit on block tops
up to half a block off the smooth surface; mobs step up 0.6, so the walk follows the path. Generated logs and leaves
do not collide either (the CS1 trunk box already does). Barrier over a custom invisible block: vanilla (no registry
or asset risk), solid to path-finding, no spawns on it, unbreakable; the crosshair skips it explicitly in `SkyClip`.
Rejected: writing chunk sections directly (fast, but lighting, heightmaps and client resend would be ours to keep
right); filling the full column depth up front (millions of `setBlock` calls for a loaded city).

## 2026-10-06: entities drawn by the host (protocol 1.14)

The guest captures what Minecraft's own entity renderers submit: `EntityRenderDispatcher.extractEntity` then `submit`
into a recording `SubmitNodeCollector` (`dev.mcskylines.entity.EntityCapture`), so every model (base model and
layers such as sheep wool, saddles, armour), its texture (the render type's `Sampler0`), tint, hurt overlay and the
full pose matrix (yaw, scale, death tilt, baby scale) come from vanilla, and each model is posed with
`Model.setupAnim(state)`. Models (ModelPart trees as textured quads with normals) and textures (the resource PNG) go
once per connection (`ENTITY_MODEL`, `ENTITY_TEXTURE`); every client tick `ENTITY_STATES` carries the complete set
within 96 m with per-part transforms and flags. Items are one-part models of their baked quads with their sprites
packed into a texture of their own; the spin and bob are in the matrix. The host's `Skylines.Host.Rendering.BoxModelRenderer`
is Minecraft-free (parts, quads, texture, transforms; pure maths in `Skylines.Core.Models.BoxModelMath`), interpolates
between the last two snapshots and draws with `Graphics.DrawMesh` on the Props layer within 256 m of the camera, in
city and player mode. Rejected: replicating vanilla's per-type transforms on the host (yaw, scale, death flip, item
spin: duplicated Minecraft knowledge that drifts per type); streaming rendered images (lag, no occlusion with the
city). Not drawn yet: anything submitted as custom geometry (arrows, fishing lines, end crystals' beams), block-model
entities (falling blocks, minecarts' contents), name tags, shadows, fire; player skins and other textures that are not
resource-pack files.


## 2026-10-06: passive animals spawn in the city world

The city world turns `SPAWN_MOBS` on (hostile `SPAWN_MONSTERS` stays off) and `dev.mcskylines.world.AnimalSpawner` tries 3
spawns per player every 5 s, 24-48 m away, by context (`AnimalChoice`): CS1 water -> cod/salmon/squid; shadow grass
within 12 m of a host tree -> rabbit/fox/wolf; grass with no paving or building among 8 samples 12 m around -> horse,
llama, cow, sheep; other grass -> cow/pig/sheep/chicken. Vanilla caps (`MobCategory.getMaxInstancesPerChunk` within 128
blocks of the player) and, on land, vanilla spawn rules apply. CS1 zoning (parks, farm land) is not sent to the guest,
so "open grass" stands in for it. Animals live in the Minecraft cache world; persisting them per city is open.
## 2026-10-06: dug ground is a hole in CS1 (milestone 5, protocol 1.13)

Everything derives from the city's edit set on the host (`Skylines.Core.Voxels.DugGround`): an edit at or below a
column's solid top (the shadow world's rule, on the collision's own 2 m triangulation) is dug; a column whose solid-top
cell is dug is open. Open columns: the 4 m surface cells they touch are clipped with the game's own surface Clip
(`TerrainClipMask` group "dig"; the save keeps heights only, so nothing persists without the mod), the undug 1 m squares
of those cells and the skirts between the block grid and the smooth surface are drawn by `DigLink` with CS1's grass
texture on the prop shader, and collision gets the exact cut plus the cavity faces. The cavity's walls, floors and
ceilings are the guest's own shadow blocks: `SECTION_MESH` now carries a shadow block's faces toward `cave_air`, so the
textures are exactly the block (dirt, stone, ore) the player would mine. Rejected: the host drawing cavity faces itself
(it does not know the seeded ore layout; duplicating `ShadowMaterials` in C# would drift) and a new "material query"
message (more protocol for the same result). Collision regions keep the cut-away terrain as flag bit 9 (minor 13) so the
guest's shadow ground keeps its surface height over open columns (otherwise the pit floor and its material bands would
be rebuilt from the floor); bit 9 is never solid. Heights are never changed, so CS1's water never floods a pit. The
guest refuses breaking a shadow cell in a column under a non-bridge road or a building (`PlayerBlockBreakEvents.BEFORE`,
`ShadowWorld.refusesBreak`). Limits: tunnels under intact ground keep CS1's surface even where a cliff steeper than 45°
dips into a dug cell; a non-cave-air block placed in a pit keeps the hole open but its wall faces are not drawn; walls
appear only where the guest has the shadow world loaded (around the Minecraft player); CS1 terraforming over a hole needs
a reload to move it.

## 2026-10-06: growing conditions, catch-up growth, ground tools, saplings grow into CS1 trees (protocol 1.15)

Owner: "Saplings need to be fed the conditions necessary for them to grow", "Same with plants that can be farmed", "I
would like to be able to hoe CS1 grass and turn it into farmland just like a normal grass block", "make all the trees
spawned by the mod look like vanilla CS1 trees but behave like Minecraft trees".

- **Growth follows the city's clock, for every player-owned growing block, in one mechanism.** The integrated server
  keeps ticking in city view (the player is only frozen), but vanilla random ticks reach only chunks simulated near the
  player and run on real time, so CS1's pause and speed did not matter. Now vanilla's random tick is suppressed for the
  player's growing cells (saplings, crops, stems, cane, cactus, berries, bamboo, cocoa, nether wart, farmland) and
  `Growth` replays it from the city clock (`WORLD_TIME` total ticks; a CS1 day/night cycle is 24000 ticks): about once
  a second for every loaded chunk that holds such cells, from the chunk's last simulated city tick to now. Per cell the
  random-tick times are sampled with vanilla's rate (randomTickSpeed/4096 per tick, geometric gaps, seeded from the
  city seed, the cell and the start tick, at most 256 per round) and at each one vanilla's own `BlockState.randomTick`
  runs, so every rule (sapling 1/7 at light 9 above, crop speed from moisture and neighbours, farmland drying,
  CS1 water through `FarmlandWaterMixin`, CS1 lamps as light blocks) is Minecraft's. Sky light is the sun at the
  sampled time: `Level.getSkyDarken` is overridden during the call with 26.3's `sky_light_level` timeline
  (`GrowthMath.skyDarken`). Pausing CS1 stops the clock, so growth stops. A chunk that was unloaded catches up when it
  loads. Rejected: letting vanilla tick near the player and catching up elsewhere (double growth, and growth while CS1
  is paused); reimplementing each block's rule (drifts from vanilla). Limits: cocoa draws from the level's own random,
  so it is not deterministic; rain is ignored; CS1 water and collision are only known around the player.
- **The catch-up clocks live in the city's save, inside the player blob.** Per chunk "simulated up to city tick" pairs
  are written into `PLAYER_DATA`'s NBT root (`mcskylines:growth`), which the host already stores opaquely in the
  city's save and covers with the save barrier, so an older save rolls growth back with its blocks. Rejected: a new
  message and host save field (more protocol for data the host never reads) and Minecraft's own world (a cache).
- **Tools act on the shadow ground.** The crosshair keeps hitting the invisible shadow grass or the empty cell over
  CS1's surface; on the server, a hoe, shovel or bone meal used there is retargeted to the shadow ground block below
  (top face), after the invisible plant on it is removed without drops (saved as the player's cave-air edit, the rule
  for emptied shadow cells). Vanilla then tills, flattens or fertilises exactly as on a bare grass block. A tilled cell
  is an edit at the column's solid top, so milestone 5 opens CS1's surface there and the guest's `SECTION_MESH`
  draws the farmland. Rejected: changing the client pick per held item (placement and mining would change too).
- **A sapling that would grow becomes a CS1 tree** (`TREE_GROWN`, 0x01C2). The guest cancels vanilla's tree for a
  player's sapling, checks room against the city's collision (trunk and crown boxes against every non-terrain
  triangle: roads, bridges, buildings, railings, tunnels, trees, props, the land boundary; and no host tree within
  1.5 m), sends the kind and a seed, and removes the sapling edit; the host picks a loaded tree prefab of that kind
  (the `TREES` kind rule on prefab names, ordinal order, seed modulo, oak-kind fallback), creates it through
  `TreeManager.CreateTree` and re-sends the region, so the shadow world builds and fells it like any city tree. No room
  (or collision not streamed there yet) leaves a stage-1 sapling, as vanilla does when a tree does not fit. Kinds:
  oak, cherry → 0 oak; spruce → 1; birch, poplar → 2; jungle, mangrove → 3; acacia → 4; dark oak, pale oak → 5. Room
  sizes (height/radius m): 10/4, 14/3, 12/3, 12/4, 9/5, 10/5. Rejected: hard-coded CS1 prefab names (asset packs and
  DLC vary; the kind rule already classifies whatever is loaded).

## 2026-10-06: broken grass leaves bare ground in CS1

Owner: "I punched at the ground and got some seeds but the CS1 grass didn't physically go away." Every grass-topped
shadow column carries an invisible plant at solid top + 1; breaking it records `minecraft:cave_air` there. The host's
`DugGround` now remembers which edits empty their cell, and a column whose plant cell is emptied (and which is not open)
is **bare**: `DigLink` clips its 4 m surface cell exactly like a dug column and redraws the cell's undug 1 m squares,
grass for the others and CS1's own `TerrainProperties.m_ruinedDiffuse` (tiling `m_ruinedTiling`) for the bare one, on
the same prop-shader patch mesh, so the square follows the ground with no z-fighting and no seam beyond the existing
dig patches. A plant placed back there (bone meal) is an edit with a non-air state and restores the grass look.
Collision is unchanged. Rejected: a decal or quad lifted above unclipped terrain (z-fights or floats at distance);
reading the plant rule (`ShadowMaterials.plant`) on the host (it is the guest's world generation; the emptied edit is
the fact the host already receives). Risk: the host's solid top is computed from its own heightfield in float; where
CS1's surface sits within float error of a cell's half-height, host and guest can disagree by one cell.

## 2026-10-06: Minecraft's /time moves the city's clock (protocol 1.16 TIME_SET)

Owner: "minecraft /time command should control the CS1 time and sync it to whatever that time would be in
Minecraft." The city stays the clock's only authority: every change of the city world's overworld clock that does not
come from WORLD_TIME runs through `ServerClockManager.modifyClock` (26.3: `/time set`, `/time add`, time markers such as
`/time set noon`, sleeping), where a mixin lets vanilla compute the new value, puts the clock back before it is
broadcast and sends the difference as TIME_SET (hour, whole days). The host moves the sun forward only and re-sends
WORLD_TIME on the next frame. Rejected: letting the command apply locally and also telling the host (the guest would
briefly show a time the city never had and a stale WORLD_TIME in flight could flick it back), and absolute TIME_SET
(a Minecraft absolute tick count has no meaning against the city's 738000-day epoch and could move the day count
backwards). `/time set <ticks>` counts from the start of the shown Minecraft day, so `/time set 30000` is tomorrow's
noon. Sleeping skips the night in the city world although ADVANCE_TIME is false there. The calendar date does not move.

## 2026-10-06: vehicles shaped by their mesh, walkable, and rideable (protocol 1.17)

Owner: a tractor pulling a long trailer had "very odd" collision; "jump onto a car and ride it"; "stairs where the
windshield and back windows would be". The guest used to guess a hood/cabin/boot shape for every low vehicle at least
3.4 m long, so a flat trailer bed and a tractor got a car's cabin; an inverted trailer's box was also placed from the
unflipped mesh bounds. Now the host sends each vehicle's real height profile (0.25 m slices of its mesh, a plain box
for a chained vehicle without mesh data) and its turn rate in `SHAPED_OBSTACLES` 0x0171; the guest lowers car-sized
profiles to 0.5 m stairs so the player walks up and over, and carries an entity whose feet are on a vehicle's top by
the vehicle's motion and turn for each tick. Rejected: guest-derived turn rates from successive sets by id (one more
store keyed by id, wrong for the first 50 ms and across id reuse, while the host already tracks per-id velocity on its
real clock); a wire field per slice as f32 (4x larger for no visible gain); rotating the player's view with the vehicle
(the host owns the camera yaw in player mode; the feet still turn round the vehicle's centre).

## 2026-10-07: entities belong to the city save; the city view's area is simulated (protocol 1.18)

Owner (2026-10-06): "Minecraft mobs need to exist in the CS1 world even when the player leaves Minecraft mode, just
like the blocks". Every non-player entity is saved in the city save as its own blob, `CITY_ENTITIES` 0x01D0 (same
shape, limits and host storage as `PLAYER_DATA`: BlobRecord with version and CRC under
`MinecraftSkylines.CityEntities`, an unreadable record kept aside). The guest tags each entity with a per-open
generation and discards anything else the world loads (another city's mobs, stale disk copies); the city's entities
are restored from the blob once their chunk is loaded and its shadow ground built, and those unloaded to disk are
parked so a capture holds the whole city. Blob: gzip NBT `{version: 1, DataVersion, entities}`, at most 1024 entities,
persistent mobs first, trimmed to 4 MiB; older data is upgraded with `DataFixTypes.ENTITY_CHUNK`. Rejected: riding
inside `PLAYER_DATA` (the player's inventory and a growing mob population would share one 4 MiB budget, so an
overflow could cost the inventory, and a fresh-player reset would wipe the city's animals).

City view: `CITY_FOCUS` 0x01D1 carries `CameraController.m_currentPosition` (moved 8 m, at most 4/s). The guest holds
a vanilla `DRAGON` ticket (loading + simulation, no timeout, not persisted; a registered type needs a main entrypoint
this client-only mod lacks) of radius 4: entities tick in the 5 x 5 chunks (80 m) around the focus, two frozen rings
around them so mobs never walk off the ground the host streams there (64 m, which covers the 25 chunks). About 25
ticking chunks on top of the player's, so the hidden server stays cheap. Mobs in that area skip `Mob.checkDespawn`
(not in Peaceful); items and arrows keep their age-based despawn. The exporter adds server-side entities of the area
(the client tracks only those near the player), posed by the same renderers. Pausing CS1 is unchanged: mobs in Minecraft
still move while the city is paused, as they did near the player. Rejected: a fake player at the camera (a second
connection and chunk-tracking view), and moving the hidden player there (breaks the player's own state).
