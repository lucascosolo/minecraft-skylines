#!/usr/bin/env bash
# Unattended in-game self-test, for running over SSH while nobody is at the PC:
#
#     bash ~/Workspaces/minecraft-skylines/tools/remote-selftest.sh                       # new game, default map
#     bash ~/Workspaces/minecraft-skylines/tools/remote-selftest.sh --new-game "<map>"
#     bash ~/Workspaces/minecraft-skylines/tools/remote-selftest.sh "<save name>"
#     bash ~/Workspaces/minecraft-skylines/tools/remote-selftest.sh --list-maps | --list-saves
#
# A new game (the default) touches no save: the mod starts "MCSK Self-Test" on a built-in map, builds its own
# test roads and quits without saving. Default map: Green Plains if installed, else the first --list-maps name.
# Installs the mod with launch.cfg "selftest = city_load", "selftest_quit = true", "autoload = new:<map>" (or
# "autoload = <save name>"),
# starts Cities: Skylines through the already running Steam (steam -applaunch 255710), waits for the
# self-test report (the mod quits the game itself, without saving), copies the evidence to
# ~/.cache/minecraft-skylines/evidence/selftest-<UTC>/ and finally sets launch.cfg back to normal play
# (install-cs1-mod.sh --normal). Never kills a process and never deletes anything.
#
# Exit codes: 0 report found; 1 refused (game already running) or install failed; 2 game did not start;
# 3 timeout (no report in time, or the game exited without one); 4 bad usage.
# Launch: MCSK_LAUNCH=direct (default, starts the game binary) or steam (via steam -applaunch;
# opens the Paradox Launcher, which needs a click). MCSK_GAME_DIR overrides the install path.
# Test overrides: MCSK_STEAM_CMD, MCSK_POLL_SECONDS (10), MCSK_TIMEOUT_SECONDS (2400), MCSK_START_SECONDS (180),
# MCSK_EXIT_WAIT_SECONDS (120), MCSK_GAME_PROCESS (Cities.x64), MCSK_MOD_SRC (build output to install),
# MCSK_CS1_INSTALL (game folder; default: cs1_install in ~/.cache/minecraft-skylines/refs/environment.txt).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INSTALL="${ROOT:?}/tools/install-cs1-mod.sh"
DATA="${XDG_DATA_HOME:-${HOME:?}/.local/share}"
APPDATA="${DATA:?}/Colossal Order/Cities_Skylines"
MODLOGS="${APPDATA:?}/ModLogs"
PLAYER_LOG="${XDG_CONFIG_HOME:-${HOME:?}/.config}/unity3d/Colossal Order/Cities_ Skylines/Player.log"
MC_LOG="${ROOT:?}/minecraft/fabric/run/logs/latest.log"
GAME="${MCSK_GAME_PROCESS:-Cities.x64}"
POLL="${MCSK_POLL_SECONDS:-10}"
TIMEOUT="${MCSK_TIMEOUT_SECONDS:-2400}"
START_WINDOW="${MCSK_START_SECONDS:-90}"
EXIT_WAIT="${MCSK_EXIT_WAIT_SECONDS:-120}"

DEFAULT_MAP="Green Plains"

usage() {
  echo "usage: $(basename "$0") [--new-game \"<map>\" | \"<save name>\"] | --list-maps | --list-saves | --help"
  echo "  no argument: --new-game on the default map (${DEFAULT_MAP} if installed, else the first --list-maps name)."
  echo "  --new-game <map>: a new city on that map (file, asset or display name); no save is read or written."
  echo "  <save name>: as the Load panel lists it (see --list-saves). Steam must already be running on the desktop."
}

maps_dir() {
  local install="${MCSK_CS1_INSTALL:-}" envfile="${HOME:?}/.cache/minecraft-skylines/refs/environment.txt"
  if [ -z "${install}" ] && [ -f "${envfile}" ]; then install="$(sed -n 's/^cs1_install:[[:space:]]*//p' "${envfile}" | head -1)"; fi
  [ -n "${install}" ] || install="${HOME:?}/.steam/debian-installation/steamapps/common/Cities_Skylines"
  echo "${install}/Files/Maps"
}

