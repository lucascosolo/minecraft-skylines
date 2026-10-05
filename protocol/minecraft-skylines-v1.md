# `minecraft-skylines` application protocol, version 1.0

Runs on the SKBR bridge (`bridge-v1.md`); `appProtocol = "minecraft-skylines"`, `appMajor = 1`,
`appMinor = 0`. Encodings are the bridge's primitives. Message types start at `0x0100`.

Milestone 1 defines only status exchange. Later milestones add messages with a minor bump;
anything that changes an existing layout bumps the major.

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
