#!/usr/bin/env bash
# Everything that can be checked without either game running. Reports land in reports/.
# Never runs a clean task and never deletes anything (project rule).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT:?}"
REPORTS="${ROOT:?}/reports"
mkdir -p "${REPORTS:?}/cs1" "${REPORTS:?}/java" "${REPORTS:?}/protocol"

DOTNET="${DOTNET:-$HOME/.cache/dotnet/sdk10/dotnet}"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$HOME/.cache/minecraft-skylines/dotnet-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$HOME/.cache/minecraft-skylines/nuget}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export GRADLE_USER_HOME="${GRADLE_USER_HOME:-$HOME/.cache/gradle-home}"

step() { printf '\n== %s\n' "$*"; }

step "protocol: regenerate vectors and confirm they are unchanged"
python3 protocol/reference/gen_vectors.py >/dev/null
git diff --exit-code -- protocol/vectors/ || { echo "vectors drifted from gen_vectors.py"; exit 1; }

step "protocol: conformance suite validated against the Python reference peer"
(cd protocol/reference && python3 conformance.py host --junit "${REPORTS}/protocol/ref-host.xml" -- python3 ref_peer.py | tail -1)
(cd protocol/reference && python3 conformance.py guest --junit "${REPORTS}/protocol/ref-guest.xml" -- python3 ref_peer.py | tail -1)

step "cs1: build (net35 + net10.0) and unit tests"
"${DOTNET}" build cs1/MinecraftSkylines.sln -c Release -nologo -v quiet
"${DOTNET}" test cs1/MinecraftSkylines.sln -c Release --no-build -nologo \
  --logger "trx" --results-directory "${REPORTS}/cs1"

step "cs1: bridge conformance (host and guest)"
IUT_CS=("${DOTNET}" cs1/tools/Skylines.Bridge.Conformance/bin/Release/net10.0/Skylines.Bridge.Conformance.dll)
python3 protocol/reference/conformance.py host --junit "${REPORTS}/cs1/conformance-host.xml" -- "${IUT_CS[@]}" | tail -1
python3 protocol/reference/conformance.py guest --junit "${REPORTS}/cs1/conformance-guest.xml" -- "${IUT_CS[@]}" | tail -1

step "java: build, unit tests, bridge install"
(cd minecraft && ./gradlew build :bridge:installDist --console=plain -q)

step "java: bridge conformance (guest)"
python3 protocol/reference/conformance.py guest --junit "${REPORTS}/java/conformance-guest.xml" -- minecraft/bridge/build/install/bridge/bin/bridge | tail -1

step "cross-language: C# host <-> Java guest"
python3 tools/interop.py --host "${IUT_CS[*]}" --guest minecraft/bridge/build/install/bridge/bin/bridge --junit "${REPORTS}/interop.xml"

echo
echo "All checks passed."
