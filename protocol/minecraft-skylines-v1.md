# `minecraft-skylines` application protocol, version 1.4

Runs on the SKBR bridge (`bridge-v1.md`); `appProtocol = "minecraft-skylines"`, `appMajor = 1`,
`appMinor = 4`. Encodings are the bridge's primitives. Message types start at `0x0100`.

1.0 (milestone 1): status exchange. 1.1 (milestone 2): player mode, input, collision, player
state. 1.2 (milestone 3): block meshes, texture atlas, debug commands. 1.3 (milestone 3): GUI overlay
through shared memory, viewport, cursor input. 1.4: block selection outline. Messages of a newer minor are sent only when the negotiated minor (min of both sides)
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
| u16 | `flags` | bit 0 terrain, bit 1 road surface, bit 2 bridge deck, bit 3 building (the building's LOD mesh, the one the game raycasts, clipped to the region; an oriented box when no LOD data exists), bit 4 railing, bit 5 tunnel wall or ceiling, bit 6 vegetation (a tree's trunk box, or a bush's full box), bit 7 prop (an oriented box from the prop's mesh bounds: standalone, building and road-lane props; a tall prop whose pivot is off its bounds' centre, such as a street light, is only its post) (bits 1-7 informational; the guest treats every triangle as solid), bits 8-15 reserved. Terrain triangles are omitted where the game clipped its terrain surface (tunnel portals, clip-terrain buildings and roads), so the guest can walk into tunnel portals |

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
geometry, the 1x1x1 cell just behind the hit surface (kind 1, the "virtual block" vanilla would outline;
a placement fills its neighbour across the hit face). Sent when it changes; latest value wins.

| Type | Field | Notes |
|---|---|---|
| bool | `visible` | false = no outline |
| f32 × 3 | `minX`, `minY`, `minZ` | Minecraft coordinates |
| f32 × 3 | `maxX`, `maxY`, `maxZ` | |
| u8 | `kind` | 0 existing block, 1 virtual block at host geometry |
