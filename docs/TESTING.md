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
   `cd ~/Workspaces/minecraft-skylines/minecraft && GRADLE_USER_HOME=~/.cache/gradle-home ./gradlew :fabric:runClient`
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

Return these files:
- `~/.local/share/Colossal Order/Cities_Skylines/ModLogs/MinecraftSkylines.log`
- `~/.config/unity3d/Colossal Order/Cities: Skylines/Player.log`
- `~/Workspaces/minecraft-skylines/minecraft/fabric/run/logs/latest.log`
- a screenshot of the CS1 box while connected in a city (optional).

Copy them into `~/.cache/minecraft-skylines/evidence/m1/` (the agent can read that folder):

    mkdir -p ~/.cache/minecraft-skylines/evidence/m1 && cp ~/.local/share/"Colossal Order"/Cities_Skylines/ModLogs/MinecraftSkylines.log ~/.config/unity3d/"Colossal Order"/"Cities: Skylines"/Player.log ~/Workspaces/minecraft-skylines/minecraft/fabric/run/logs/latest.log ~/.cache/minecraft-skylines/evidence/m1/
