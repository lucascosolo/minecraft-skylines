# `minecraft-skylines` application protocol, version 1.1

Runs on the SKBR bridge (`bridge-v1.md`); `appProtocol = "minecraft-skylines"`, `appMajor = 1`,
`appMinor = 1`. Encodings are the bridge's primitives. Message types start at `0x0100`.

1.0 (milestone 1): status exchange. 1.1 (milestone 2): player mode, input, collision, player
state. Messages of a newer minor are sent only when the negotiated minor (min of both sides)
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
| u16 | `flags` | bit 0 terrain, bit 1 road surface, bit 2 bridge deck, bit 3 building, bits 8-15 reserved |

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
