#!/usr/bin/env bash
# Copy the built Cities: Skylines mod into the game's local mods folder. Run it yourself in a normal
# terminal (the agent's sandbox cannot write there):
#
#     bash ~/Workspaces/minecraft-skylines/tools/install-cs1-mod.sh [build-output-dir]
#
# Without an argument it installs the repo's current Release build. With one, it installs that
# folder instead (e.g. a pinned build of a specific commit).
# Copies four DLLs into Addons/Mods/MinecraftSkylines/ (overwriting older copies of the same four
# files). It deletes nothing and touches no save, game file or other mod. To uninstall, move that
# one folder away. Path logic follows ColossalFramework.IO.DataLocation on Linux.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="${1:-${ROOT:?}/cs1/src/MinecraftSkylines.Mod/bin/Release/net35}"
DATA="${XDG_DATA_HOME:-${HOME:?}/.local/share}"
DEST="${DATA:?}/Colossal Order/Cities_Skylines/Addons/Mods/MinecraftSkylines"

FILES=(MinecraftSkylines.dll Skylines.Host.dll Skylines.Bridge.dll MinecraftSkylines.Protocol.dll)
# Optional assemblies: installed when the build has them.
[ -f "${SRC:?}/Skylines.Core.dll" ] && FILES+=(Skylines.Core.dll)
for f in "${FILES[@]}"; do
  [ -f "${SRC:?}/$f" ] || { echo "missing ${SRC}/$f: build first (tools/check.sh, or dotnet build cs1/MinecraftSkylines.sln -c Release -m:1)"; exit 1; }
done
mkdir -p "${DEST:?}"
for f in "${FILES[@]}"; do
  cp -f "${SRC:?}/$f" "${DEST:?}/$f"
  echo "installed $f  sha256=$(sha256sum "${DEST:?}/$f" | cut -d' ' -f1)"
done
echo "Mod folder: ${DEST}"
echo "Now start Cities: Skylines, open Content Manager > Mods and enable 'Minecraft Skylines'."
