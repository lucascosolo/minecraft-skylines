# Milestones

Status per milestone uses five separate columns, never merged: **implemented**, **built**,
**sandbox-tested** (automated, no game running), **verified in game**, **blocked / unverified**.
A sandbox test is never evidence that engine integration works.

## Current: M1, the bridge

| | Status (2026-10-05) |
|---|---|
| Implemented | Protocol spec, Python reference + conformance suite; C# bridge (host + guest) and app protocol; Java bridge (guest) and Fabric client mod skeleton. The CS1 mod entry is **not** written: it needs the CS1 assemblies |
| Built | see `TESTING.md` for the latest run |
| Sandbox-tested | see `TESTING.md` |
| Verified in game | nothing yet |
| Blocked | CS1 assemblies (`tools/collect-cs1-refs.sh`); a test city save; the owner running Minecraft with the mod |

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

## Spike T1: can CS1's terrain be suppressed in a region? (planned for M2)

Question: with only public API plus Harmony, can we stop native terrain from rendering inside a set
of dug cells while the rest renders normally, and draw replacement geometry with the terrain's own
material so it matches? Candidate routes, to be checked in the assemblies first, then in game:

1. Replace the terrain patch meshes (CS1 terrain is drawn per patch) with our own cut meshes,
   SkyCraft-style (clone, cut, hide original).
2. A shader-side clip: give the terrain material a property/texture that discards fragments inside
   dug cells (needs a custom shader built for Unity 5.6, loaded from an AssetBundle).
3. Fallback if both fail: tunnels exist only for collision and MC-rendered walls; the terrain stays
   drawn over the entrance (fails M6; would be documented as the blocker, not hidden).
