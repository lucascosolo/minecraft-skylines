# `minecraft-skylines` application protocol, version 1.9

Runs on the SKBR bridge (`bridge-v1.md`); `appProtocol = "minecraft-skylines"`, `appMajor = 1`,
`appMinor = 9`. Encodings are the bridge's primitives. Message types start at `0x0100`.

1.0 (milestone 1): status exchange. 1.1 (milestone 2): player mode, input, collision, player
state. 1.2 (milestone 3): block meshes, texture atlas, debug commands. 1.3 (milestone 3): GUI overlay
through shared memory, viewport, cursor input. 1.4: block selection outline. 1.5 (milestone 4): per-city block edits and the save barrier. 1.6: the city's time of day. 1.7: moving vehicles and citizens as obstacles. 1.8: the city's lit lamps as Minecraft light. 1.9: Minecraft's sky drawn by the host. Messages of a newer minor are sent only when the negotiated minor (min of both sides)
allows them. Anything that changes an existing layout bumps the major.

## `0x0100 HOST_STATUS` (host → guest)

Sent right after the handshake and whenever a field changes.

| Type | Field | Notes |
|---|---|---|
| u32 | `flags` | bit 0 `IN_CITY` (a city is loaded), bit 1 `LOADING`, bit 2 `SIM_PAUSED`, bit 3 `PLAYER_MODE` (the host has handed the camera to the guest) |
| string | `cityName` | empty outside a city |
| uuid | `saveId` | the city's pairing id; all zero when none (see `docs/ARCHITECTURE.md`, save identity) |
| string | `gameVersion` | e.g. `1.21.1-f5` |

## `0x0101 GUEST_STATUS` (guest → host)

Sent right after the handshake and whenever a field changes.

| Type | Field | Notes |
|---|---|---|
| u32 | `flags` | bit 0 `IN_WORLD` (a world is loaded), bit 1 `SCREEN_OPEN` (a Minecraft GUI screen has input) |
| string | `worldName` | empty when no world is loaded |
| uuid | `pairedSaveId` | the `saveId` the loaded world belongs to; all zero when none |

## Coordinate frames (shared by every later message)

Cities: Skylines (Unity) is left-handed, Y up, metres. Minecraft is right-handed, Y up, blocks.
**One block is one metre.** Positions convert as

    mc.x = cs.x
    mc.y = cs.y + Y_OFFSET        (Y_OFFSET = 0 in v1)
    mc.z = -cs.z

and look angles (degrees) as

    mc.yaw   = wrap180(unity.eulerY + 180)
    mc.pitch = wrap180(unity.eulerX)        (both: positive = looking down)

