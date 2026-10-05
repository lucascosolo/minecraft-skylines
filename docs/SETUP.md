# Setup

## What lives where

| Thing | Location | In Git? |
|---|---|---|
| Source, specs, docs | this repository | yes |
| CS1 reference assemblies (read-only copies) | `~/.cache/minecraft-skylines/refs/cs1/Managed/` | **never** (proprietary) |
| Environment facts about the local install | `~/.cache/minecraft-skylines/refs/environment.txt` | no |
| SkyCraft reference clone | `~/.cache/minecraft-skylines/ref/SkyCraft` | no |
| .NET SDK 10.0.401 | `~/.cache/dotnet/sdk10/` | no |
| NuGet packages, dotnet home | `~/.cache/minecraft-skylines/{nuget,dotnet-home}` | no |
| Gradle caches | `~/.cache/gradle-home` (shared, machine-wide) | no |
| Build outputs | `cs1/**/bin`, `cs1/**/obj`, `minecraft/**/build` | no |
| Test reports | `reports/` | no |

## 1. Reference files from the Cities: Skylines install (once, and after each game update)

The build compiles against the game's own assemblies, which cannot be committed or downloaded.
Run this in a normal terminal (not inside the agent's sandbox, which cannot see your Steam folder):

    bash ~/Workspaces/minecraft-skylines/tools/collect-cs1-refs.sh

It finds Steam (native, Flatpak or Snap) and every library folder, copies `Assembly-CSharp.dll`,
`ColossalManaged.dll`, `ICities.dll`, `UnityEngine.dll` (and `UnityEngine.UI.dll` /
`Assembly-CSharp-firstpass.dll` if present) into the cache, and writes `environment.txt` with the
Steam build id, native-vs-Proton, the Unity version, whether the Harmony workshop item is
subscribed, the names of local mods, whether a Minecraft launcher folder exists, and a copy of the
last CS1 `Player.log`. It reads only; it writes nothing into the game or any save.

## 2. Build and check everything that runs without the games

    tools/check.sh

Runs: vector regeneration (must be unchanged), the conformance suite against the Python reference,
the C# build (net35 + net10.0) and unit tests, C# host and guest conformance, the Gradle build and
JUnit tests, Java guest conformance, and a C#-host-to-Java-guest interop run. Reports go to
`reports/`. `tools/check.sh` never runs a clean task and deletes nothing.

Manual equivalents: `~/.cache/dotnet/sdk10/dotnet build cs1/MinecraftSkylines.sln -c Release`;
`cd minecraft && ./gradlew build`.

## 3. Stand-ins for either game

    python3 protocol/reference/ref_peer.py host     # pretends to be CS1 on 127.0.0.1:47615
    python3 protocol/reference/ref_peer.py guest    # pretends to be Minecraft

With the default `--app minecraft-skylines` they exchange `HOST_STATUS` / `GUEST_STATUS`.

## 4. Local game test procedure

Written per milestone once there is something to load; see `docs/TESTING.md`.
