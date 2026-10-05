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
| M5 | Small native-terrain excavation (open pit) with matching appearance, collision, persistence | Dig a 3×3×2 pit: terrain visibly lowered/cut, walls textured, walk into it, survives reload | Terrain material reuse for cut walls; CS1 terrain edits vs MC authority |
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

Open, to be settled by an in-game experiment early in M2 (a debug key that clips one 4 m cell):
1. Does the terrain shader actually discard clipped cells (and what is drawn in their place)?
2. Can our replacement surface match the terrain's look (same material on our own mesh, or MC-style
   blocks as SkyCraft does for partly dug cells)?
3. CS1's own raycasts and simulation still see the original heights (`m_finalHeights`); acceptable
   for the player, revisited in M7 for citizens and vehicles.

Fallback if clipping does not hide terrain: Harmony-patch `TerrainPatch.Render` to skip patches and
redraw them ourselves (heavy), or a custom terrain shader from an AssetBundle (needs Unity 5.6.7f1).
