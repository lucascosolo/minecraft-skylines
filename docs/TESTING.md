# Testing

Two kinds of evidence, never mixed: **sandbox** (automated, no game running; proves protocol,
codecs, state machines, coordinate maths, build health) and **in game** (the owner runs the games
and returns logs; the only evidence that engine integration works).

## Automated: `tools/check.sh`

| Check | What it proves | Report |
|---|---|---|
| Vector regeneration | `protocol/vectors/*.json` still match the reference codec | — |
| Conformance vs Python reference | the conformance suite itself is right (host 17, guest 10 scenarios) | `reports/protocol/` |
| C# build | net35 assemblies compile against CS1's real assemblies and BCL 2.0/3.5 only; net10 for tests | build log |
| C# unit tests | codec vs golden vectors (valid and invalid frames), coordinate vectors, outbound queue bound, retry schedule | `reports/cs1/unit_*.trx` |
| C# conformance | `Skylines.Bridge` host and guest obey `bridge-v1.md` (handshake, rejections, busy, heartbeats, timeouts, goodbye, protocol errors, reconnect, graceful shutdown) | `reports/cs1/conformance-*.xml` |
| Java build + JUnit | bridge codec vs vectors; app messages and coordinates vs vectors; Fabric mod compiles against MC 26.3 | `minecraft/*/build/test-results/` |
| Java conformance | `dev.mcskylines.bridge.BridgeGuest` obeys the spec (guest, 10 scenarios) | `reports/java/conformance-guest.xml` |
| Interop | real C# host and real Java guest: handshake, heartbeats across 3 timeout periods, goodbye both ways, reconnect | `reports/interop.xml` |

What the sandbox runs do **not** cover: Unity 5.6's Mono runtime (the C# tests run on .NET 10),
CS1's mod loader, frame timing inside either game, and the Fabric client at runtime.

### Latest run

2026-10-05 15:12 UTC, all green: reference 17/17 + 10/10, C# build 0 warnings, C# conformance
17/17 + 10/10, Java build + JUnit (bridge 17, fabric 14 tests), Java conformance 10/10, interop PASS.

## In game: milestone 1 procedure (owner)

Prerequisites: Cities: Skylines started once **without** the mod (lets Steam Cloud restore saves and
gives a clean baseline `Player.log`).

1. **Install the CS1 mod** (copies 4 DLLs; nothing else touched):
   `bash ~/Workspaces/minecraft-skylines/tools/install-cs1-mod.sh`
2. **Start Cities: Skylines**, Content Manager → Mods → enable *Minecraft Skylines*.
   - Expected: a small box top-left: `Link: listening on 127.0.0.1:47615`. F7 toggles it.
3. **Start the Minecraft dev client** (its own folder `minecraft/fabric/run/`, offline test name,
   does not use your launcher, account or worlds):
   `cd ~/Workspaces/minecraft-skylines/minecraft && GRADLE_USER_HOME=~/.cache/gradle-home ./gradlew --no-daemon :fabric:runClient`
   First run downloads Minecraft's assets (a few hundred MB).
   - Expected within ~2 s of the title screen: CS1 box shows `Link: connected to Minecraft ...`;
     in Minecraft, create a test world and type `/skylines status`: `connected`, peer
     `Cities: Skylines <version>`.
4. **Load a test city**: load any city, then immediately *Save As* a new name like `MCSK-Test-1`
   and keep working only in that copy.
   - Expected: CS1 box shows `City: <name>  (not paired)`; `/skylines status` shows the city.
     Milestone 1 never pairs a city, so nothing is written into the save.
   - During the loading screen the link must stay connected (heartbeats run off the main thread).
5. **Save** the test city (Esc → Save): saving works as normal and the mod log shows no
   `wrote save id` line.
6. **Quit Minecraft**: CS1 box returns to `listening` within ~1 s, `Last disconnect: peer_goodbye`.
   Start it again: reconnects without touching CS1.
7. **Quit Cities: Skylines** with Minecraft running: Minecraft's chat/log shows the disconnect
   (`peer_goodbye`, code 1 shutting down), and it keeps retrying quietly.

Return these files (the evidence of the 2026-10-05 M1 run is in `~/.cache/minecraft-skylines/evidence/m1/`):
- `~/.local/share/Colossal Order/Cities_Skylines/ModLogs/MinecraftSkylines.log`
- `~/.config/unity3d/Colossal Order/Cities_ Skylines/Player.log` (Unity replaces the colon with an underscore)
- `~/Workspaces/minecraft-skylines/minecraft/fabric/run/logs/latest.log`
- a screenshot of the CS1 box while connected in a city (optional).

