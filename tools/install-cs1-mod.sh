#!/usr/bin/env bash
# Copy the built Cities: Skylines mod into the game's local mods folder. Run it yourself in a normal
# terminal (the agent's sandbox cannot write there):
#
#     bash ~/Workspaces/minecraft-skylines/tools/install-cs1-mod.sh [--selftest] [--selftest-quit]
#         [--autoload "<save name>"] [build-output-dir]
#     bash ~/Workspaces/minecraft-skylines/tools/install-cs1-mod.sh --normal
#
# --selftest (any position): also ensures launch.cfg has "selftest = city_load" and that the args
# line carries -PmcskylinesDebugCommands (dated backup copy before any edit of an existing line).
# --selftest-quit: "selftest_quit = true" (quit the game after the self-test report). --autoload NAME:
# "autoload = NAME" (load that save from the main menu once per game start). --normal: copies nothing and
# only sets "autoload =", "selftest = off", "selftest_quit = false" (back to normal play). A changed key
# gets its old file kept as one dated backup per run; a missing key is appended.
# Without an argument it installs the repo's current Release build. With one, it installs that
# folder instead (e.g. a pinned build of a specific commit).
# Copies four DLLs into Addons/Mods/MinecraftSkylines/ (overwriting older copies of the same four
# files) and, if absent, writes launch.cfg beside them. It deletes nothing and touches no save, game file or other mod. To uninstall, move that
# one folder away. Path logic follows ColossalFramework.IO.DataLocation on Linux.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SELFTEST=0
SELFTEST_QUIT=0
NORMAL=0
AUTOLOAD=""
AUTOLOAD_SET=0
SRC_ARG=""
while [ $# -gt 0 ]; do
  case "$1" in
    --selftest) SELFTEST=1 ;;
    --selftest-quit) SELFTEST_QUIT=1 ;;
    --normal) NORMAL=1 ;;
    --autoload)
      [ $# -ge 2 ] || { echo "--autoload needs a save name"; exit 2; }
      AUTOLOAD="$2"; AUTOLOAD_SET=1; shift ;;
    *) SRC_ARG="$1" ;;
  esac
  shift
done
if [ "${NORMAL}" = 1 ] && [ "${SELFTEST}${SELFTEST_QUIT}${AUTOLOAD_SET}" != 000 ]; then
  echo "--normal cannot be combined with --selftest, --selftest-quit or --autoload"; exit 2
fi
SRC="${SRC_ARG:-${ROOT:?}/cs1/src/MinecraftSkylines.Mod/bin/Release/net35}"
DATA="${XDG_DATA_HOME:-${HOME:?}/.local/share}"
DEST="${DATA:?}/Colossal Order/Cities_Skylines/Addons/Mods/MinecraftSkylines"
CFG="${DEST:?}/launch.cfg"
BACKUP=""
backup_once() {
  [ -n "${BACKUP}" ] && return 0
  BACKUP="${CFG:?}.backup-$(date +%Y%m%d-%H%M%S)-$$"
  cp -p "${CFG:?}" "${BACKUP:?}"
}
# set_key KEY VALUE: append when missing; when any line of KEY has another value, back up once and replace
# every KEY line with one "KEY = VALUE" (awk takes the value from the environment: no escaping problems).
set_key() {
  local key="$1" value="$2" line want cur
  if [ -n "${value}" ]; then want="${key} = ${value}"; else want="${key} ="; fi
  if ! grep -q "^[[:space:]]*${key}[[:space:]]*=" "${CFG:?}"; then
    printf '%s\n' "${want}" >> "${CFG:?}"
    echo "added '${want}' to ${CFG}"
    return 0
  fi
  cur="$(KEY="${key}" awk '{ l=$0; sub(/^[ \t]+/, "", l); k=l; sub(/[ \t]*=.*/, "", k); if (k == ENVIRON["KEY"] && index(l, "=") > 0) { v=substr(l, index(l, "=") + 1); gsub(/^[ \t]+|[ \t\r]+$/, "", v); print v } }' "${CFG:?}")"
  if [ "${cur}" = "${value}" ]; then return 0; fi
  backup_once
  line="$(KEY="${key}" WANT="${want}" awk '{ l=$0; sub(/^[ \t]+/, "", l); k=l; sub(/[ \t]*=.*/, "", k); if (k == ENVIRON["KEY"] && index(l, "=") > 0) { if (!done) print ENVIRON["WANT"]; done=1 } else print }' "${CFG:?}")"
  printf '%s\n' "${line}" > "${CFG:?}"
  echo "set '${want}' in ${CFG}; old file kept as ${BACKUP}"
}

if [ "${NORMAL}" = 1 ]; then
  if [ ! -e "${CFG:?}" ]; then echo "no ${CFG}; nothing to do"; exit 0; fi
  set_key autoload ""
  set_key selftest off
  set_key selftest_quit false
  echo "launch.cfg set for normal play (no autoload, no self-test, no quit): ${CFG}"
  exit 0
fi

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