# Built-in maps: every .crp under <game>/Files/Maps, subfolders included (PackageManager.LoadPackages recurses).
installed_maps() {
  local dir; dir="$(maps_dir)"
  [ -d "${dir}" ] || return 0
  find "${dir}" -type f -name '*.crp' -printf '%f\n' | sed 's/\.crp$//' | LC_ALL=C sort -u
}

list_maps() {
  local dir; dir="$(maps_dir)"
  if [ ! -d "${dir}" ]; then
    echo "no built-in maps folder at ${dir}; the default map is \"${DEFAULT_MAP}\". If a name does not match, the mod log lists every map it can see (grep 'autoload: no map' in ModLogs/MinecraftSkylines.log)." >&2
    return 0
  fi
  installed_maps
}

default_map() {
  local maps pick
  maps="$(installed_maps)"
  pick="$(printf '%s\n' "${maps}" | awk -v w="${DEFAULT_MAP}" 'tolower($0) == tolower(w) { print; exit }')"
  [ -n "${pick}" ] || pick="$(printf '%s\n' "${maps}" | sed -n '1p')"
  echo "${pick:-${DEFAULT_MAP}}"
}

list_saves() {
  local dir="${APPDATA:?}/Saves"
  if [ ! -d "${dir}" ]; then echo "no saves folder at ${dir}" >&2; return 0; fi
  find "${dir}" -maxdepth 1 -type f -name '*.crp' -printf '%T@\t%f\n' | sort -rn | cut -f2- | sed 's/\.crp$//'
}

NAME=""
NAMES=0
MAP=""
MAPS=0
while [ $# -gt 0 ]; do
  case "$1" in
    --help|-h) usage; exit 0 ;;
    --list-saves) list_saves; exit 0 ;;
    --list-maps) list_maps; exit 0 ;;
    --new-game)
      [ $# -ge 2 ] || { echo "--new-game needs a map name" >&2; usage >&2; exit 4; }
      MAP="$2"; MAPS=$((MAPS + 1)); shift ;;
    -*) echo "unknown option: $1" >&2; usage >&2; exit 4 ;;
    *) NAME="$1"; NAMES=$((NAMES + 1)) ;;
  esac
  shift
done
if [ "${MAPS}" -gt 1 ] || [ "${NAMES}" -gt 1 ] || [ $((MAPS + NAMES)) -gt 1 ]; then usage >&2; exit 4; fi
if [ "${MAPS}" = 1 ] && [ -z "${MAP//[[:space:]]/}" ]; then echo "--new-game needs a map name" >&2; exit 4; fi
if [ "${NAMES}" = 1 ] && [ -z "${NAME//[[:space:]]/}" ]; then usage >&2; exit 4; fi
if [ "${NAMES}" = 0 ]; then
  [ "${MAPS}" = 1 ] || MAP="$(default_map)"
  AUTOLOAD="new:${MAP}"
else
  AUTOLOAD="${NAME}"
fi

if pgrep -x "${GAME}" >/dev/null; then
  echo "Cities: Skylines (${GAME}) is already running; quit it first. Nothing was changed." >&2
  exit 1
fi

if [ -n "${MCSK_STEAM_CMD:-}" ]; then
  read -r -a STEAM <<< "${MCSK_STEAM_CMD}"
elif command -v steam >/dev/null; then
  STEAM=(steam)
elif [ -x /usr/games/steam ]; then
  STEAM=(/usr/games/steam)
else
  echo "steam not found on PATH or at /usr/games/steam" >&2
  exit 2
fi
# Without a running client "steam -applaunch" would start a new Steam in this SSH session and block.
if [ -z "${MCSK_STEAM_CMD:-}" ] && ! pgrep -x steam >/dev/null; then
  echo "Steam is not running; it must already be running (logged in) in the desktop session on the home display." >&2
  exit 2
fi

SRC_ARGS=()
[ -n "${MCSK_MOD_SRC:-}" ] && SRC_ARGS=("${MCSK_MOD_SRC}")
echo "== installing the mod with selftest = city_load, selftest_quit = true, autoload = ${AUTOLOAD}"
bash "${INSTALL:?}" --selftest --selftest-quit --autoload "${AUTOLOAD}" "${SRC_ARGS[@]}" || { echo "install failed" >&2; exit 1; }

