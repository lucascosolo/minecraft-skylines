# Milestones

Status per milestone uses five separate columns, never merged: **implemented**, **built**,
**sandbox-tested** (automated, no game running), **verified in game**, **blocked / unverified**.
A sandbox test is never evidence that engine integration works.

## Current: M1, the bridge

| | Status (2026-10-05) |
|---|---|
| Implemented | Protocol spec + Python reference + conformance suite; C# `Skylines.Bridge` (host+guest), `MinecraftSkylines.Protocol`, reusable `Skylines.Host` (log, main-thread pump, city state, save identity, status overlay) and the CS1 mod (`MinecraftSkylines.dll`: link status, `HOST_STATUS`, save id in the save); Java `bridge` (guest) and the Fabric client mod (`GUEST_STATUS`, `/skylines status`, chat notices) |
| Built | yes: C# 0 warnings against the owner's CS1 assemblies (build 22724702); Fabric mod jar for MC 26.3 with the bridge nested |
| Sandbox-tested | yes: all of `tools/check.sh` green (see `TESTING.md`) |
| Verified in game | **yes, 2026-10-05** (owner run, logs in `~/.cache/minecraft-skylines/evidence/m1/`): CS1 1.21.1-f9 loaded the 4 DLLs on Unity 5.6 Mono with no mod exceptions; the Fabric dev client connected; status flowed both ways; the link survived a 10 s level load; Minecraft quit → `peer_goodbye` → listening; Minecraft restart → reconnected; CS1 quit → goodbye seen by Minecraft, which kept retrying. Bridge tick avg 0.036 ms, max 33.9 ms (one spike, see below) over 23,055 frames |
| Blocked / unverified | version-mismatch rejection in game (sandbox only); a city save is not visible in the logs (owner reports it worked; CS1 does not log city saves); the 33.9 ms one-frame spike is unexplained (profile in M2); MC's log shows its OpenGL backend failing with `EGL_BAD_DISPLAY` before continuing, which matters for M3's pixel readback |

Exit criteria: CS1 loads the mod (Player.log shows it), shows link status in the city, accepts a
Minecraft client's handshake, shows both sides' versions, survives a city save/load (heartbeats
continue through the loading stall), and returns to `listening` cleanly when Minecraft quits;
Minecraft shows the same through `/skylines status`. Rejection on a version mismatch is shown on
both sides.

## Backlog

| # | Milestone | Done when | Highest risk, investigated first |
|---|---|---|---|
| M1 | Loadable CS1 mod, diagnostics, bridge with versioned handshake, status, clean disconnect | exit criteria above | Mono socket behaviour in Unity 5.6 (old runtime) |
| M2 | First-person movement with MC physics; collision on terrain, slopes, roads, bridges | Walk, sprint-jump, climb a road ramp and cross a bridge deck in a test city; fall off nothing | **Spike T1 (terrain rendering suppression)** runs here even though digging is M5/M6, because it decides whether M6 is possible. Also: are building meshes readable for collision? |
| M3 | MC hotbar/inventory overlay; a placed block rendered in the city with correct depth and collision | Place a block on a road, walk behind a building and see it occluded, stand on it | Shader/material for MC blocks under CS1 lighting; pixel transport for the GUI |
| M4 | Breaking blocks; persistence across paired save/reload | Build, save city, quit both, reload: blocks back; load a different city: not there | `saveId` pairing, MC flush ordering on save |
| M5 | Small native-terrain excavation (open pit) with matching appearance, collision, persistence | Dig a 3×3×2 pit: terrain visibly lowered/cut, walls textured, walk into it, survives reload | Terrain material reuse for cut walls; CS1 terrain edits vs MC authority; **CS1's water simulation floods lowered terrain below the water level (owner, 2026-10-05)**, so pits near water need a rule (let it flood as CS1 would, or keep pits clip-only) |
| M6 | **Decisive volumetric test**: short tunnel with intact roof | 1×2×8 tunnel into a hillside: roof terrain still rendered above, interior faces visible, collision correct inside and on the roof, survives reload | Depends entirely on T1's answer |
| M7 | Broader interactions guided by M1-M6: buildings and roads (bulldoze/cut), citizens and vehicles as entities, simulation reactions | defined after M6 | |

## Spike T1: can CS1's terrain be cut in a region?

**Desk result (2026-10-05, from the decompiled assemblies; not yet tried in game).** CS1 terrain has
no per-patch CPU mesh: `TerrainManager.EndRenderingImpl` draws shared flat meshes per patch and LOD
with `Graphics.DrawMesh(..., m_terrainMaterial, ..., m_materialBlock1, ...)`, displaced by a height
texture in the shader. So SkyCraft's clone-and-cut route does not apply. But the game already has a
per-cell **clip mask** (`SurfaceCell.m_clipped`, 4 m cells, uploaded in `_SurfaceTexA`), set through
`TerrainModify`'s surface `Clip` and used for quays and tunnel-style cuts.

Resulting plan for M6 (tunnel with intact roof): leave the terrain above the tunnel untouched (the
roof is native terrain), draw the cavity's interior faces ourselves (an `IRenderableManager`), and
clip only the 4 m surface cells where the excavation breaks the surface, redrawing the undug part
of those cells ourselves. MC's collision comes from our own voxel-aware triangles, not CS1's.

