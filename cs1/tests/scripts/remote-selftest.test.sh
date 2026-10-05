#!/usr/bin/env bash
# Tests for tools/install-cs1-mod.sh (autoload/selftest-quit/normal) and tools/remote-selftest.sh.
# Scratch dirs only (mktemp under $TMPDIR); nothing is deleted; the real HOME is never touched.
set -u
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
INSTALL="${REPO:?}/tools/install-cs1-mod.sh"
REMOTE="${REPO:?}/tools/remote-selftest.sh"
FAILS=0
ok()   { echo "PASS $1"; }
bad()  { echo "FAIL $1${2:+: $2}"; FAILS=$((FAILS + 1)); }
check() { # name, condition-exit-status, detail
  if [ "$2" = 0 ]; then ok "$1"; else bad "$1" "${3:-}"; fi
}

mk_env() { # sets S H D CFG MODDIR BUILD SAVES LOGS
  S="$(mktemp -d "${TMPDIR:?}/mcsk-test.XXXXXX")"
  H="${S:?}/home"; D="${S:?}/data"
  MODDIR="${D:?}/Colossal Order/Cities_Skylines/Addons/Mods/MinecraftSkylines"
  SAVES="${D:?}/Colossal Order/Cities_Skylines/Saves"
  LOGS="${D:?}/Colossal Order/Cities_Skylines/ModLogs/selftest"
  CFG="${MODDIR:?}/launch.cfg"
  BUILD="${S:?}/build"
  mkdir -p "${H:?}/.cache/minecraft-skylines/jdk/jdk-25-fake/bin" "${S:?}/config" "${BUILD:?}" "${MODDIR:?}"
  printf '#!/bin/sh\nexit 0\n' > "${H:?}/.cache/minecraft-skylines/jdk/jdk-25-fake/bin/java"
  chmod +x "${H:?}/.cache/minecraft-skylines/jdk/jdk-25-fake/bin/java"
  for f in MinecraftSkylines.dll Skylines.Host.dll Skylines.Bridge.dll MinecraftSkylines.Protocol.dll; do : > "${BUILD:?}/$f"; done
}
write_cfg() {
  cat > "${CFG:?}" <<CF
command = /bin/true
args = --no-daemon :fabric:runClient
selftest = off
prewarm = game_start
CF
}
envrun() { env HOME="${H:?}" XDG_DATA_HOME="${D:?}" XDG_CONFIG_HOME="${S:?}/config" "$@"; }
inst() { envrun timeout 30 bash "${INSTALL:?}" "$@" >"${S:?}/out.txt" 2>&1; }
backups() { ls "${MODDIR:?}"/launch.cfg.backup-* 2>/dev/null | wc -l; }
cfg_has() { grep -qxF -- "$1" "${CFG:?}"; }
cfg_reset() { cfg_has 'autoload =' && cfg_has 'selftest = off' && cfg_has 'selftest_quit = false'; }

# 1. syntax
for f in "${INSTALL:?}" "${REMOTE:?}"; do
  bash -n "$f" 2>/dev/null; check "1 bash -n $(basename "$f")" $?
done

# 2. install options + single backup, idempotent
mk_env; write_cfg
inst --autoload "My City" --selftest-quit --selftest "${BUILD:?}"; rc=$?
check "2a install exit 0" "$rc" "rc=$rc"
cfg_has 'autoload = My City' && cfg_has 'selftest_quit = true' && cfg_has 'selftest = city_load'
check "2b install lines present" $?
n="$(backups)"; [ "$n" = 1 ]; check "2c exactly one backup" $? "n=$n"
inst --autoload "My City" --selftest-quit --selftest "${BUILD:?}"
n2="$(backups)"; [ "$n2" = 1 ]; check "2d no new backup on identical run" $? "n=$n2"

# 2e. differing value replaces every line of the key
mk_env; write_cfg; printf 'autoload = A\nautoload = B\n' >> "${CFG:?}"
inst --autoload "C" "${BUILD:?}"
[ "$(grep -c '^autoload' "${CFG:?}")" = 1 ] && cfg_has 'autoload = C'
check "2e duplicate keys replaced by one line" $?

# 3. no new options
mk_env; inst "${BUILD:?}"; rc=$?
[ "$rc" = 0 ] && [ -f "${CFG:?}" ] && ! grep -qE '^(autoload|selftest_quit)' "${CFG:?}"
check "3 plain run writes no autoload/selftest_quit" $? "rc=$rc"

