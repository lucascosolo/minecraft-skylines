# Source before ./gradlew inside a proxied sandbox: Java ignores HTTPS_PROXY, so this maps it to
# JAVA_TOOL_OPTIONS system properties. Recompute per shell; the proxy credentials rotate.
_p="${HTTPS_PROXY:-${https_proxy:-}}"
if [ -n "$_p" ]; then
  JAVA_TOOL_OPTIONS="$(python3 -c '
import sys, urllib.parse as u
p = u.urlsplit(sys.argv[1]); h, pt = p.hostname, p.port or 80
o = []
for s in ("http", "https"):
    o += [f"-D{s}.proxyHost={h}", f"-D{s}.proxyPort={pt}"]
    if p.username:
        o += [f"-D{s}.proxyUser={u.unquote(p.username)}", f"-D{s}.proxyPassword={u.unquote(p.password or "")}"]
o += ["-Djdk.http.auth.tunneling.disabledSchemes=", "-Djdk.http.auth.proxying.disabledSchemes=",
      "-Dhttp.nonProxyHosts=localhost|127.0.0.1"]
print(" ".join(o))' "$_p")"
  export JAVA_TOOL_OPTIONS
fi
unset _p
export GRADLE_USER_HOME="${GRADLE_USER_HOME:-$HOME/.cache/gradle-home}"