**In-game result 1 (owner, 2026-10-05, evidence `~/.cache/minecraft-skylines/evidence/t1/`):**
the clip makes native terrain stop rendering: every clipped 12 m area showed a perfectly uniform
colour (RGB 66,146,218 at every sampled pixel, no texture or shading), the clip survived a road
built nearby (diagnostics still 9/9 clipped after the recompute), a clip over road surface left
the road mesh drawn, and restore by key and on level unload both worked. Still open: whether the
uniform colour is a true hole (geometry behind it visible) or an opaque fill. The probe now places
a red cube 3 m below each clip plus a control cube beside it to settle this. Clip-based tunnels also
leave CS1's water simulation untouched (heights unchanged).

Open, to be settled by an in-game experiment early in M2 (a debug key that clips one 4 m cell):
1. Does the terrain shader actually discard clipped cells (and what is drawn in their place)?
2. Can our replacement surface match the terrain's look (same material on our own mesh, or MC-style
   blocks as SkyCraft does for partly dug cells)?
3. CS1's own raycasts and simulation still see the original heights (`m_finalHeights`); acceptable
   for the player, revisited in M7 for citizens and vehicles.

Fallback if clipping does not hide terrain: Harmony-patch `TerrainPatch.Render` to skip patches and
redraw them ourselves (heavy), or a custom terrain shader from an AssetBundle (needs Unity 5.6.7f1).

### Tool route (2026-10-05, read in the decompiled Assembly-CSharp; not yet tried in game)

**How a clip reaches `SurfaceCell` data.** `TerrainModify.UpdateAreaImplementation` is the only
writer of the detail surface. For every recomputed area it first zeroes the scratch buffer
`m_tempSurface` (TerrainModify.cs:308, `surfaceCell.m_clipped = 0` and all other channels), then
calls `TerrainManager.Managers_TerrainUpdated` (TerrainModify.cs:381 → TerrainManager.cs:2757),
which calls `TerrainUpdated` on every registered `ITerrainManager` (NetManager.cs:3866,
BuildingManager.cs:6797, which reach `NetSegment.TerrainUpdated` NetSegment.cs:1338 and
`Building.TerrainUpdated` Building.cs:863, where `TerrainModify.ApplyQuad(..., Surface.Clip)` is
called). Only after that is `m_tempSurface` copied into `m_detailSurface` (TerrainModify.cs:481) and
the patches flagged for a texture refresh (TerrainModify.cs:526; `TerrainPatch.Refresh` puts
`m_clipped` into `_SurfaceTexA.r`, TerrainPatch.cs:326). `ApplyQuad` writes only into
`m_tempSurface` and returns at once unless a surface recompute is in progress
(TerrainModify.cs:646). So: **`ApplyQuad` is only meaningful inside the game's recompute pass, and a
direct write to `m_detailSurface` would be wiped by the next recompute touching that area** (any
road, building, zoning or terraforming edit nearby).

**Route chosen (no Harmony):** register our own `ITerrainManager` with the public static
`TerrainManager.RegisterTerrainManager` (TerrainManager.cs:583; the game registers its own managers
the same way, SimulationManager.cs:578). Its `TerrainUpdated` re-applies our clip quads with
`ApplyQuad(a, b, c, d, Edges.None, Heights.None, Surface.Clip)` on every recompute, so the clip
survives nearby updates by construction. To apply or remove a clip we queue
`TerrainModify.UpdateArea(minX, minZ, maxX, maxZ, heights: false, surface: true, zones: false)` on the
simulation thread with `SimulationManager.AddAction` (SimulationManager.cs:725/743); actions run
inside `BeginUpdateArea`/`EndUpdateArea` on that thread, also while paused (SimulationManager.cs:871),
so we never race the game's own terrain writes. Removal = forget the quad, recompute the area.
`Edges.None` gives cells whose centre lies inside the quad a full 255 clip with no fade
(TerrainModify.cs:1134); a 12 m square centred on a 4 m cell centre covers exactly 3×3 cells.

**Limits found:** (1) the detail surface exists only for patches with `m_simDetailIndex != 0`
(TerrainModify.cs:231), which `GameAreaManager` assigns to owned/unlocked tiles
(`SetDetailedPatch`, TerrainManager.cs:903/940, GameAreaManager.cs:1000/1054); elsewhere a clip has no
effect. (2) There is no unregister API; the mask object stays registered for the process and is
inert when empty. (3) The surface is not saved (`TerrainManager.Data.Serialize`, TerrainManager.cs:46,
writes heights only) and the recompute is surface-only, so heights and saves are untouched.

**Debug probe** (`cs1/src/MinecraftSkylines.Mod/Debug/TerrainClipProbe.cs`, logic in
`cs1/src/Skylines.Host/Terrain/TerrainClipMask.cs`), only in a loaded city: Ctrl+Shift+C clips the
3×3 cells under the cursor (game terrain raycast `TerrainManager.RayCast`), Ctrl+Shift+U restores all,
Ctrl+Shift+I logs the 9 `SurfaceCell`s and the patch's detail indices. Everything is restored on level
unload and mod disable.

**What only the in-game test can show:** whether the terrain shader discards clipped pixels or
draws something else (the shader is not decompilable); what is visible through the hole (sky, water,
underground view, black); whether the clip holds after a nearby road or building is placed; and
whether the cut edge looks acceptable at 4 m resolution.