# 4. --normal
mk_env; write_cfg; printf 'autoload = X\nselftest = city_load\nselftest_quit = true\n' >> "${CFG:?}"
inst --normal; rc=$?
check "4a --normal exit 0" "$rc" "rc=$rc"
cfg_reset && ! grep -q '^autoload = X' "${CFG:?}" && grep -q '^prewarm = game_start' "${CFG:?}" && grep -q '^args = ' "${CFG:?}"
check "4b --normal resets keys, keeps others" $?
[ -z "$(ls "${MODDIR:?}"/*.dll 2>/dev/null)" ]; check "4c --normal copies no DLLs" $?
inst --normal --selftest; rc=$?; [ "$rc" = 2 ]; check "4d --normal --selftest exit 2" $? "rc=$rc"
inst --normal --selftest-quit; rc=$?; [ "$rc" = 2 ]; check "4e --normal --selftest-quit exit 2" $? "rc=$rc"
inst --normal --autoload X; rc=$?; [ "$rc" = 2 ]; check "4f --normal --autoload exit 2" $? "rc=$rc"
inst "${BUILD:?}" --autoload; rc=$?; [ "$rc" = 2 ]; check "4g --autoload without value exit 2" $? "rc=$rc"
mk_env; inst --normal; rc=$?
[ "$rc" = 0 ] && [ ! -e "${CFG:?}" ]; check "4h --normal without launch.cfg: exit 0, nothing created" $? "rc=$rc"

# 5. remote usage
mk_env
rrun() { envrun timeout 60 bash "${REMOTE:?}" "$@" >"${S:?}/rout.txt" 2>"${S:?}/rerr.txt"; }
# 5a (no args = new game on default map) is an end-to-end check, see section 11
rrun ""; rc=$?; [ "$rc" = 4 ]; check "5b empty name exit 4" $? "rc=$rc"
rrun "   "; rc=$?; [ "$rc" = 4 ]; check "5c blank name exit 4" $? "rc=$rc"
rrun --bogus; rc=$?; [ "$rc" = 4 ]; check "5d unknown option exit 4" $? "rc=$rc"
rrun one two; rc=$?; [ "$rc" = 4 ]; check "5e two positionals exit 4" $? "rc=$rc"
rrun --help; rc=$?; [ "$rc" = 0 ]; check "5f --help exit 0" $? "rc=$rc"

# 6. --list-saves
mk_env
rrun --list-saves; rc=$?
[ "$rc" = 0 ] && [ ! -s "${S:?}/rout.txt" ]; check "6a missing Saves folder: empty, exit 0" $? "rc=$rc"
mkdir -p "${SAVES:?}"
: > "${SAVES:?}/Old City.crp"; : > "${SAVES:?}/Mid.crp"; : > "${SAVES:?}/New.crp"; : > "${SAVES:?}/notes.txt"
touch -d '3 days ago' "${SAVES:?}/Old City.crp"; touch -d '2 days ago' "${SAVES:?}/Mid.crp"
touch -d '1 hour ago' "${SAVES:?}/New.crp"; touch -d '1 minute ago' "${SAVES:?}/notes.txt"
rrun --list-saves; rc=$?
[ "$rc" = 0 ] && [ "$(cat "${S:?}/rout.txt")" = "$(printf 'New\nMid\nOld City')" ]
check "6b newest first, .crp stripped, non-.crp ignored" $? "got: $(tr '\n' '|' < "${S:?}/rout.txt")"

# fake game process: a copy of bash named $GN blocking on a fifo read (no child processes)
fake_game_prep() { cp /bin/bash "${S:?}/${GN:?}"; mkfifo "${S:?}/fifo"; }

# helpers for end-to-end cases
rrun_env() { # extra env pairs via "$@" before the name; name last
  envrun MCSK_MOD_SRC="${BUILD:?}" MCSK_POLL_SECONDS=1 "$@" >"${S:?}/rout.txt" 2>"${S:?}/rerr.txt"
}
remote_e2e() { envrun MCSK_MOD_SRC="${BUILD:?}" MCSK_POLL_SECONDS=1 "$@" timeout 60 bash "${REMOTE:?}" "${NAME:-My City}" >"${S:?}/rout.txt" 2>"${S:?}/rerr.txt"; }

