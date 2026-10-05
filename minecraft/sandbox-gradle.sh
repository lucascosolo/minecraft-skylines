#!/usr/bin/env bash
# ./gradlew for the agent sandbox only. Outside the sandbox, run ./gradlew directly.
#  1. Java ignores HTTPS_PROXY, and Loom's HttpClient cannot answer a proxy 407, so a loopback forwarder
#     (sandbox-shim/auth_proxy.py) adds the credentials and Java is pointed at it.
#  2. The sandbox forbids AF_UNIX sockets (EPERM), which Loom 1.18's platform probe does not tolerate;
#     sandbox-shim/NoUnixSelectorProvider reports them as unsupported instead.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
export GRADLE_USER_HOME="${GRADLE_USER_HOME:-$HOME/.cache/gradle-home}"
out="${HOME:?}/.cache/minecraft-skylines/sandbox-shim"
jar="$out/nounix-shim.jar"
src="$here/sandbox-shim/NoUnixSelectorProvider.java"
if [ ! -f "$jar" ] || [ "$src" -nt "$jar" ]; then
  mkdir -p "$out/classes"
  javac --add-exports java.base/sun.nio.ch=ALL-UNNAMED -d "$out/classes" "$src"
  jar --create --file "$jar" -C "$out/classes" .
fi
opts="-Xbootclasspath/a:$jar --add-exports=java.base/sun.nio.ch=ALL-UNNAMED -Djava.nio.channels.spi.SelectorProvider=dev.mcskylines.sandboxshim.NoUnixSelectorProvider"
upstream="${HTTPS_PROXY:-${https_proxy:-}}"
if [ -n "$upstream" ]; then
  coproc FWD { exec python3 "$here/sandbox-shim/auth_proxy.py" "$upstream"; }
  fwd_pid=$FWD_PID
  trap 'kill "$fwd_pid" 2>/dev/null || true' EXIT
  read -r port <&"${FWD[0]}"
  for s in http https; do opts="$opts -D$s.proxyHost=127.0.0.1 -D$s.proxyPort=$port"; done
  opts="$opts -Dhttp.nonProxyHosts=localhost|127.0.0.1"
fi
export JAVA_TOOL_OPTIONS="$opts"
"$here/gradlew" "$@"
