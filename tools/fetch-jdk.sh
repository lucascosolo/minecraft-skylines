#!/usr/bin/env bash
# Fetch a self-contained JDK 25 (Eclipse Temurin, free) into ~/.cache/minecraft-skylines/jdk/ for
# starting Minecraft from inside Steam's Linux runtime container, which cannot see the system's
# /usr/lib/jvm. Verifies the SHA-256 published by Adoptium. Deletes nothing; re-running is a no-op
# when the JDK is already there.
set -euo pipefail
DEST="${HOME:?}/.cache/minecraft-skylines/jdk"
mkdir -p "${DEST:?}"
if ls -d "${DEST:?}"/jdk-25*/bin/java >/dev/null 2>&1; then
  echo "JDK already present: $(ls -d "${DEST:?}"/jdk-25*/)"
  exit 0
fi
META="$(curl -fsSL -m 30 'https://api.adoptium.net/v3/assets/latest/25/hotspot?architecture=x64&image_type=jdk&os=linux&vendor=eclipse')"
LINK="$(printf '%s' "${META}" | python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["binary"]["package"]["link"])')"
SUM="$(printf '%s' "${META}" | python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["binary"]["package"]["checksum"])')"
curl -fSL -m 600 -o "${DEST:?}/temurin25.tar.gz" "${LINK:?}"
echo "${SUM:?}  ${DEST:?}/temurin25.tar.gz" | sha256sum -c
tar -xzf "${DEST:?}/temurin25.tar.gz" -C "${DEST:?}"
echo "JDK ready: $(ls -d "${DEST:?}"/jdk-25*/)"
