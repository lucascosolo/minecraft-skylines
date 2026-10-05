#!/usr/bin/env bash
# Collect the minimum read-only reference files the build needs from the local Cities: Skylines
# install, plus version facts, into ~/.cache/minecraft-skylines/refs/. Run it yourself in a normal
# terminal (the agent's sandbox cannot read your Steam folder):
#
#     bash ~/Workspaces/minecraft-skylines/tools/collect-cs1-refs.sh
#
# It only reads the game and copies 4-6 DLLs; it never writes into the game, your saves, or any
# Minecraft folder, and it deletes nothing. Copies stay on this PC and are git-ignored.
set -euo pipefail

OUT="${HOME:?}/.cache/minecraft-skylines/refs"
DEST="${OUT:?}/cs1/Managed"
mkdir -p "${DEST:?}"
REPORT="${OUT:?}/environment.txt"
: > "${REPORT:?}"
say() { printf '%s\n' "$*" | tee -a "${REPORT:?}"; }

say "collected_at_utc: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
say "os: $(. /etc/os-release && echo "${PRETTY_NAME:-unknown}")"

# Steam roots: native, ~/.steam symlink, Flatpak, Snap.
roots=()
for r in "$HOME/.local/share/Steam" "$HOME/.steam/steam" "$HOME/.steam/root" \
         "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam" "$HOME/snap/steam/common/.local/share/Steam"; do
  [ -d "$r/steamapps" ] && roots+=("$(readlink -f "$r")")
done
# Library folders listed in each root's libraryfolders.vdf.
libs=()
for r in "${roots[@]}"; do
  libs+=("$r")
  vdf="$r/steamapps/libraryfolders.vdf"
  if [ -f "$vdf" ]; then
    while IFS= read -r p; do libs+=("$p"); done < <(sed -n 's/^[[:space:]]*"path"[[:space:]]*"\(.*\)"/\1/p' "$vdf")
  fi
done

CS=""; MANIFEST=""
for l in "${libs[@]}"; do
  if [ -f "$l/steamapps/appmanifest_255710.acf" ]; then
    MANIFEST="$l/steamapps/appmanifest_255710.acf"
    dir=$(sed -n 's/^[[:space:]]*"installdir"[[:space:]]*"\(.*\)"/\1/p' "$MANIFEST")
    CS="$l/steamapps/common/${dir:-Cities_Skylines}"
    WORKSHOP="$l/steamapps/workshop/content/255710"
    break
  fi
done
if [ -z "$CS" ] || [ ! -d "$CS" ]; then
  say "ERROR: Cities: Skylines (app 255710) not found under: ${libs[*]:-no Steam roots}"
  exit 1
fi
say "cs1_install: $CS"
say "cs1_steam_buildid: $(sed -n 's/^[[:space:]]*"buildid"[[:space:]]*"\(.*\)"/\1/p' "$MANIFEST")"
say "cs1_native_linux_binary: $([ -e "$CS/Cities.x64" ] && echo yes || echo no)"
say "cs1_windows_binary_present: $([ -e "$CS/Cities.exe" ] && echo yes || echo no)"

MANAGED="$CS/Cities_Data/Managed"
[ -d "$MANAGED" ] || { say "ERROR: $MANAGED missing"; exit 1; }
for f in Assembly-CSharp.dll ColossalManaged.dll ICities.dll UnityEngine.dll UnityEngine.UI.dll Assembly-CSharp-firstpass.dll; do
  if [ -f "$MANAGED/$f" ]; then
    cp --preserve=timestamps "$MANAGED/$f" "${DEST:?}/$f"
    say "copied: $f sha256=$(sha256sum "$MANAGED/$f" | cut -d' ' -f1)"
  else
    say "absent: $f"
  fi
done
say "managed_dir_listing: $(ls "$MANAGED" | tr '\n' ' ')"
# Unity version is embedded near the start of the main data file.
for d in "$CS/Cities_Data/mainData" "$CS/Cities_Data/globalgamemanagers"; do
  [ -f "$d" ] && say "unity_version: $(head -c 200 "$d" | strings | grep -m1 -E '^[0-9]+\.[0-9]+\.[0-9]+[a-z][0-9]+' || echo unknown)" && break
done

# Mod/runtime facts that change implementation choices (names only, nothing copied).
say "harmony_workshop_item_2040656402: $([ -d "${WORKSHOP:-/nonexistent}/2040656402" ] && echo subscribed || echo absent)"
say "workshop_item_count: $(ls "${WORKSHOP:-/nonexistent}" 2>/dev/null | wc -l)"
LOCALMODS="$HOME/.local/share/Colossal Order/Cities_Skylines/Addons/Mods"
say "local_mods_dir: $([ -d "$LOCALMODS" ] && echo "$LOCALMODS" || echo absent)"
[ -d "$LOCALMODS" ] && say "local_mods: $(ls "$LOCALMODS" | tr '\n' ' ')"
for log in "$HOME/.config/unity3d/Colossal Order/Cities: Skylines/Player.log"; do
  if [ -f "$log" ]; then
    cp --preserve=timestamps "$log" "${OUT:?}/cs1/Player.log"
    say "player_log: copied (last game run $(date -r "$log" -u +%Y-%m-%dT%H:%M:%SZ))"
  else
    say "player_log: absent"
  fi
done

# Minecraft: which launchers/instances exist (names only; no accounts, tokens or worlds read).
say "minecraft_dot_minecraft: $([ -d "$HOME/.minecraft" ] && echo present || echo absent)"
[ -d "$HOME/.minecraft/versions" ] && say "minecraft_versions: $(ls "$HOME/.minecraft/versions" | tr '\n' ' ')"
for p in "$HOME/.local/share/PrismLauncher" "$HOME/.var/app/org.prismlauncher.PrismLauncher/data/PrismLauncher"; do
  [ -d "$p" ] && say "prism_launcher: $p (instances: $(ls "$p/instances" 2>/dev/null | tr '\n' ' '))"
done
say "java_on_path: $(command -v java >/dev/null && java -version 2>&1 | head -1 || echo none)"
echo
echo "Done. Wrote ${REPORT}. Tell the agent the script finished."
