#!/usr/bin/env bash
# Copy the built Cities: Skylines mod into the game's local mods folder. Run it yourself in a normal
# terminal (the agent's sandbox cannot write there):
#
#     bash ~/Workspaces/minecraft-skylines/tools/install-cs1-mod.sh [build-output-dir]
#
# Without an argument it installs the repo's current Release build. With one, it installs that
# folder instead (e.g. a pinned build of a specific commit).
# Copies four DLLs into Addons/Mods/MinecraftSkylines/ (overwriting older copies of the same four
# files) and, if absent, writes launch.cfg beside them. It deletes nothing and touches no save, game file or other mod. To uninstall, move that
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

# Developer auto-start config for Ctrl+Shift+M (see cs1/src/MinecraftSkylines.Mod/MinecraftLauncher.cs).
# Written only when absent: a user-edited launch.cfg is never overwritten.
CFG="${DEST:?}/launch.cfg"
if [ -e "${CFG:?}" ]; then
  echo "launch.cfg exists, left unchanged: ${CFG}"
else
  cat > "${CFG:?}" <<CFGEOF
# Minecraft auto-start for Ctrl+Shift+M. One key = value per line; # starts a comment.
command = ${ROOT:?}/minecraft/gradlew
args = --no-daemon --console=plain :fabric:runClient -PmcskylinesHidden
working_dir = ${ROOT:?}/minecraft
env.GRADLE_USER_HOME = ${HOME:?}/.cache/gradle-home
connect_timeout_seconds = 180
# Start Minecraft hidden ahead of time: game_start (main menu), city_load, or off (only on Ctrl+Shift+M).
prewarm = game_start
CFGEOF
  echo "wrote ${CFG}"
fi
echo "Mod folder: ${DEST}"
echo "Now start Cities: Skylines, open Content Manager > Mods and enable 'Minecraft Skylines'."