Copy them into `~/.cache/minecraft-skylines/evidence/m1/` (the agent can read that folder):

    mkdir -p ~/.cache/minecraft-skylines/evidence/m1 && cp ~/.local/share/"Colossal Order"/Cities_Skylines/ModLogs/MinecraftSkylines.log ~/.config/unity3d/"Colossal Order"/"Cities_ Skylines"/Player.log ~/Workspaces/minecraft-skylines/minecraft/fabric/run/logs/latest.log ~/.cache/minecraft-skylines/evidence/m1/

## In game: milestone 2 procedure (owner)

Build under test: `main` at the commit named in `.agent/ledger-m2.md`. About 20 minutes.

**Setup**
1. `bash ~/Workspaces/minecraft-skylines/tools/install-cs1-mod.sh` (Cities: Skylines closed).
2. Start Cities: Skylines, load the test city. Before testing, build a short **elevated road or
   bridge** with a ramp up to it, ideally over a slope, so there is a deck to climb and walk under.
3. Start the Minecraft dev client (window visible for this first test):
   `cd ~/Workspaces/minecraft-skylines/minecraft && GRADLE_USER_HOME=~/.cache/gradle-home ./gradlew --no-daemon :fabric:runClient`
   Stay on the title screen: the mod opens its own void world `skylines-dev` when needed.
   Wait until the Cities: Skylines box says `Link: connected`, then click back into Cities: Skylines.

**A. Spike T1 marker check (city camera, game paused)**
4. Point at grass and press **Ctrl+Shift+C**. Two red (or magenta) 2 m cubes appear: one on the
   ground beside the hole, one 3 m under the hole. Screenshot from a low angle, looking into the hole.
   - Both cubes visible → the hole is see-through (tunnels possible as planned).
   - Only the side cube visible → the clip paints an opaque fill (route needs rethinking).
   - Neither visible → our way of drawing objects is wrong (also useful to know).
5. **Ctrl+Shift+U** removes the clip and both cubes.

**B. Minecraft mode**
6. Point the city camera at open ground and press **Ctrl+Shift+M**. Expected: the view drops to eye
   height where the camera was looking, "waiting for Minecraft…", then you control the player.
   The Minecraft window shows the `skylines-dev` world.
7. Walk (WASD), look (mouse), jump (Space), sprint (Ctrl or double-tap W), sneak (Shift).
   Check: walking on slopes without floating or sinking; walking onto roads; climbing the ramp onto
   the elevated road/bridge; walking off its edge; walking underneath it; trying a steep cliff.
8. Press Space, 1, 2, 3, F1-F4 while walking: the city must not pause, change speed or open tools.
9. Press **Esc**: back to the normal city camera exactly where it was, cursor free, no pause menu.
10. Ctrl+Shift+M again, then quit Minecraft: control must return to the city within a moment.
11. Start Minecraft again, Ctrl+Shift+M, then Esc → Main menu in Cities: Skylines (via Esc twice):
    clean exit, shortcuts work normally afterwards.

**Notes worth writing down:** where you floated, sank or got stuck; lag or stutter of the camera;
whether mouse look is too fast or slow; anything that pressed through to the city.

Evidence: copy into `~/.cache/minecraft-skylines/evidence/m2/` with
`mkdir -p ~/.cache/minecraft-skylines/evidence/m2 && cp ~/.local/share/"Colossal Order"/Cities_Skylines/ModLogs/MinecraftSkylines.log ~/.config/unity3d/"Colossal Order"/"Cities_ Skylines"/Player.log ~/Workspaces/minecraft-skylines/minecraft/fabric/run/logs/latest.log ~/.cache/minecraft-skylines/evidence/m2/`
plus screenshots.

### Milestone 2, round 1 result (owner, 2026-10-05; evidence `~/.cache/minecraft-skylines/evidence/m2/`)

