#!/usr/bin/env bash
# Copy the mod's logs, the game's Player.log and Minecraft's log into a folder the agent can read:
#     bash ~/Workspaces/minecraft-skylines/tools/collect-evidence.sh
# Copies only; deletes and changes nothing.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DATA="${XDG_DATA_HOME:-${HOME:?}/.local/share}"
MODLOGS="${DATA:?}/Colossal Order/Cities_Skylines/ModLogs"
PLAYER_LOG="${XDG_CONFIG_HOME:-${HOME:?}/.config}/unity3d/Colossal Order/Cities_ Skylines/Player.log"
MC_LOG="${ROOT:?}/minecraft/fabric/run/logs/latest.log"
OUT="${HOME:?}/.cache/minecraft-skylines/evidence/collected-$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "${OUT:?}"
[ -d "${MODLOGS}" ] && cp -r "${MODLOGS}" "${OUT:?}/ModLogs"
[ -f "${PLAYER_LOG}" ] && cp "${PLAYER_LOG}" "${OUT:?}/"
[ -f "${MC_LOG}" ] && cp "${MC_LOG}" "${OUT:?}/minecraft-latest.log"
echo "copied to ${OUT}"
