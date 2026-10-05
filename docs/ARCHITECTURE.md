# Architecture

**You play inside Cities: Skylines (CS1). Minecraft Java runs hidden in the background.** CS1
draws everything on screen and keeps simulating the city; Minecraft supplies the player: movement
physics, inventory, hotbar, crafting, block logic and its GUI. This is SkyCraft's model
(Skyrim + hidden Minecraft) applied to CS1. Neither game is rewritten; the mods translate.

## Layers

The code is split so that a future "Cities: Skylines + some other game" project can lift the
lower layers unchanged. Dependencies only point downward.

```
 ┌──────────────────── CS1 process (Unity 5.6 / Mono, .NET 3.5) ─────────────────────┐   ┌────── Minecraft 26.3 + Fabric (Java 25) ──────┐
 │ MinecraftSkylines.Mod   Minecraft-specific: player puppet, block rendering from   │   │ minecraft/fabric   dev.mcskylines.*            │
 │                         MC meshes, excavation projection, app protocol messages   │   │   mirror world, collision injection, input    │
 ├────────────────────────────────────────────────────────────────────────────────────┤   │   replay, mesh/GUI export (Minecraft-only)    │
 │ Skylines.Host           reusable CS1 integration: lifecycle, diagnostics, main-   │   ├───────────────────────────────────────────────┤
 │                         thread pump, camera takeover, input capture, world        │   │ minecraft/bridge   dev.mcskylines.bridge       │
 │                         geometry export (terrain/nets/buildings), custom mesh     │   │   SKBR guest, codec, coords; NO Minecraft     │
 │                         drawing, terrain edits, save-data store. No Minecraft.    │   │   imports (reusable by any Java game mod)     │
 ├────────────────────────────────────────────────────────────────────────────────────┤   └───────────────────────────────────────────────┘
 │ Skylines.Core           pure C#, no Unity: coordinate frames, geometry types,     │                    ▲
 │                         heightfield/mesh → triangle export, voxel sets            │                    │  SKBR over TCP 127.0.0.1:47615
 │ Skylines.Bridge         pure C#, no Unity: SKBR host transport + codec            │◀───────────────────┘  (protocol/bridge-v1.md)
 └────────────────────────────────────────────────────────────────────────────────────┘
```

| Layer | Path | Depends on | Reusable for other games? |
|---|---|---|---|
| `Skylines.Bridge` | `cs1/src/Skylines.Bridge` | BCL only (net35 + net10.0) | Yes, any host/guest pair |
| `Skylines.Core` | `cs1/src/Skylines.Core` | BCL only | Yes |
| `Skylines.Host` | `cs1/src/Skylines.Host` | CS1 assemblies, Core | Yes, any CS1 integration |
| `MinecraftSkylines.Mod` | `cs1/src/MinecraftSkylines.Mod` | all of the above | No |
| `dev.mcskylines.bridge` | `minecraft/bridge` | JDK only | Yes, any Java guest |
| `dev.mcskylines` Fabric mod | `minecraft/fabric` | Minecraft, Fabric, bridge | No |
| protocol specs + reference | `protocol/` | Python stdlib | Yes; the app protocol file is Minecraft-specific |

## Ownership: one authority per piece of state