# Copies every log the agent needs into a folder it can read. Runs on every exit after the game was
# asked to start (success, timeout, game ended without a report), not only on success.
EVIDENCE=""
copy_evidence() {
  [ -n "${EVIDENCE}" ] && return 0
  EVIDENCE="${HOME:?}/.cache/minecraft-skylines/evidence/selftest-$(date -u +%Y%m%dT%H%M%SZ)"
  mkdir -p "${EVIDENCE:?}"
  [ -d "${MODLOGS:?}" ] && cp -r "${MODLOGS:?}" "${EVIDENCE:?}/ModLogs" 2>/dev/null || true
  [ -f "${PLAYER_LOG}" ] && cp "${PLAYER_LOG}" "${EVIDENCE:?}/" 2>/dev/null || true
  [ -f "${MC_LOG}" ] && cp "${MC_LOG}" "${EVIDENCE:?}/minecraft-latest.log" 2>/dev/null || true
  echo "Evidence: ${EVIDENCE}" >&2
}
LAUNCHED=0

restore() {
  local rc=$?
  [ "${LAUNCHED}" = 1 ] && copy_evidence
  echo "== setting launch.cfg back to normal play (autoload off, selftest off, selftest_quit false)"
  bash "${INSTALL:?}" --normal || echo "WARNING: could not reset launch.cfg; run: bash ${INSTALL} --normal" >&2
  exit "${rc}"
}
trap restore EXIT

# Over SSH this shell has none of the desktop session's display/session variables, and the steam
# command may then fail to hand the launch to the running client (owner's first SSH run, 2026-10-05:
# the game never started). Borrow them from the running Steam process (same user, /proc is readable).
borrow_session_env() {
  [ -n "${MCSK_STEAM_CMD:-}" ] && return 0
  local pid
  pid="$(pgrep -x steam | head -1 || true)"
  [ -n "${pid}" ] && [ -r "/proc/${pid}/environ" ] || return 0
  local var val
  for var in DISPLAY WAYLAND_DISPLAY XDG_RUNTIME_DIR DBUS_SESSION_BUS_ADDRESS XAUTHORITY XDG_SESSION_TYPE XDG_CURRENT_DESKTOP; do
    if [ -z "${!var:-}" ]; then
      val="$(tr '\0' '\n' < "/proc/${pid}/environ" | sed -n "s/^${var}=//p" | head -1)"
      if [ -n "${val}" ]; then
        export "${var}=${val}"
        echo "   using ${var}=${val} from the running Steam (pid ${pid})"
      fi
    fi
  done
}

# Everything needed to diagnose a launch that never happened, written where the agent can read it.
write_diagnostics() {
  local dir log
  dir="${HOME:?}/.cache/minecraft-skylines/evidence/remote-diag-$(date -u +%Y%m%dT%H%M%SZ)"
  mkdir -p "${dir:?}"
  {
    echo "date_utc: $(date -u +%FT%TZ)"
    echo "steam_cmd: ${STEAM[*]}"
    for var in DISPLAY WAYLAND_DISPLAY XDG_RUNTIME_DIR DBUS_SESSION_BUS_ADDRESS XAUTHORITY XDG_SESSION_TYPE; do
      echo "${var}=${!var:-}"
    done
    echo "--- processes matching cities|steam"
    pgrep -a -f -i 'cities|steam' | cut -c1-300 | head -40 || true
  } > "${dir:?}/diag.txt"
  for log in "${HOME}/.steam/debian-installation/logs/console_log.txt" "${HOME}/.steam/steam/logs/console_log.txt" \
             "${HOME}/.local/share/Steam/logs/console_log.txt"; do
    if [ -f "${log}" ]; then
      { echo "--- tail of ${log}"; tail -60 "${log}"; } >> "${dir:?}/diag.txt"
      break
    fi
  done
  [ -f "${MODLOG:-}" ] && cp "${MODLOG}" "${dir:?}/" 2>/dev/null || true
  echo "diagnostics written to ${dir}" >&2
}

