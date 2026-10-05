#!/usr/bin/env bash
# Unattended in-game self-test, for running over SSH while nobody is at the PC:
#
#     bash ~/Workspaces/minecraft-skylines/tools/remote-selftest.sh "<save name>"
#     bash ~/Workspaces/minecraft-skylines/tools/remote-selftest.sh --list-saves
#
# Installs the mod with launch.cfg "selftest = city_load", "selftest_quit = true", "autoload = <save name>",
# starts Cities: Skylines through the already running Steam (steam -applaunch 255710), waits for the
# self-test report (the mod quits the game itself, without saving), copies the evidence to
# ~/.cache/minecraft-skylines/evidence/selftest-<UTC>/ and finally sets launch.cfg back to normal play
# (install-cs1-mod.sh --normal). Never kills a process and never deletes anything.
#
# Exit codes: 0 report found; 1 refused (game already running) or install failed; 2 game did not start;
# 3 timeout (no report in time, or the game exited without one); 4 bad usage.
# Test overrides: MCSK_STEAM_CMD, MCSK_POLL_SECONDS (10), MCSK_TIMEOUT_SECONDS (1200), MCSK_START_SECONDS (180),
# MCSK_EXIT_WAIT_SECONDS (120), MCSK_GAME_PROCESS (Cities.x64), MCSK_MOD_SRC (build output to install).
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
TIMEOUT="${MCSK_TIMEOUT_SECONDS:-1200}"
START_WINDOW="${MCSK_START_SECONDS:-180}"
EXIT_WAIT="${MCSK_EXIT_WAIT_SECONDS:-120}"

usage() {
  echo "usage: $(basename "$0") \"<save name>\" | --list-saves | --help"
  echo "  <save name>: as the Load panel lists it (see --list-saves); Steam must already be running on the desktop."
}

list_saves() {
  local dir="${APPDATA:?}/Saves"
  if [ ! -d "${dir}" ]; then echo "no saves folder at ${dir}" >&2; return 0; fi
  find "${dir}" -maxdepth 1 -type f -name '*.crp' -printf '%T@\t%f\n' | sort -rn | cut -f2- | sed 's/\.crp$//'
}

NAME=""
NAMES=0
for a in "$@"; do
  case "$a" in
    --help|-h) usage; exit 0 ;;
    --list-saves) list_saves; exit 0 ;;
    -*) echo "unknown option: $a" >&2; usage >&2; exit 4 ;;
    *) NAME="$a"; NAMES=$((NAMES + 1)) ;;
  esac
done
if [ "${NAMES}" -ne 1 ] || [ -z "${NAME//[[:space:]]/}" ]; then usage >&2; exit 4; fi

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
echo "== installing the mod with selftest = city_load, selftest_quit = true, autoload = ${NAME}"
bash "${INSTALL:?}" --selftest --selftest-quit --autoload "${NAME}" "${SRC_ARGS[@]}" || { echo "install failed" >&2; exit 1; }

restore() {
  local rc=$?
  echo "== setting launch.cfg back to normal play (autoload off, selftest off, selftest_quit false)"
  bash "${INSTALL:?}" --normal || echo "WARNING: could not reset launch.cfg; run: bash ${INSTALL} --normal" >&2
  exit "${rc}"
}
trap restore EXIT

MODLOG="${MODLOGS:?}/MinecraftSkylines.log"
LOG_START=0
[ -f "${MODLOG}" ] && LOG_START="$(stat -c %s "${MODLOG}")"
START="$(date +%s)"
echo "== $(date -u +%FT%TZ) starting the game: ${STEAM[*]} -applaunch 255710 (Steam must already be running in the desktop session)"
"${STEAM[@]}" -applaunch 255710 >/dev/null 2>&1 || { echo "steam command failed" >&2; exit 2; }

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
  if [ "${SEEN}" = 0 ] && [ "${ELAPSED}" -ge "${START_WINDOW}" ]; then
    echo "the game did not start within ${START_WINDOW} s (is Steam running in the desktop session?)" >&2
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

EVIDENCE="${HOME:?}/.cache/minecraft-skylines/evidence/selftest-$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "${EVIDENCE:?}"
cp -r "${MODLOGS:?}" "${EVIDENCE:?}/ModLogs"
[ -f "${PLAYER_LOG}" ] && cp "${PLAYER_LOG}" "${EVIDENCE:?}/"
[ -f "${MC_LOG}" ] && cp "${MC_LOG}" "${EVIDENCE:?}/minecraft-latest.log"
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