# 7. end to end success
mk_env; write_cfg
RUN="${LOGS:?}/20260101T000000Z"
cat > "${S:?}/steam.sh" <<SH
#!/bin/sh
cp "${CFG:?}" "${S:?}/cfg-at-launch.txt"
sleep 1
mkdir -p "${RUN:?}"
echo '{"summary":{"pass":3,"fail":1,"skip":2,"error":0}}' > "${RUN:?}/report.json"
SH
chmod +x "${S:?}/steam.sh"
remote_e2e MCSK_STEAM_CMD="${S:?}/steam.sh" MCSK_GAME_PROCESS="mcsk-nogame-$$" MCSK_START_SECONDS=20 MCSK_TIMEOUT_SECONDS=20; rc=$?
check "7a exit 0" "$rc" "rc=$rc; $(head -c 300 "${S:?}/rerr.txt")"
grep -qxF 'Self-test: 3 pass, 1 fail, 2 skip, 0 error' "${S:?}/rout.txt"; check "7b summary line" $?
ev="$(sed -n 's/^Evidence: //p' "${S:?}/rout.txt" | head -1)"
[ -n "$ev" ] && [ -f "${ev}/ModLogs/selftest/20260101T000000Z/report.json" ] && case "$ev" in "${H:?}/.cache/minecraft-skylines/evidence/selftest-"*) true;; *) false;; esac
check "7c Evidence dir holds report.json" $? "ev=$ev"
grep -qxF 'autoload = My City' "${S:?}/cfg-at-launch.txt" && grep -qxF 'selftest = city_load' "${S:?}/cfg-at-launch.txt" && grep -qxF 'selftest_quit = true' "${S:?}/cfg-at-launch.txt"
check "7d launch.cfg set at launch time" $?
cfg_reset; check "7e launch.cfg reset afterwards" $?

# 8. no game, no report: exit 2, old report ignored
mk_env; write_cfg
mkdir -p "${LOGS:?}/20200101T000000Z"
echo '{"summary":{"pass":9,"fail":0,"skip":0,"error":0}}' > "${LOGS:?}/20200101T000000Z/report.json"
touch -d '1 hour ago' "${LOGS:?}/20200101T000000Z/report.json"
remote_e2e MCSK_STEAM_CMD=true MCSK_GAME_PROCESS="mcsk-nogame-$$" MCSK_START_SECONDS=2 MCSK_TIMEOUT_SECONDS=20; rc=$?
[ "$rc" = 2 ]; check "8a exit 2, old report ignored" $? "rc=$rc"
cfg_reset; check "8b launch.cfg reset" $?

# 9. refuses when game already running
mk_env; write_cfg
GN="mcsk-f$$"; fake_game_prep
"${S:?}/${GN:?}" -c 'read -t 30 -u 3 x' 3<>"${S:?}/fifo" >/dev/null 2>&1 & GP=$!
sleep 1
cp "${CFG:?}" "${S:?}/cfg-before.txt"
remote_e2e MCSK_STEAM_CMD=true MCSK_GAME_PROCESS="${GN:?}" MCSK_START_SECONDS=2 MCSK_TIMEOUT_SECONDS=5; rc=$?
kill "${GP:?}" 2>/dev/null
[ "$rc" = 1 ]; check "9a exit 1 when game running" $? "rc=$rc"
cmp -s "${CFG:?}" "${S:?}/cfg-before.txt" && [ "$(backups)" = 0 ]; check "9b launch.cfg untouched" $?

# 10. timeout: game starts, no report
mk_env; write_cfg
GN="mcsk-g$$"; fake_game_prep
cat > "${S:?}/steam.sh" <<SH
#!/bin/sh
"${S:?}/${GN:?}" -c 'read -t 30 -u 3 x' 3<>"${S:?}/fifo" >/dev/null 2>&1 &
echo \$! > "${S:?}/game.pid"
SH
chmod +x "${S:?}/steam.sh"
remote_e2e MCSK_STEAM_CMD="${S:?}/steam.sh" MCSK_GAME_PROCESS="${GN:?}" MCSK_START_SECONDS=10 MCSK_TIMEOUT_SECONDS=3 MCSK_EXIT_WAIT_SECONDS=2; rc=$?
[ -s "${S:?}/game.pid" ] && kill "$(cat "${S:?}/game.pid")" 2>/dev/null
[ "$rc" = 3 ]; check "10a exit 3 on timeout" $? "rc=$rc"
cfg_reset; check "10b launch.cfg reset" $?