# Copies of the logs the agent cannot read directly (they live outside its sandbox), refreshed every
# poll so a run can be diagnosed while it is still going. Only our own copies are overwritten.
LIVE="${HOME:?}/.cache/minecraft-skylines/evidence/live"
mirror_live() {
  mkdir -p "${LIVE:?}"
  local f
  for f in "${MODLOGS:?}/MinecraftSkylines.log" "${MODLOGS:?}/MinecraftSkylines-companion.log" \
           "${HOME:?}/.config/unity3d/Colossal Order/Cities_ Skylines/Player.log"; do
    [ -f "${f}" ] && tail -c 400000 "${f}" > "${LIVE:?}/$(basename "${f}")" 2>/dev/null || true
  done
  date -u +%FT%TZ > "${LIVE:?}/updated_utc.txt"
}

MODLOG="${MODLOGS:?}/MinecraftSkylines.log"
LOG_START=0
[ -f "${MODLOG}" ] && LOG_START="$(stat -c %s "${MODLOG}")"
START="$(date +%s)"
borrow_session_env
# The game and Minecraft together need several GB of RAM and most of a 6 GB GPU; another heavy job
# (e.g. a local image model) made the owner's remote run crawl at ~1 fps (2026-10-05). Warn, don't block.
resources_check() {
  local avail_kb gpu
  avail_kb="$(sed -n 's/^MemAvailable:[[:space:]]*\([0-9]*\) kB/\1/p' /proc/meminfo 2>/dev/null || true)"
  [ -n "${avail_kb}" ] && echo "   RAM available: $((avail_kb / 1024 / 1024)) GB"
  if [ -n "${avail_kb}" ] && [ "${avail_kb}" -lt $((10 * 1024 * 1024)) ]; then
    echo "WARNING: less than 10 GB RAM available; the game may crawl (close other heavy programs)" >&2
  fi
  if command -v nvidia-smi >/dev/null 2>&1; then
    gpu="$(nvidia-smi --query-gpu=memory.used,memory.total,utilization.gpu --format=csv,noheader,nounits 2>/dev/null | head -1 || true)"
    if [ -n "${gpu}" ]; then
      echo "   GPU memory used/total (MiB), load (%): ${gpu}"
      local used total
      used="$(echo "${gpu}" | cut -d, -f1 | tr -d ' ')"; total="$(echo "${gpu}" | cut -d, -f2 | tr -d ' ')"
      if [ -n "${used}" ] && [ -n "${total}" ] && [ "${total}" -gt 0 ] && [ $((used * 100 / total)) -ge 50 ]; then
        echo "WARNING: the GPU is already ${used}/${total} MiB busy; the game may crawl (pause other GPU jobs)" >&2
        nvidia-smi --query-compute-apps=pid,process_name,used_memory --format=csv,noheader 2>/dev/null | head -5 | sed 's/^/   GPU user: /' >&2 || true
      fi
    fi
  fi
}
resources_check
# Launching through Steam opens the Paradox Launcher, which waits for a click on "Play" (owner's SSH
# run, 2026-10-05). For unattended runs start the game binary directly instead; SteamAppId makes it
# attach to the running Steam client without a restart through Steam. MCSK_LAUNCH=steam keeps the old way.
LAUNCH="${MCSK_LAUNCH:-direct}"
RETRIED=0
LAUNCHED=1
if [ "${LAUNCH}" = direct ]; then
  RETRIED=1
  GAME_DIR="${MCSK_GAME_DIR:-$(sed -n 's/^cs1_install: //p' "${HOME:?}/.cache/minecraft-skylines/refs/environment.txt" 2>/dev/null | head -1)}"
  GAME_DIR="${GAME_DIR:-${HOME:?}/.steam/debian-installation/steamapps/common/Cities_Skylines}"
  if [ ! -x "${GAME_DIR:?}/${GAME}" ]; then
    echo "game binary not found: ${GAME_DIR}/${GAME} (set MCSK_GAME_DIR)" >&2
    write_diagnostics
    exit 2
  fi
  GAME_OUT="${HOME:?}/.cache/minecraft-skylines/evidence/game-stdout-$(date -u +%Y%m%dT%H%M%SZ).log"
  mkdir -p "$(dirname "${GAME_OUT:?}")"
  echo "== $(date -u +%FT%TZ) starting the game directly: ${GAME_DIR}/${GAME} (no Paradox Launcher; output in ${GAME_OUT})"
  # setsid: the game must survive this SSH session ending.
  (cd "${GAME_DIR:?}" && SteamAppId=255710 SteamGameId=255710 setsid -f "./${GAME}" > "${GAME_OUT:?}" 2>&1 < /dev/null) \
    || { echo "could not start ${GAME}" >&2; write_diagnostics; exit 2; }