Verified in game: entering and leaving Minecraft mode three times (teleport acknowledged in
0.1-3.7 s, up to 319 collision regions / 51,324 triangles streamed, player-mode frame cost
0.1-0.2 ms average), walking up ramps and under bridges, Esc back to the city camera. Found:
(1) ground roads had no sides, so the player walked under their surface (fixed: 1 m slabs);
(2) Ctrl+C on Gradle left the Minecraft window running (now: Minecraft quits 10 s after CS1 says
goodbye on exit; start the client with `--no-daemon` so Gradle ends with it); (3) walking feels a
bit slow (Minecraft's own 4.3 m/s; Minecraft logged no lag); (4) T1: the clipped area's blue drew
over a cube in front of it; the cube itself used Unity's error shader, so the test is redone with
the game's prop shader and a marker on the underground camera's layer.

### Milestone 2, round 2 (owner)

Minecraft now starts by itself: no Gradle terminal needed.

1. Once: `cd ~/Workspaces/minecraft-skylines/minecraft && GRADLE_USER_HOME=~/.cache/gradle-home ./gradlew --stop`
   (stops the daemon left from round 1). Close any Minecraft window still open.
2. `bash ~/Workspaces/minecraft-skylines/tools/install-cs1-mod.sh` (also writes `launch.cfg`, which
   tells the mod how to start Minecraft; it prints where). An existing `launch.cfg` is kept; without a
   `prewarm` line it behaves as `prewarm = game_start`.
3. Start Cities: Skylines. About 6 s after the main menu appears, Minecraft starts hidden in the
   background (no window). Check the mod log for `companion started (prewarm GameStart, posix_spawn, pid N)`;
   on failure it now logs `start failed (…)` with the exception type, native error code and stack.
   Load the test city; the status overlay shows "Minecraft: starting in background (N s)" until it
   connects, and Minecraft opens its `skylines-dev` world by itself once the city is loaded.
4. **T1 test 2** (paused, city camera): point at grass, **Ctrl+Shift+C**. Three cubes: a red one
   beside the hole (control), and under the hole a red one (normal prop layer) and a yellow one
   (the underground camera's layer). Screenshot from a low angle into the hole, and one with the
   control cube between the camera and the hole. **Ctrl+Shift+I**, then **Ctrl+Shift+U**.
5. Wait until the overlay no longer says "starting in background", point at open ground,
   **Ctrl+Shift+M**. Expected: Minecraft mode within a second or two, no Minecraft window. (Pressed
   earlier, it shows "Starting Minecraft…" and enters by itself once connected.) `ps` should show one
   Gradle-launched Minecraft only.
6. Walk from grass onto a ground road: you should step up onto it instead of walking through it. Esc.
7. Quit Cities: Skylines to desktop. Minecraft should end by itself within about 10 s.
8. Copy evidence into `~/.cache/minecraft-skylines/evidence/m2r2/`:
   `mkdir -p ~/.cache/minecraft-skylines/evidence/m2r2 && cp ~/.local/share/"Colossal Order"/Cities_Skylines/ModLogs/MinecraftSkylines*.log ~/.config/unity3d/"Colossal Order"/"Cities_ Skylines"/Player.log ~/Workspaces/minecraft-skylines/minecraft/fabric/run/logs/latest.log ~/.cache/minecraft-skylines/evidence/m2r2/`
   plus screenshots. (`MinecraftSkylines*.log` includes the new `MinecraftSkylines-companion.log`
   with Minecraft's own output.)

## Automated in-game self-test (milestones 2 and 3)

The mod can run the milestone 2 acceptance checks by itself; the owner only starts the game and
loads a test city. It reads the city and drives Minecraft mode; it never edits the city or its save.

- **Enable:** `bash tools/install-cs1-mod.sh --selftest` sets `selftest = city_load` and adds
  `-PmcskylinesDebugCommands` to the `args` line of `launch.cfg` (a dated `launch.cfg.backup-*`
  copy is kept when a line is changed; without the flag the script leaves both alone). By hand: add
  `selftest = city_load` to `launch.cfg` in the mod folder (default `off`). It then runs once after each city load. In a loaded city,
  **Ctrl+Shift+T** starts it by hand (also at most once per city load). **Esc** aborts; the report
  then says `"aborted": true`.
- **What it does:** waits up to 240 s for Minecraft (connected, app minor >= 1, in its world, not in
  a teleport hold); without it, only S1 runs and the rest report `skip` "no Minecraft". Then, one at
  a time, each with a timeout: S1 road surface vs collision top (20 ground segments within 400 m of
  the city camera's target, 5 points each, tolerance 0.05 m, plus the curb step 1 m outside each
  edge); S2 walk from terrain 3 m beside a ground road onto it (W held until the feet reach the
  centre line, at most 2.5 s; feet within 0.15 m of the road top, on the strip); S7 screenshots (in
  Minecraft mode at the end of S2 and from a city camera 15 m from that road, collision wireframe
  on); S3 walk 3 s at a bridge railing (must stay on the deck); S4 walk 1 s on terrain under a deck
  at least 4 m up (feet within 0.3 m of terrain); S5 2 s up and 2 s down a 10-30 degree slope
  within 300 m (feet within 0.25 m of terrain, never airborne > 0.5 s); S6 the city camera and
  controller equal to before the first entry (1e-3) with no modal left up. S8 block rendering
  (milestone 3; runs after S7, skips unless app minor >= 2, `launch.cfg` args carry
  `-PmcskylinesDebugCommands` and Minecraft is in `skylines-dev`): enters at the S2 spawn facing
  the nearest axis, sends `DEBUG_COMMAND fill`s for a 2-high row of stone, grass_block,
  oak_planks, glass, oak_leaves, water 3 blocks ahead at feet level, waits up to 10 s for a
  `SECTION_MESH` covering it, holds W 1.5 s (pass needs the player stopped 0 to 0.15 m in front of
  the row: placed blocks are solid), leaves Minecraft mode and takes `s8_material_0..3.png` with
  the city camera 6 m in front of the row, 1.5 m up, one per material variant (0 prop shader with
  the atlas; 1 prop shader plus neutral `_XYSMap`/`_ACIMap`; 2 building shader plus the same maps;
  3 Unity `Diffuse`). The screenshots are the material verdict: compare them for texture
  orientation (grass side green on top), vertex tint (grass top and leaves green, not grey),
  lighting, and depth against terrain. Measurements: atlas size, time to first mesh, sections and
  vertices received, mesh build ms, gap to the row before and after walking. Its cleanup fills the
  row with air. In normal play **Ctrl+Shift+B** cycles the variant live (`block_material = 0..3` in
  `launch.cfg` sets the start); the status box shows it. S9 GUI overlay and screen input (milestone 3;
  runs after S7, skips unless app minor >= 3): enters at the S2 spawn, waits up to 10 s for a mapped
  `OVERLAY_OFFER` and 10 new frames (measures frames/s, frame and screen size, upload avg/max ms,
  blend path), takes `s9_hud.png`, presses E with synthetic keys and waits up to 3 s for
  `SCREEN_OPEN` (cursor must be shown and unlocked), moves the cursor to the screen centre with cursor
  events, takes `s9_inventory.png`, presses Esc with synthetic keys and needs `SCREEN_OPEN` cleared
  within 3 s with Minecraft mode still on and the cursor relocked; `measurements.steps` records
  pass/fail per step. S3-S5 skip when the city
  has no suitable bridge or slope; build one near the camera's view first.
- **Report:** `~/.local/share/Colossal Order/Cities_Skylines/ModLogs/selftest/<yyyyMMddTHHmmssZ>/report.json`
  (keys `run_started_utc`, `game_version`, `mod_version`, `city_name`, `minecraft_peer`,
  `aborted`, `abort_reason`, `scenarios[{id, name, status, reason, duration_ms, measurements}]`,
  `summary{pass, fail, skip, error}`) and the PNGs beside it (two from S2/S7, four from S8, two from S9). The mod log and the status box
  show `Self-test: N pass, N fail, N skip, N error — report at …`.
- **Bring it back:**
  `mkdir -p ~/.cache/minecraft-skylines/evidence/selftest && cp -r ~/.local/share/"Colossal Order"/Cities_Skylines/ModLogs/selftest/. ~/.local/share/"Colossal Order"/Cities_Skylines/ModLogs/MinecraftSkylines*.log ~/.cache/minecraft-skylines/evidence/selftest/`

### Cross-language GUI overlay check (sandbox, 2026-10-05)

`minecraft/bridge/build/install/bridge/bin/overlay-demo <file> 320 180 180` (Java OverlayWriter,
~60 Hz) against `cs1/tools/Skylines.Overlay.Probe/bin/Release/net10.0/probe <file> 4` (C#
SharedOverlayReader, separate process): 152 of 180 frames consumed (latest-frame-wins), 0 rejected,
each frame's first pixel byte equal to its frame id. Proves the 1.3 layout, the triple-buffer
atomics and pixel transfer agree across languages; says nothing about Minecraft's renderer or Unity.