# 11. new-game options, --list-maps, default map
rrun4() { # usage errors must never launch anything: fake steam, no game, short timeouts
  envrun MCSK_STEAM_CMD=true MCSK_GAME_PROCESS="mcsk-nogame-$$" MCSK_START_SECONDS=1 MCSK_TIMEOUT_SECONDS=2 MCSK_POLL_SECONDS=1 MCSK_MOD_SRC="${BUILD:?}" MCSK_CS1_INSTALL="${S:?}/cs1" \
    timeout 30 bash "${REMOTE:?}" "$@" >"${S:?}/rout.txt" 2>"${S:?}/rerr.txt"
}
lrun() { # list-maps with extra env pairs first, then args
  envrun "$@" timeout 30 bash "${REMOTE:?}" --list-maps >"${S:?}/rout.txt" 2>"${S:?}/rerr.txt"
}
e2e_args() { # script args via ARGS array; maps under ${S}/cs1/Files/Maps prepared by the caller
  RUN="${LOGS:?}/20260101T000000Z"
  cat > "${S:?}/steam.sh" <<SH
#!/bin/sh
cp "${CFG:?}" "${S:?}/cfg-at-launch.txt"
sleep 1
mkdir -p "${RUN:?}"
echo '{"summary":{"pass":3,"fail":1,"skip":2,"error":0}}' > "${RUN:?}/report.json"
SH
  chmod +x "${S:?}/steam.sh"
  envrun MCSK_MOD_SRC="${BUILD:?}" MCSK_POLL_SECONDS=1 MCSK_CS1_INSTALL="${S:?}/cs1" MCSK_STEAM_CMD="${S:?}/steam.sh" MCSK_GAME_PROCESS="mcsk-nogame-$$" MCSK_START_SECONDS=20 MCSK_TIMEOUT_SECONDS=20 \
    timeout 60 bash "${REMOTE:?}" "${ARGS[@]}" >"${S:?}/rout.txt" 2>"${S:?}/rerr.txt"
}
mkmaps() { mkdir -p "${S:?}/cs1/Files/Maps"; local f; for f in "$@"; do mkdir -p "$(dirname "${S:?}/cs1/Files/Maps/$f")"; : > "${S:?}/cs1/Files/Maps/$f"; done; }

# 11a. exit-4 cases
mk_env
rrun4 --new-game; rc=$?; [ "$rc" = 4 ]; check "11a --new-game without value exit 4" $? "rc=$rc"
rrun4 --new-game ""; rc=$?; [ "$rc" = 4 ]; check "11b --new-game empty exit 4" $? "rc=$rc"
rrun4 --new-game "   "; rc=$?; [ "$rc" = 4 ]; check "11c --new-game blank exit 4" $? "rc=$rc"
rrun4 "My City" --new-game X; rc=$?; [ "$rc" = 4 ]; check "11d save then --new-game exit 4" $? "rc=$rc"
rrun4 --new-game X "My City"; rc=$?; [ "$rc" = 4 ]; check "11e --new-game then save exit 4" $? "rc=$rc"
rrun4 --new-game X --new-game Y; rc=$?; [ "$rc" = 4 ]; check "11f --new-game twice exit 4" $? "rc=$rc"
rrun4 --new-game X --bogus; rc=$?; [ "$rc" = 4 ]; check "11g --new-game with unknown option exit 4" $? "rc=$rc"
rrun4 --help; rc=$?; [ "$rc" = 0 ] && grep -q -- '--new-game' "${S:?}/rout.txt" "${S:?}/rerr.txt"
check "11h --help exit 0 and mentions --new-game" $? "rc=$rc"

# 12. --list-maps
mk_env
mkmaps "Zeta.crp" "alpha.crp" "Sub/Deep/Mid Map.crp" "dup/Zeta.crp" "readme.txt" "Sub/notes.txt" "fake.crp.bak"
lrun MCSK_CS1_INSTALL="${S:?}/cs1"; rc=$?
[ "$rc" = 0 ] && [ "$(cat "${S:?}/rout.txt")" = "$(printf 'Mid Map\nZeta\nalpha')" ]
check "12a list-maps via MCSK_CS1_INSTALL: sorted (C), recursive, deduped, .crp only" $? "rc=$rc got: $(tr '\n' '|' < "${S:?}/rout.txt")"

