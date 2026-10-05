#!/usr/bin/env bash
# Copy the built Cities: Skylines mod into the game's local mods folder. Run it yourself in a normal
# terminal (the agent's sandbox cannot write there):
#
#     bash ~/Workspaces/minecraft-skylines/tools/install-cs1-mod.sh [--selftest] [build-output-dir]
#
# --selftest (any position): also ensures launch.cfg has "selftest = city_load" and that the args
# line carries -PmcskylinesDebugCommands (dated backup copy before any edit of an existing line).
# Without an argument it installs the repo's current Release build. With one, it installs that
# folder instead (e.g. a pinned build of a specific commit).
# Copies four DLLs into Addons/Mods/MinecraftSkylines/ (overwriting older copies of the same four
# files) and, if absent, writes launch.cfg beside them. It deletes nothing and touches no save, game file or other mod. To uninstall, move that
# one folder away. Path logic follows ColossalFramework.IO.DataLocation on Linux.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SELFTEST=0
SRC_ARG=""
for a in "$@"; do
  if [ "$a" = "--selftest" ]; then SELFTEST=1; else SRC_ARG="$a"; fi
done
SRC="${SRC_ARG:-${ROOT:?}/cs1/src/MinecraftSkylines.Mod/bin/Release/net35}"
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
    BACKUP="${CFG:?}.backup-$(date +%Y%m%d-%H%M%S)-$$"
    cp -p "${CFG:?}" "${BACKUP:?}"
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
  BACKUP="${BACKUP:-}"
  backup_once() {
    [ -n "${BACKUP}" ] && return 0
    BACKUP="${CFG:?}.backup-$(date +%Y%m%d-%H%M%S)-$$"
    cp -p "${CFG:?}" "${BACKUP:?}"
  }
  SELFTMP="$(mktemp)"
  CUR_ST="$(sed -n 's/^[[:space:]]*selftest[[:space:]]*=[[:space:]]*//p' "${CFG:?}" | tail -1)"
  if ! grep -q '^[[:space:]]*selftest[[:space:]]*=' "${CFG:?}"; then
    printf 'selftest = city_load\n' >> "${CFG:?}"
    echo "added selftest = city_load to ${CFG}"
  elif [ "${CUR_ST}" != "city_load" ]; then
    backup_once
    sed 's/^[[:space:]]*selftest[[:space:]]*=.*/selftest = city_load/' "${CFG:?}" > "${SELFTMP:?}"
    cat "${SELFTMP:?}" > "${CFG:?}"
    echo "replaced selftest = ${CUR_ST} with city_load; old file kept as ${BACKUP}"
  fi
  if ! grep -q '^[[:space:]]*args[[:space:]]*=' "${CFG:?}"; then
    echo "WARNING: no args line in ${CFG}; -PmcskylinesDebugCommands not added"
  else
    CUR_ARGS="$(sed -n 's/^[[:space:]]*args[[:space:]]*=//p' "${CFG:?}" | tail -1)"
    case " ${CUR_ARGS} " in
      *[[:space:]]-PmcskylinesDebugCommands[[:space:]]*) ;;
      *)
        backup_once
        sed '/^[[:space:]]*args[[:space:]]*=/s/$/ -PmcskylinesDebugCommands/' "${CFG:?}" > "${SELFTMP:?}"
        cat "${SELFTMP:?}" > "${CFG:?}"
        echo "appended -PmcskylinesDebugCommands to args; backup ${BACKUP}"
        ;;
    esac
  fi
fi
echo "Mod folder: ${DEST}"
echo "Now start Cities: Skylines, open Content Manager > Mods and enable 'Minecraft Skylines'."
