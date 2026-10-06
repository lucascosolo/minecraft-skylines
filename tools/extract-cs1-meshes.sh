#!/usr/bin/env bash
# One-time: read the built-in building and prop meshes out of your Cities: Skylines install into
# ~/.cache/minecraft-skylines/cs1-meshes/meshes.bin, which the mod uses for collision where the game keeps
# its meshes GPU-only. Run it yourself in a normal terminal, no arguments (the agent's sandbox cannot read
# your Steam folder). Re-run after the game or a DLC updates.
#
#     bash ~/Workspaces/minecraft-skylines/tools/extract-cs1-meshes.sh
#
# It only reads the game; it writes nothing but the cache file (and the Python venv under ~/.cache, created
# with uv when missing), and deletes nothing. The cache stays on this PC and never enters git.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CACHE="${HOME:?}/.cache/minecraft-skylines"
VENV="${CACHE:?}/tools/unitypy-venv"
OUT="${CACHE:?}/cs1-meshes/meshes.bin"
export UV_CACHE_DIR="${UV_CACHE_DIR:-$HOME/.cache/uv}"

if [ ! -x "${VENV:?}/bin/python" ] || ! "${VENV:?}/bin/python" -c 'import UnityPy' 2>/dev/null; then
  UV="$(command -v uv || echo "$HOME/.local/bin/uv")"
  [ -x "$UV" ] || { echo "ERROR: uv not found (looked on PATH and in ~/.local/bin)"; exit 1; }
  echo "Creating ${VENV} with UnityPy 1.25.4 ..."
  "$UV" venv "${VENV:?}"
  "$UV" pip install --python "${VENV:?}/bin/python" "UnityPy==1.25.4"
fi

# Steam roots and library folders, as tools/collect-cs1-refs.sh.
roots=()
for r in "$HOME/.local/share/Steam" "$HOME/.steam/steam" "$HOME/.steam/root" \
         "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam" "$HOME/snap/steam/common/.local/share/Steam"; do
  [ -d "$r/steamapps" ] && roots+=("$(readlink -f "$r")")
done
libs=()
for r in "${roots[@]}"; do
  libs+=("$r")
  vdf="$r/steamapps/libraryfolders.vdf"
  if [ -f "$vdf" ]; then
    while IFS= read -r p; do libs+=("$p"); done < <(sed -n 's/^[[:space:]]*"path"[[:space:]]*"\(.*\)"/\1/p' "$vdf")
  fi
done
CS=""
for l in "${libs[@]}"; do
  if [ -f "$l/steamapps/appmanifest_255710.acf" ]; then
    dir=$(sed -n 's/^[[:space:]]*"installdir"[[:space:]]*"\(.*\)"/\1/p' "$l/steamapps/appmanifest_255710.acf")
    CS="$l/steamapps/common/${dir:-Cities_Skylines}"
    break
  fi
done
DATA="${CS:-/nonexistent}/Cities_Data"
[ -d "$DATA" ] || { echo "ERROR: Cities: Skylines (app 255710) Cities_Data not found under: ${libs[*]:-no Steam roots}"; exit 1; }

echo "Reading meshes from ${DATA} (read-only)"
"${VENV:?}/bin/python" "${HERE:?}/cs1_meshes.py" extract "${DATA:?}" "${OUT:?}"
echo
echo "Done. Start (or restart) Cities: Skylines; Player.log names each building's collision source."