mk_env
mkmaps "Bravo.crp" "Alpha.crp"
mkdir -p "${H:?}/.cache/minecraft-skylines/refs"
printf 'other: x\ncs1_install: %s\n' "${S:?}/cs1" > "${H:?}/.cache/minecraft-skylines/refs/environment.txt"
lrun; rc=$?
[ "$rc" = 0 ] && [ "$(cat "${S:?}/rout.txt")" = "$(printf 'Alpha\nBravo')" ]
check "12b list-maps via refs/environment.txt cs1_install" $? "rc=$rc got: $(tr '\n' '|' < "${S:?}/rout.txt")"

mk_env
mkmaps "Env One.crp"
mkdir -p "${S:?}/other/Files/Maps"; : > "${S:?}/other/Files/Maps/Other.crp"
mkdir -p "${H:?}/.cache/minecraft-skylines/refs"
printf 'cs1_install: %s\n' "${S:?}/other" > "${H:?}/.cache/minecraft-skylines/refs/environment.txt"
lrun MCSK_CS1_INSTALL="${S:?}/cs1"; rc=$?
[ "$rc" = 0 ] && [ "$(cat "${S:?}/rout.txt")" = "Env One" ]
check "12c MCSK_CS1_INSTALL wins over environment.txt" $? "rc=$rc got: $(tr '\n' '|' < "${S:?}/rout.txt")"

mk_env
lrun MCSK_CS1_INSTALL="${S:?}/cs1"; rc=$?
[ "$rc" = 0 ] && [ ! -s "${S:?}/rout.txt" ] && grep -qF "${S:?}/cs1/Files/Maps" "${S:?}/rerr.txt" && grep -qF 'Green Plains' "${S:?}/rerr.txt"
check "12d missing Maps folder: empty stdout, stderr has path and Green Plains, exit 0" $? "rc=$rc err: $(head -c 300 "${S:?}/rerr.txt")"

# 13. end to end new game
mk_env; write_cfg; mkmaps "Alpha.crp"
ARGS=(--new-game "Two Rivers"); e2e_args; rc=$?
check "13a --new-game exit 0" "$rc" "rc=$rc; $(head -c 300 "${S:?}/rerr.txt")"
grep -qxF 'autoload = new:Two Rivers' "${S:?}/cfg-at-launch.txt" && grep -qxF 'selftest = city_load' "${S:?}/cfg-at-launch.txt" && grep -qxF 'selftest_quit = true' "${S:?}/cfg-at-launch.txt"
check "13b launch.cfg at launch has autoload = new:Two Rivers" $?
cfg_reset; check "13c launch.cfg reset afterwards" $?
grep -qxF 'Self-test: 3 pass, 1 fail, 2 skip, 0 error' "${S:?}/rout.txt"; check "13d summary line" $?

# 5a / 14. no args = new game on default map
mk_env; write_cfg; mkmaps "Alpha.crp" "sub/Green Plains.crp"
ARGS=(); e2e_args; rc=$?
check "5a no args: new game, exit 0" "$rc" "rc=$rc; $(head -c 300 "${S:?}/rerr.txt")"
grep -qxF 'autoload = new:Green Plains' "${S:?}/cfg-at-launch.txt"; check "14a default map is Green Plains (subfolder)" $?
cfg_reset; check "14b launch.cfg reset afterwards" $?

mk_env; write_cfg; mkmaps "green plains.crp" "Alpha.crp"
ARGS=(); e2e_args; rc=$?
grep -qxF 'autoload = new:green plains' "${S:?}/cfg-at-launch.txt"; check "14c default matches case-insensitively, listed spelling used" $? "rc=$rc"

mk_env; write_cfg; mkmaps "Beta.crp" "Alpha.crp"
ARGS=(); e2e_args; rc=$?
grep -qxF 'autoload = new:Alpha' "${S:?}/cfg-at-launch.txt"; check "14d no Green Plains: first listed map" $? "rc=$rc"

mk_env; write_cfg; mkdir -p "${S:?}/cs1"
ARGS=(); e2e_args; rc=$?
grep -qxF 'autoload = new:Green Plains' "${S:?}/cfg-at-launch.txt"; check "14e no Maps folder: Green Plains" $? "rc=$rc"

echo "failures: $FAILS"
[ "$FAILS" = 0 ]