# Steam runs the game inside its Linux runtime container: the host's /usr (and so /usr/lib/jvm and
# /usr/bin/java) is not visible there, but the home folder is (owner's runs, 2026-10-05: "no java on
# PATH", then "JAVA_HOME is set to an invalid directory: /usr/lib/jvm/java-25-openjdk-amd64").
# So Minecraft is started with a self-contained JDK under the home folder: Temurin 25, fetched by
# tools/fetch-jdk.sh into ~/.cache/minecraft-skylines/jdk/.
JAVA_HOME_DETECTED="$(ls -d "${HOME:?}"/.cache/minecraft-skylines/jdk/jdk-25*/ 2>/dev/null | sort | tail -1 || true)"
JAVA_HOME_DETECTED="${JAVA_HOME_DETECTED%/}"
if [ -z "${JAVA_HOME_DETECTED}" ] || [ ! -x "${JAVA_HOME_DETECTED}/bin/java" ]; then
  echo "ERROR: no JDK under ~/.cache/minecraft-skylines/jdk/. Run: bash ${ROOT}/tools/fetch-jdk.sh"
  exit 1
fi
LAUNCH_PATH="${JAVA_HOME_DETECTED}/bin:/usr/local/bin:/usr/bin:/bin"
if [ -e "${CFG:?}" ]; then
  # Add only keys that are missing; existing lines (the user's edits) are left as they are, with one
  # exception: a JAVA_HOME outside the home folder (written by an earlier version of this script) is
  # invisible inside Steam's container, so those two lines are replaced after a dated backup copy.
  CUR_JH="$(sed -n 's/^[[:space:]]*env\.JAVA_HOME[[:space:]]*=[[:space:]]*//p' "${CFG:?}" | tail -1)"
  if [ -n "${CUR_JH}" ] && [ "${CUR_JH#${HOME}/}" = "${CUR_JH}" ]; then
    backup_once
    grep -vE '^[[:space:]]*env\.(JAVA_HOME|PATH)[[:space:]]*=' "${BACKUP:?}" > "${CFG:?}"
    echo "replaced env.JAVA_HOME ${CUR_JH} (not visible inside Steam's container); old file kept as ${BACKUP}"
  fi
  if ! grep -q '^[[:space:]]*env\.JAVA_HOME[[:space:]]*=' "${CFG:?}"; then
    printf 'env.JAVA_HOME = %s\n' "${JAVA_HOME_DETECTED}" >> "${CFG:?}"
    echo "added env.JAVA_HOME = ${JAVA_HOME_DETECTED} to ${CFG}"
  fi
  if ! grep -q '^[[:space:]]*env\.PATH[[:space:]]*=' "${CFG:?}"; then
    printf 'env.PATH = %s\n' "${LAUNCH_PATH}" >> "${CFG:?}"
    echo "added env.PATH to ${CFG}"
  fi
  echo "launch.cfg kept (missing keys added only): ${CFG}"
else
  cat > "${CFG:?}" <<CFGEOF
# Minecraft auto-start for Ctrl+Shift+M. One key = value per line; # starts a comment.
command = ${ROOT:?}/minecraft/gradlew
args = --no-daemon --console=plain :fabric:runClient -PmcskylinesHidden
working_dir = ${ROOT:?}/minecraft
env.GRADLE_USER_HOME = ${HOME:?}/.cache/gradle-home
env.JAVA_HOME = ${JAVA_HOME_DETECTED}
env.PATH = ${LAUNCH_PATH}
connect_timeout_seconds = 180
# Start Minecraft hidden ahead of time: game_start (main menu), city_load, or off (only on Ctrl+Shift+M).
prewarm = game_start
CFGEOF
  echo "wrote ${CFG}"
fi
if [ "${SELFTEST}" = 1 ]; then
  set_key selftest city_load
  if ! grep -q '^[[:space:]]*args[[:space:]]*=' "${CFG:?}"; then
    echo "WARNING: no args line in ${CFG}; -PmcskylinesDebugCommands not added"
  else
    CUR_ARGS="$(sed -n 's/^[[:space:]]*args[[:space:]]*=//p' "${CFG:?}" | tail -1)"
    case " ${CUR_ARGS} " in
      *[[:space:]]-PmcskylinesDebugCommands[[:space:]]*) ;;
      *)
        backup_once
        # Edited in memory and written back: no temp file is left behind (/tmp is RAM here).
        NEW_CFG="$(sed '/^[[:space:]]*args[[:space:]]*=/s/$/ -PmcskylinesDebugCommands/' "${CFG:?}")"
        printf '%s\n' "${NEW_CFG}" > "${CFG:?}"
        echo "appended -PmcskylinesDebugCommands to args; backup ${BACKUP}"
        ;;
    esac
  fi
fi
if [ "${SELFTEST_QUIT}" = 1 ]; then set_key selftest_quit true; fi
if [ "${AUTOLOAD_SET}" = 1 ]; then set_key autoload "${AUTOLOAD}"; fi
echo "Mod folder: ${DEST}"
echo "Now start Cities: Skylines, open Content Manager > Mods and enable 'Minecraft Skylines'."
