#!/usr/bin/env bash
# ./gradlew for the agent sandbox only: maps HTTPS_PROXY into Java system properties and loads the
# Unix-socket shim (sandbox-shim/) into the Gradle daemon. Outside the sandbox, use ./gradlew directly.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
. "$here/sandbox-proxy-env.sh"
out="${HOME:?}/.cache/minecraft-skylines/sandbox-shim"
jar="$out/nounix-shim.jar"
src="$here/sandbox-shim/NoUnixSelectorProvider.java"
if [ ! -f "$jar" ] || [ "$src" -nt "$jar" ]; then
  mkdir -p "$out/classes"
  javac -q --add-exports java.base/sun.nio.ch=ALL-UNNAMED -d "$out/classes" "$src" 2>/dev/null \
    || javac --add-exports java.base/sun.nio.ch=ALL-UNNAMED -d "$out/classes" "$src"
  jar --create --file "$jar" -C "$out/classes" .
fi
# Gradle drops -D options from org.gradle.jvmargs, so the shim rides in JAVA_TOOL_OPTIONS, which every
# JVM the build starts (client, daemon, test workers) inherits.
export JAVA_TOOL_OPTIONS="${JAVA_TOOL_OPTIONS:-} -Xbootclasspath/a:$jar --add-exports=java.base/sun.nio.ch=ALL-UNNAMED -Djava.nio.channels.spi.SelectorProvider=dev.mcskylines.sandboxshim.NoUnixSelectorProvider"
exec "$here/gradlew" "$@"