else
  echo "== $(date -u +%FT%TZ) starting the game: ${STEAM[*]} -applaunch 255710 (Steam must already be running in the desktop session)"
  "${STEAM[@]}" -applaunch 255710 >/dev/null 2>&1 || { echo "steam command failed" >&2; write_diagnostics; exit 2; }
fi

new_report() {
  [ -d "${MODLOGS:?}/selftest" ] || return 0
  find "${MODLOGS:?}/selftest" -mindepth 2 -maxdepth 2 -name report.json -newermt "@${START}" -printf '%T@\t%p\n' | sort -rn | head -1 | cut -f2-
}
new_log() {
  [ -f "${MODLOG}" ] && tail -c +"$((LOG_START + 1))" "${MODLOG}" || true
}

REPORT=""
SEEN=0
while :; do
  REPORT="$(new_report)"
  [ -n "${REPORT}" ] && break
  ELAPSED=$(( $(date +%s) - START ))
  if pgrep -x "${GAME}" >/dev/null; then UP=yes; SEEN=1; else UP=no; fi
  LINK="$(new_log | grep -i 'link state connected\|connected to ' >/dev/null && echo connected || echo "not connected")"
  STATUS="$({ new_log | grep -i 'self-test:\|autoload:\|unattended:' || true; } | tail -1 | sed -E 's/^[0-9:.]+ [A-Z]+ +//')"
  echo "[${ELAPSED}s] game ${UP}; link ${LINK}; ${STATUS:-no self-test line yet}"
  mirror_live
  if [ "${SEEN}" = 0 ] && [ "${RETRIED}" = 0 ] && [ "${ELAPSED}" -ge $(( START_WINDOW / 3 )) ]; then
    RETRIED=1
    echo "   no game yet; asking Steam again through steam://rungameid/255710"
    "${STEAM[@]}" "steam://rungameid/255710" >/dev/null 2>&1 || true
  fi
  if [ "${SEEN}" = 0 ] && [ "${ELAPSED}" -ge "${START_WINDOW}" ]; then
    echo "the game did not start within ${START_WINDOW} s (is Steam running in the desktop session?)" >&2
    write_diagnostics
    exit 2
  fi
  if [ "${SEEN}" = 1 ] && [ "${UP}" = no ]; then
    sleep 2
    REPORT="$(new_report)"
    [ -n "${REPORT}" ] && break
    echo "the game exited without writing a self-test report" >&2
    exit 3
  fi
  if [ "${ELAPSED}" -ge "${TIMEOUT}" ]; then
    echo "no self-test report after ${TIMEOUT} s; the game is left running (nothing is killed)" >&2
    exit 3
  fi
  sleep "${POLL}"
done
echo "== report: ${REPORT}"

WAITED=0
while pgrep -x "${GAME}" >/dev/null && [ "${WAITED}" -lt "${EXIT_WAIT}" ]; do
  sleep "${POLL}"
  WAITED=$((WAITED + POLL))
done
pgrep -x "${GAME}" >/dev/null && echo "WARNING: the game is still running after ${EXIT_WAIT} s; copying evidence anyway" >&2

copy_evidence
python3 -c '
import json, sys
r = json.load(open(sys.argv[1]))
s = r.get("summary", {})
line = "Self-test: %d pass, %d fail, %d skip, %d error" % (s.get("pass", 0), s.get("fail", 0), s.get("skip", 0), s.get("error", 0))
if r.get("aborted"):
    line += " (aborted: %s)" % r.get("abort_reason")
print(line)
' "${REPORT}"
echo "Evidence: ${EVIDENCE}"