| State | Authority | The other side holds |
|---|---|---|
| Player position, velocity, collision response, pose | Minecraft (real `LocalPlayer` physics) | CS1: a camera driven from MC's interpolated state |
| Look direction (yaw/pitch) | CS1 integrates mouse deltas (SkyCraft's choice: no input lag on the visible camera); MC copies it | — |
| Raw input | CS1 has OS focus, captures and forwards; a small allow-list stays with CS1 (Esc to return to city controls) | MC replays it into its handlers |
| Inventory, items, crafting, block states, block entities | Minecraft world | CS1: nothing but rendered meshes |
| City simulation, citizens, vehicles, roads, buildings | CS1 | MC: collision shapes only |
| Native terrain heights | CS1 `TerrainManager` | MC: collision triangles streamed from CS1 |
| Excavation of native terrain (which cells are dug) | Minecraft (voxel set persisted in its world, like SkyCraft's `DUG` chunk attachment) | CS1: a derived projection (hidden terrain, wall meshes, optionally lowered heights) rebuilt from MC's record |
| Rendering of everything visible | CS1 (Unity), including MC blocks drawn from MC-exported meshes and MC's GUI as an overlay | MC renders nothing visible |
| Time of day, weather | CS1 | MC mirrors |
| Save identity | CS1 save carries a `saveId` (serializable mod data); the MC world records the `saveId` it belongs to | — |

Pits are where two authorities could collide: lowering CS1 terrain makes CS1's save carry the
change too. Rule: the MC voxel record is authoritative; any CS1 terrain change is re-derived from
it on load, and a `saveId` mismatch blocks re-derivation (see Save identity).

## Transport

SKBR over TCP loopback (`protocol/bridge-v1.md`). Why not SkyCraft's shared memory: CS1 mods
compile against the .NET 3.5 profile, which has no `MemoryMappedFiles`; sockets give clean EOF
semantics for disconnect detection; per-frame state is a few hundred bytes. Bulk data
(the GUI overlay's pixels, ~8 MB a frame at 1080p) is the exception and will use a shared-memory
file (`/dev/shm` on Linux, via `mmap` P/Invoke on the CS1 side) negotiated over the bridge in
milestone 3. CS1 is the listening host (it is the game you start); Minecraft connects and retries.

## Rendering plan

SkyCraft's hybrid model, which fits Unity better than it fits Skyrim: Minecraft builds block
meshes with its own block renderer and exports vertex streams plus its texture atlas; CS1 builds
Unity `Mesh`es and draws them in its own scene, so depth occlusion against terrain and buildings
comes for free. MC's GUI (hotbar, inventory screens, hand) is read back from MC's framebuffer
and drawn as a screen-space overlay in CS1.

## Excavation: four distinct capabilities

1. Breaking placed Minecraft blocks: pure Minecraft, mirrored as mesh updates.
2. Open pits in native terrain: CS1 terrain is a 2.5D heightfield; lowering it is supported by the
   game's own terrain tools.
3. Volumetric tunnels with an intact roof: **a heightfield cannot represent this.** It needs CS1's
   native terrain to be hidden inside dug cells and the cut surfaces drawn by us, the way SkyCraft
   clones and cuts Skyrim meshes (`DigMesh.cpp`). The CS1 hook for suppressing terrain rendering in a
   region is the project's highest-risk unknown (milestone 2 spike T1, before any polish).
4. Altering buildings and roads: CS1 objects, not terrain; removal/bulldoze APIs exist, but partial
   cuts are a rendering problem like (3).

Lowering terrain is never presented as (3).

## Save identity and recovery

- First enable in a city: CS1 generates a `saveId` (UUID) stored in the save's mod data.
- MC keeps one mirror world per `saveId` (`skylines-<saveId>`), created on first pairing.
- On load, a CS1 save whose `saveId` has no world gets a new world; a world opened against the
  wrong `saveId` is refused and the user is told. Saving the city asks MC to flush first and
  records MC's acknowledgement; a "Save As" in CS1 forks: new `saveId`, MC world copied.
- If MC dies or the link drops, CS1 returns to normal city controls within one frame (camera
  restored, input released) and shows the status; MC freezes its player in place.

## Coordinates

1 block = 1 metre. `mc = (cs.x, cs.y, -cs.z)`, `yaw = wrap180(unityY + 180)`, `pitch = unityX`
(`protocol/minecraft-skylines-v1.md`, tested by `protocol/vectors/coords.json`).

## Verified versus assumed

Everything about CS1's API below the `Skylines.Host` line is unverified until the game's
assemblies are available (see `docs/SETUP.md`). `docs/CS1-API-NOTES.md` records each API as
verified (with the assembly and signature) or as a hypothesis.