so that Unity's forward vector `(sin y, ., cos y)` maps to Minecraft's look vector
`(-sin yaw, ., cos yaw)` after the z flip. `MinecraftSkylines.Protocol.MinecraftFrame` (C#) and
`dev.mcskylines.protocol.MinecraftFrame` (Java, in the Fabric mod) implement this and are tested against `protocol/vectors/coords.json`.

The playable CS1 map spans x, z in [-8640, 8640] m and terrain heights in [0, 1024] m. The guest's
mirror dimension uses `min_y = -64` (digging below the city's zero) and `height = 1536`.

## Milestone 2 messages (minor 1)

All positions are Minecraft coordinates (blocks, Y up, Z south; see above). Angles are degrees.

### `0x0110 ENTER_PLAYER_MODE` (host → guest)

The player switched the city to Minecraft mode. The guest opens its mirror world if needed,
teleports the player, and **holds** it (no gravity, no movement) until collision is loaded around
it (every region within 1 of the player's region received, or 6 s elapsed), then acknowledges by
reporting `teleportAck = teleportSeq` in `PLAYER_STATE`.

| Type | Field | Notes |
|---|---|---|
| u32 | `teleportSeq` | increments on every teleport the host requests |
| f64 | `x`, `y`, `z` | feet position |
| f32 | `yaw`, `pitch` | initial look |
| u32 | `collisionEpoch` | the epoch the regions for this position carry |

### `0x0111 EXIT_PLAYER_MODE` (host → guest)

| Type | Field | Notes |
|---|---|---|
| string | `reason` | e.g. `player pressed Esc`, `city unloading` |

The guest releases every held key and button and freezes the player in place. The host has
already restored its own camera and input.

### `0x0112 INPUT` (host → guest)

Sent once per host frame while in player mode. The host has OS focus, so it owns the look
direction: it integrates mouse movement into `yaw`/`pitch` (SkyCraft's choice: the visible camera
never waits for the guest) and the guest copies them onto its player.

| Type | Field | Notes |
|---|---|---|
| f32 | `yaw`, `pitch` | authoritative look |
| u16 | `eventCount` | |
| per event: u8 | `kind` | 1 key, 2 mouse button, 3 scroll, 4 text, 5 release all |
| u8 | `action` | 1 press, 0 release (0 for scroll, text, release all) |
| i32 | `code` | key: GLFW key code; button: GLFW button (0 left, 1 right, 2 middle); scroll: wheel notches × 120, positive up; text: Unicode code point; release all: 0 |

### `0x0113 COLLISION_REGION` (host → guest)

The complete collision geometry of one 16 × 16 column of blocks (all heights). Replaces anything
the guest holds for that region.

| Type | Field | Notes |
|---|---|---|
| u32 | `epoch` | regions from an older epoch than the latest `COLLISION_RESET` are dropped |
| i32 | `regionX`, `regionZ` | `floor(x / 16)`, `floor(z / 16)` |
| u32 | `triCount` | 0 = the region is known to be empty |
| per triangle: f32 × 9 | `ax ay az bx by bz cx cy cz` | absolute coordinates; counter-clockwise seen from the solid side's outward normal, i.e. `(b-a)×(c-a)` points out of the solid (up for ground) |
| u16 | `flags` | bit 0 terrain, bit 1 road surface, bit 2 bridge deck, bit 3 building (the building's LOD mesh, the one the game raycasts, clipped to the region; an oriented box when no LOD data exists), bit 4 railing, bit 5 tunnel wall or ceiling, bit 6 vegetation (a tree's trunk box, or a bush's full box), bit 7 prop (an oriented box from the prop's mesh bounds: standalone, building and road-lane props; a tall prop whose pivot is off its bounds' centre, such as a street light, is only its post) bit 8 boundary (an invisible wall at the edge of the land the player owns; minor 5 hosts send it, older guests treat it as solid like any other), (bits 1-8 informational; the guest treats every triangle as solid and does not let the crosshair target bit-8 walls), bits 9-15 reserved. Terrain triangles are omitted where the game clipped its terrain surface (tunnel portals, clip-terrain buildings and roads), so the guest can walk into tunnel portals |

A triangle may extend past its region's bounds; the guest files it under the region it arrived in.

### `0x0114 COLLISION_RESET` (host → guest)

| Type | Field |
|---|---|
| u32 | `epoch` |

The guest drops all collision regions and accepts only regions with `epoch >=` this value. Sent
on entering player mode for a different city and whenever the host's world changes wholesale.

### `0x0120 PLAYER_STATE` (guest → host)

Sent once per guest render frame while a world is loaded. Latest value wins; the host may drop
all but the newest.

| Type | Field | Notes |
|---|---|---|
| u32 | `flags` | bit 0 in world, 1 on ground, 2 sneaking, 3 sprinting, 4 swimming, 5 flying, 6 dead, 7 held (waiting for collision) |
| u32 | `teleportAck` | the last `teleportSeq` applied and released from hold; 0 before any |
| f64 | `x`, `y`, `z` | feet, interpolated to this frame |
| f64 | `eyeX`, `eyeY`, `eyeZ` | camera position (includes sneak and swim eye height) |
| f32 | `yaw`, `pitch` | as applied by the guest |
| f32 | `fovDeg` | effective vertical field of view (includes sprint and fluid modifiers) |
| u32 | `tickSeq` | increments every guest physics tick |
| f64 × 3 | `prevX`, `prevY`, `prevZ` | feet at the previous tick |
| f64 × 3 | `curX`, `curY`, `curZ` | feet at the latest tick |
| f32 | `prevEyeHeight`, `curEyeHeight` | eye height above feet at those ticks |
| f32 | `partialTick` | 0..1, how far this frame is between `prev` and `cur` |
| f32 | `tickMs` | milliseconds per tick (50 unless the tick rate was changed) |

The host draws the camera from these. It may use the frame values directly or interpolate the
tick values on its own clock (as SkyCraft does) to hide the two games' frame-phase difference.

## Milestone 3 messages (minor 2)

Minecraft builds its block meshes with its own block renderer (models, biome tint, ambient
occlusion) and ships them with its texture atlas; CS1 builds ordinary meshes from them and draws
them in its own scene, so terrain and buildings occlude blocks and vice versa (SkyCraft's model).

### `0x0130 BLOCK_ATLAS` (guest → host)

| Type | Field | Notes |
|---|---|---|
| u32 | `width`, `height` | pixels |
| u8 | `format` | 1 = PNG (the only format in 1.2; raw RGBA of a full atlas would exceed the 16 MiB frame limit) |
| u32 | `byteLength` | |
| bytes | `data` | the encoded image, top row first |

Sent after the handshake once a world is loaded, and again whenever the atlas changes (resource
reload). UV coordinates in `SECTION_MESH` refer to this image (0,0 = top-left corner).

### `0x0131 ATLAS_REGION` (guest → host)

| Type | Field | Notes |
|---|---|---|
| u32 | `x`, `y`, `width`, `height` | pixels in the atlas |
| bytes | `rgba` | `width × height × 4` bytes, RGBA8, top row first |

An animated sprite's current frame (water, lava, fire). Optional for a host to honour.

### `0x0132 SECTION_MESH` (guest → host)

The complete mesh of one 16×16×16 section of placed Minecraft blocks; replaces what the host has.

| Type | Field | Notes |
|---|---|---|
| i32 | `sx`, `sy`, `sz` | section coordinates (`floor(block / 16)`) |
| u32 | `vertexCount` | multiple of 3 (triangle list); 0 = the section is empty, drop it |
| per vertex: f32 × 3 | `x`, `y`, `z` | Minecraft coordinates relative to the section origin (`sx*16, sy*16, sz*16`) |
| f32 × 2 | `u`, `v` | atlas UV (0..1, origin top-left) |
| u32 | `color` | RGBA8 as bytes R,G,B,A: biome tint × ambient occlusion (Minecraft's directional face shading left out: the host lights the mesh) |
| u32 | `light` | low byte block light 0-15, next byte sky light 0-15 |
| u32 | `flags` | bit 0 cutout (alpha test), bit 1 translucent |

Triangles are counter-clockwise seen from outside in Minecraft's right-handed frame; a host in a
left-handed frame that mirrors z must reverse the winding (as for collision).

### `0x0133 SECTIONS_CLEAR` (guest → host)

No fields. Drop every section (world change, dimension change, reconnect).

### `0x01F0 DEBUG_COMMAND` (host → guest)

| Type | Field | Notes |
|---|---|---|
| string | `command` | a server command without the leading slash, e.g. `fill 10 64 -20 12 66 -18 minecraft:stone` |

For automated in-game tests only. The guest runs it as the server console in its dev world
`skylines-dev` and ignores it everywhere else, and only when started with
`-Dmcskylines.debugCommands=true`. The result is logged, not returned.

## Milestone 3 messages, part 2 (minor 3): the GUI overlay

Minecraft renders its HUD (hotbar, hearts, crosshair, held item) and any open screen (inventory,
crafting, chat, pause menu) at the host's viewport size on a transparent background and publishes
the pixels through a shared-memory file; the host draws them over its own view. Pixels never travel
over the socket (a 1920×1080 frame is about 8 MB).

### `0x0140 VIEWPORT` (host → guest)

| Type | Field | Notes |
|---|---|---|
| u32 | `width`, `height` | the host's screen size in pixels |
| f32 | `uiScale` | 0 = let the guest choose its GUI scale; otherwise a requested Minecraft GUI scale |

Sent after the handshake, on every resize, and on entering player mode.

### `0x0141 OVERLAY_OFFER` (guest → host)

| Type | Field | Notes |
|---|---|---|
| string | `path` | absolute path of the shared-memory file (Linux: under `/dev/shm`) |
| u32 | `maxWidth`, `maxHeight` | the largest frame a slot holds |
| u32 | `slotCount` | 3 |
| u64 | `generation` | changes whenever the guest recreates the file |

Sent once the guest has created and initialised the file (after a VIEWPORT), and again after it
recreates it (bigger viewport, restart). The host maps the file read-write (it writes only the
`state` word) and stops reading it on disconnect or a new offer.

### `0x0142 OVERLAY_STOP` (guest → host)

No fields. The guest stopped publishing (left the world, overlay disabled); the host hides the
overlay and unmaps the file.

### Shared-memory layout (all little-endian)

| Offset | Type | Field |
|---|---|---|
| 0x00 | u32 | `magic` = `0x564F534D` (bytes "MSOV") |
| 0x04 | u32 | `layoutVersion` = 1 |
| 0x08 | u32 | `maxWidth` |
| 0x0C | u32 | `maxHeight` |
| 0x10 | u32 | `slotCount` (3) |
| 0x14 | u32 | `state`: bits 0-1 index of the *middle* slot, bit 2 `DIRTY` (middle holds an unread frame) |
| 0x18 | u64 | `framesPublished` |
| 0x20 | u64 | `generation` (as in OVERLAY_OFFER) |
| 0x40 + 0x40·i | slot header i | u32 `width`, u32 `height`, u32 `flags` (bit 0: rows bottom-up), u32 pad, u64 `frameId`, pad to 0x40 |
| 0x100 + i·maxWidth·maxHeight·4 | slot pixels i | RGBA8, premultiplied alpha, `width`×`height` used, row stride `width`·4 |

Triple buffer (SkyCraft's scheme): the guest owns a private *back* slot, writes a frame into it,
then atomically exchanges `state` with `back | DIRTY` and keeps the returned index as its new back
slot. The host owns a private *front* slot; once per host frame, if `state` has `DIRTY` set, it
atomically exchanges `state` with `front` (DIRTY clear) and keeps the returned index as its new
front slot. Initial state: guest back = 0, middle = 1 (not dirty), host front = 2. Both sides
use 32-bit atomic exchange on the 4-byte-aligned `state` word.

### INPUT additions (minor 3)

| `kind` | Meaning |
|---|---|
| 6 | cursor: `code` = `(x << 16) | y` in host pixels, origin top-left; sent when a guest screen is open (`GUEST_STATUS.SCREEN_OPEN`) and the cursor moved |

While `SCREEN_OPEN` is set the host shows its own mouse cursor, stops integrating mouse movement
into yaw/pitch, and sends mouse buttons, wheel and keys as before plus cursor positions.

## Milestone 3 messages, part 3 (minor 4): the block selection outline

### `0x0134 BLOCK_SELECTION` (guest → host)

The box Minecraft outlines under the crosshair: a real block (kind 0), or, when the crosshair hits host
geometry, the 1x1x1 cell a placed block would fill (kind 1): the cell 0.4 blocks out from the hit along the
surface normal, so blocks sink up to 0.6 into uneven city surfaces rather than float (SkyCraft's rule; owner,
2026-10-06). Sent when it changes; latest value wins.

| Type | Field | Notes |
|---|---|---|
| bool | `visible` | false = no outline |
| f32 × 3 | `minX`, `minY`, `minZ` | Minecraft coordinates |
| f32 × 3 | `maxX`, `maxY`, `maxZ` | |
| u8 | `kind` | 0 existing block, 1 virtual block at host geometry |

## Milestone 4 messages (minor 5): per-city block edits

The city save is the authority for everything the player built or broke: the host keeps the city's
**edit set** (Minecraft block position → block state) and writes it into the save; Minecraft's world
is a cache rebuilt from it whenever a city opens. Loading an older save, Save As forks and quitting
without saving therefore behave for blocks exactly as they do for the rest of the city.

An edit is a block position (Minecraft block coordinates, `i32 × 3`) and a block state in
Minecraft's block-state syntax as produced by the guest's canonical serializer, for example
`minecraft:oak_stairs[facing=north,half=bottom,shape=straight,waterlogged=false]`. The state
`minecraft:air` means "back to the world's base" (in 1.5 the base is void, so the host removes the
position from its edit set). Hosts treat state strings as opaque.

A city is **open** on the guest between `CITY_OPEN` and the next `CITY_CLOSE` (or link loss). Every
open has a host-chosen `openSeq` (strictly increasing within a host process); messages carrying an
`openSeq` other than the current one are stale and are dropped by both sides.

### `0x0150 CITY_OPEN` (host → guest)

| Type | Field | Notes |
|---|---|---|
| u32 | `openSeq` | |
| uuid | `saveId` | the city's pairing id (never all zero) |
| string | `cityName` | for logs and the guest's UI |
| u32 | `editCount` | number of edits the host will send in `BLOCK_EDITS` batches for this open |

Followed by `BLOCK_EDITS` batches with this `openSeq` totalling `editCount` edits, the last one
with `flags` bit 0 set (also when `editCount` is 0: one empty batch with bit 0). Sent when a paired
city is loaded and the link is up, and again after every reconnect; an open replaces any earlier
one without a `CITY_CLOSE`.

The guest then makes its world match: every position it may have changed before (its own
persistent record of touched positions, kept across Minecraft restarts) that is not in the edit
set goes back to air; every edit is applied. It applies without block updates to neighbours or
physics (the result must equal the snapshot), and it does not record its own applications as
player edits. When everything is applied or queued for chunks that are not loaded, it sends
`CITY_STATE` ready and starts recording.

### `0x0151 BLOCK_EDITS` (both directions)

| Type | Field | Notes |
|---|---|---|
| u32 | `openSeq` | |
| u8 | `flags` | bit 0 `LAST` (host → guest: last batch of the `CITY_OPEN` snapshot; guest → host: 0) |
| u16 | `paletteCount` | |
| string × `paletteCount` | `palette` | block states used in this batch, no duplicates |
| u32 | `editCount` | at most 65536 per batch |
| per edit: i32 × 3, u16 | `x`, `y`, `z`, `state` | `state` indexes `palette` (out of range is a protocol error) |

Guest → host: the player's (and the world's: fluids, falling blocks, fire) block changes since the
last batch, latest state per position, at most one batch per server tick. The host applies them to
its edit set in order. A position may appear once per batch.

### `0x0152 CITY_CLOSE` (host → guest)

| Type | Field | Notes |
|---|---|---|
| u32 | `openSeq` | the open being closed |

The city was unloaded (or the host stopped using Minecraft for it). The guest stops recording,
sends any edits still pending (with the closing `openSeq`, before acting on the close), reverts its
world to empty as for a `CITY_OPEN` with no edits, and sends `CITY_STATE` closed.

### `0x0153 EDIT_SYNC` (host → guest) and `0x0154 EDIT_SYNC_ACK` (guest → host)

| Type | Field | Notes |
|---|---|---|
| u32 | `openSeq` | |
| u32 | `token` | echoed in the ack |

The save barrier. The guest answers with `EDIT_SYNC_ACK` carrying the same fields after it has sent,
in `BLOCK_EDITS`, every edit it recorded before it received the `EDIT_SYNC` (TCP order then
guarantees the host has those batches before the ack). A guest whose open does not match acks
anyway, so the host never waits for nothing. Hosts wait a bounded time (CS1: 2 s) and save what
they have on timeout.

### `0x0155 CITY_STATE` (guest → host)

| Type | Field | Notes |
|---|---|---|
| u32 | `openSeq` | |
| u8 | `state` | 0 applying, 1 ready (applied, recording), 2 closed |
| u32 | `appliedCount` | edits applied so far for this open |

Sent on every state change. The host enters player mode for a paired city only after `ready`.

## Minor 6: the city's clock

### `0x0160 WORLD_TIME` (host → guest)

| Type | Field | Notes |
|---|---|---|
| f32 | `hour` | the city's time of day, 0 ≤ hour < 24 (CS1 `SimulationManager.m_currentDayTimeHour`) |
| u32 | `day` | whole days since an arbitrary epoch that only moves forward (CS1: completed day/night cycles, `(m_referenceFrameIndex + m_dayTimeOffsetFrames) / 65536`; not the calendar, which CS1 runs about 112 times faster than its sun); the guest uses it for the moon phase |
| u8 | `flags` | bit 0 `DAY_NIGHT`: the city has a day/night cycle; when clear the guest shows midday |

Sent while a city is loaded: right after the handshake (or the level load) and then at most once a second
while the value changes. The guest stops its own clock and shows exactly this time, so the sky, light
levels (and therefore everything Minecraft lights, the player's hand included) follow the city.
Minecraft's day starts at 06:00: ticks into the day = ((hour − 6) mod 24) × 1000.


## Minor 7: moving obstacles

### `0x0170 DYNAMIC_OBSTACLES` (host → guest)

The vehicles and citizens near the player as oriented boxes, so the player collides with them. Sent about 20 times a
second while in player mode, each time with the **complete** current set within 48 m of the player's feet (possibly
empty); the latest set replaces the previous one. Positions are where the host draws the object this frame.

| Type | Field | Notes |
|---|---|---|
| u16 | `count` | |
| per obstacle: u8 | `kind` | 1 vehicle (a trailer is its own vehicle), 2 citizen, 3 parked vehicle (CS1: index into `VehicleManager.m_parkedVehicles`, its own numbering; velocity 0); others reserved, treated as solid |
| u32 | `id` | the host's id of the object (CS1: index into `VehicleManager.m_vehicles` for kind 1, into `CitizenManager.m_instances` for kind 2), so later messages can refer to it |
| f32 × 3 | `x`, `y`, `z` | centre of the box, Minecraft coordinates |
| f32 | `yaw` | Minecraft yaw of the box's length axis: that axis is `(-sin yaw, 0, cos yaw)`, the width axis `(cos yaw, 0, sin yaw)`; the box is always upright |
| f32 × 3 | `halfWidth`, `halfHeight`, `halfLength` | half extents along the width, vertical and length axes, metres |
| f32 × 3 | `vx`, `vy`, `vz` | velocity, m/s in the Minecraft frame, as observed on the host's clock (zero while the city is paused) |

The guest extrapolates each box by `velocity × age` between messages, drops the whole set when no message has arrived
for 0.5 s or player mode ends, does not let the crosshair target the boxes, and pushes the player out of a box that
moves into it along the box's horizontal velocity (along the shortest way out when it is not moving).


## Minor 8: lamp light

### `0x0180 LIGHT_SOURCES` (host → guest)

The city's lights that are on near the player, so Minecraft's own light engine lights the player's hand, placed
blocks and mobs. Sent about once a second while in player mode, each time with the **complete** set of lit lights
whose position is within 64 m horizontally of the player's feet (possibly empty, e.g. by day); the latest set
replaces the previous one.

| Type | Field | Notes |
|---|---|---|
| u16 | `count` | |
| per light: i32 × 3 | `x`, `y`, `z` | the Minecraft block containing the light; each position at most once |
| u8 | `level` | Minecraft light level 1-15; anything else is a protocol error |

The host decides which lights are on with the game's own rule and maps each light's range and intensity to a level
(CS1: standalone, building and network-lane props' `LightEffect`s; level = clamp(ceil(range × min(intensity, 1)) + 4, 0, 15) (the 4 makes up for the lamp head being several blocks above the player),
0 not sent; see `docs/CS1-API-NOTES.md`).

The guest keeps an invisible `minecraft:light[level=N]` block at each position while the city is open and ready
(`CITY_STATE` ready), placed only where the block is air or one of its own lights, never over any other block, and
only in loaded chunks (retried on the next set). Its lights not in the newest set go back to air where they are still
its lights; a light the player has replaced is forgotten, not removed. These placements are applied like snapshot
edits: never reported in `BLOCK_EDITS`, never part of the city's edit set, but recorded as touched positions, so the
next `CITY_OPEN` (also after a Minecraft restart) or `CITY_CLOSE` reverts any left behind; the guest forgets its lights
on either.


## Minor 9: Minecraft's sky

While the player is in Minecraft mode the host hides its own sky and draws Minecraft's: a dome from the sky colour
overhead to the fog colour at the horizon, the sunrise/sunset glow, stars, Minecraft's sun and moon textures and a
flat cloud layer. The sun and moon are drawn **where the host's own sun and moon light comes from** (CS1:
`DayNightProperties.m_SunLight` / `m_MoonLight`), not at Minecraft's celestial angle, so the textured sun is the light
source the city is lit and shadowed by. Time of day is shared by `WORLD_TIME` (minor 6).

### `0x0190 SKY_STATE` (guest → host)

Sent about four times a second while a world is loaded; the newest replaces the previous one, and a host stops
drawing Minecraft's sky when none has arrived for 5 s. Colours are Minecraft's 0-1 floats as its sky renderer uses
them (sRGB-encoded).

| Type | Field | Notes |
|---|---|---|
| u8 | `flags` | bit 0 `SKY`: the dimension has an overworld-style sky (draw dome, glow, sun, moon, stars); bit 1 `CLOUDS`: clouds are shown (the dimension has clouds and the player's cloud option is not off) |
| f32 × 3 | `skyColor` | `EnvironmentAttributes.SKY_COLOR` at the camera |
| f32 × 3 | `fogColor` | `FOG_COLOR` at the camera; the dome's colour at the horizon |
| f32 × 4 | `sunriseColor` | `SUNRISE_SUNSET_COLOR` (r, g, b, a); a = 0 outside sunrise and sunset |
| f32 | `starBrightness` | `STAR_BRIGHTNESS`, 0-1, before rain |
| f32 | `rainLevel` | `ClientLevel.getRainLevel`, 0-1; sun, moon and stars are drawn at `1 - rainLevel` |
| u8 | `moonPhase` | `MoonPhase.index()`: 0 full moon, 1 waning gibbous, 2 third quarter, 3 waning crescent, 4 new moon, 5 waxing crescent, 6 first quarter, 7 waxing gibbous; anything above 7 is a protocol error |
| f32 × 4 | `cloudColor` | `CLOUD_COLOR` (r, g, b, a) |
| f32 | `cloudHeight` | `CLOUD_HEIGHT`, Minecraft Y of the cloud layer |
| f32 | `cloudOffset` | blocks the cloud pattern has moved along +x: `((gameTime mod (cloudTextureWidth × 400)) + partialTick) × 0.03` |
| f32 | `cloudSpeed` | blocks per second `cloudOffset` grows by until the next message (0.6 while the game runs, 0 while it is paused); the host extrapolates with it |

Clouds are one texel of the clouds texture per 12 × 12 blocks: the texel at Minecraft (x, z) is column
`floor((x + cloudOffset) / 12) mod width`, row `floor((z + 3.96) / 12) mod height` (row 0 = the image's top row),
coloured `cloudColor` × texel; texels with alpha 0 are clear sky.

### `0x0191 SKY_TEXTURES` (guest → host)

The images the sky is drawn with, from the guest's current resource packs. Sent after the handshake once a world is
loaded and again after every resource reload; each set replaces the previous one.

| Type | Field | Notes |
|---|---|---|
| u8 | `count` | |
| per texture: u8 | `kind` | 0 sun, 1 moon, 2 clouds; a host skips kinds it does not know |
| u8 | `phase` | moon: its `moonPhase` (0-7, anything else is a protocol error); other kinds: 0 |
| u8 | `format` | 1 = PNG; a host skips other formats |
| u32 | `byteLength` | |
| bytes | `data` | the encoded image, as in the resource pack |

Minecraft 26.3 sends `textures/environment/celestial/sun.png`, the eight
`textures/environment/celestial/moon/<phase>.png` and `textures/environment/clouds.png`; an image over 2 MiB is left
out (and logged). The host draws them point-filtered. Sun and moon are additive (black is transparent), drawn as
squares facing their direction with half-size 0.30 (sun) and 0.20 (moon) at distance 1, as Minecraft's 30 and 20 at 100.
