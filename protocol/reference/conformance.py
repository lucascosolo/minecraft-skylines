"""Black-box conformance suite for SKBR bridge implementations.

Runs an implementation under test (IUT) as a child process and plays the other side with the
reference codec. Every scenario is a rule from protocol/bridge-v1.md.

    python3 protocol/reference/conformance.py host  -- <command that starts an IUT host>
    python3 protocol/reference/conformance.py guest -- <command that starts an IUT guest>
    python3 protocol/reference/conformance.py ... --junit report.xml

IUT command-line contract (the suite appends these arguments):
    host  --port P --heartbeat-ms 200 --timeout-ms 1000 --app skbr-conformance
    guest --port P --app skbr-conformance --major 1 --retry-ms 200
The IUT speaks app protocol "skbr-conformance" 1.0: on app frame 0x0100 (ECHO_REQUEST) it
replies with 0x0101 (ECHO_REPLY) carrying the same payload. It prints `READY` on stdout once it
is listening (host) or started (guest), prints `EVENT ...` lines for bridge events, and shuts
down gracefully (GOODBYE SHUTTING_DOWN to a connected peer) when its stdin closes.
"""
from __future__ import annotations

import argparse
import os
import random
import socket
import subprocess
import sys
import threading
import time
import traceback
import xml.etree.ElementTree as ET
from typing import Callable

import skbridge as sb

APP = "skbr-conformance"
ECHO_REQUEST, ECHO_REPLY = 0x0100, 0x0101
HB_MS, TIMEOUT_MS = 200, 1000
SLACK_S = 2.0


def free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


class Iut:
    def __init__(self, cmd: list[str], extra: list[str]) -> None:
        self.proc = subprocess.Popen(cmd + extra, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                     stderr=subprocess.STDOUT, text=True, bufsize=1)
        self.lines: list[str] = []
        self.ready = threading.Event()
        threading.Thread(target=self._pump, daemon=True).start()
        if not self.ready.wait(30):
            self.stop()
            raise AssertionError("IUT did not print READY within 30 s:\n" + "\n".join(self.lines[-30:]))

    def _pump(self) -> None:
        assert self.proc.stdout
        for line in self.proc.stdout:
            line = line.rstrip("\n")
            self.lines.append(line)
            if line.strip() == "READY":
                self.ready.set()

    def events(self, prefix: str) -> list[str]:
        return [l for l in self.lines if l.startswith("EVENT " + prefix)]

    def wait_event(self, prefix: str, timeout_s: float) -> str:
        deadline = time.monotonic() + timeout_s
        while time.monotonic() < deadline:
            for l in self.lines:
                if l.startswith("EVENT " + prefix):
                    return l
            time.sleep(0.02)
        raise AssertionError(f"no 'EVENT {prefix}' within {timeout_s}s; last lines:\n" + "\n".join(self.lines[-20:]))

    def stop(self) -> int:
        try:
            if self.proc.stdin:
                self.proc.stdin.close()
            return self.proc.wait(5)
        except subprocess.TimeoutExpired:
            self.proc.kill()  # SIGKILL to our own child process, by handle
            self.proc.wait(5)
            raise AssertionError("IUT did not exit within 5 s of stdin closing")


def hello(**over) -> bytes:
    h = sb.Hello(APP, 1, 0, "conformance-guest", "0", random.getrandbits(64))
    for k, v in over.items():
        setattr(h, k, v)
    return h.encode()


def handshake(port: int) -> tuple[sb.Conn, sb.Welcome]:
    c = sb.Conn.connect(port)
    c.send_frame(sb.HELLO, hello())
    f = c.recv_until({sb.WELCOME}, 5)
    assert f is not None, "host closed instead of sending WELCOME"
    w = sb.Welcome.decode(f[2])
    return c, w


def expect_closed(c: sb.Conn, timeout_s: float, goodbye_code: int | None = None, require_goodbye: bool = False) -> None:
    seen = c.wait_closed(timeout_s)
    byes = [sb.Goodbye.decode(p) for t, _, p in seen if t == sb.GOODBYE]
    if require_goodbye:
        assert byes, f"expected GOODBYE before close, saw types {[hex(t) for t, _, _ in seen]}"
    if goodbye_code is not None and byes:
        assert byes[-1].code == goodbye_code, f"GOODBYE code {byes[-1].code}, expected {goodbye_code}"


# ---- host scenarios (IUT is the host, this script is the guest) --------------------------------
def h_handshake_ok(port, iut):
    c, w = handshake(port)
    assert w.accepted and w.reject_code == 0 and w.reject_reason == ""
    assert w.bridge_version == 1 and w.app_protocol == APP and w.app_major == 1
    assert w.session_id != 0 and w.heartbeat_interval_ms == HB_MS and w.peer_timeout_ms == TIMEOUT_MS
    iut.wait_event("state connected", 2)
    c.close()


def h_echo(port, iut):
    c, w = handshake(port)
    for payload in (b"", b"abc", os.urandom(70000)):
        c.send_frame(ECHO_REQUEST, payload)
        f = c.recv_until({ECHO_REPLY}, 3)
        assert f is not None and f[2] == payload, "echo mismatch"
    c.close()


def h_unknown_app_type_ignored(port, iut):
    c, w = handshake(port)
    c.send_frame(0x7FFF, b"future message")
    c.send_frame(ECHO_REQUEST, b"still alive")
    f = c.recv_until({ECHO_REPLY, sb.GOODBYE}, 3)
    assert f is not None and f[0] == ECHO_REPLY, "unknown app type must not end the session"
    c.close()


def h_heartbeats(port, iut):
    c, w = handshake(port)
    seqs = []
    deadline = time.monotonic() + 3 * HB_MS / 1000 + 0.5
    while time.monotonic() < deadline:
        c.send_frame(sb.HEARTBEAT, sb.Heartbeat(len(seqs) + 1, 0).encode())
        try:
            f = c.recv_frame(0.1)
        except TimeoutError:
            continue
        assert f is not None, "host closed during heartbeat exchange"
        if f[0] == sb.HEARTBEAT:
            seqs.append(sb.Heartbeat.decode(f[2]).seq)
    assert len(seqs) >= 2, f"expected >= 2 heartbeats, got {seqs}"
    assert seqs == sorted(seqs) and len(set(seqs)) == len(seqs), f"heartbeat seq not increasing: {seqs}"
    c.close()


def h_wrong_magic(port, iut):
    c = sb.Conn.connect(port)
    c.send_frame(sb.HELLO, hello(magic=0xDEADBEEF))
    seen = c.wait_closed(5)
    assert seen == [], f"host must close without replying, sent {[hex(t) for t, _, _ in seen]}"


def _rejected(port, code, **over):
    c = sb.Conn.connect(port)
    c.send_frame(sb.HELLO, hello(**over))
    f = c.recv_until({sb.WELCOME}, 5)
    assert f is not None, "expected WELCOME(rejected), host just closed"
    w = sb.Welcome.decode(f[2])
    assert not w.accepted and w.reject_code == code, f"accepted={w.accepted} code={w.reject_code}, expected {code}"
    assert w.reject_reason, "reject reason must be non-empty"
    c.wait_closed(3)


def h_reject_major(port, iut):
    _rejected(port, sb.REJECT_APP_MAJOR, app_major=2)


def h_reject_app(port, iut):
    _rejected(port, sb.REJECT_APP_PROTOCOL, app_protocol="someone-else")


def h_reject_bridge_version(port, iut):
    _rejected(port, sb.REJECT_BRIDGE_VERSION, bridge_version=2)


def h_busy(port, iut):
    a, w = handshake(port)
    assert w.accepted
    _rejected(port, sb.REJECT_BUSY)
    a.send_frame(ECHO_REQUEST, b"first guest unaffected")
    f = a.recv_until({ECHO_REPLY}, 3)
    assert f is not None and f[2] == b"first guest unaffected"
    a.close()


def h_guest_silence_times_out(port, iut):
    c, w = handshake(port)
    t0 = time.monotonic()
    expect_closed(c, TIMEOUT_MS / 1000 + SLACK_S, goodbye_code=sb.BYE_TIMEOUT)
    assert time.monotonic() - t0 >= TIMEOUT_MS / 1000 * 0.8, "host timed out too early"
    iut.wait_event("disconnected cause=timeout", 2)


def h_goodbye_then_reconnect(port, iut):
    c, w = handshake(port)
    c.send_frame(sb.GOODBYE, sb.Goodbye(sb.BYE_NORMAL, "bye").encode())
    c.wait_closed(3)
    iut.wait_event("disconnected cause=peer_goodbye", 2)
    c2, w2 = handshake(port)
    assert w2.accepted and w2.session_id != w.session_id, "host must accept a new guest with a new sessionId"
    c2.close()


def h_abrupt_close_then_reconnect(port, iut):
    c, w = handshake(port)
    c.close()
    iut.wait_event("disconnected cause=connection_lost", 3)
    c2, w2 = handshake(port)
    assert w2.accepted
    c2.close()


def h_nonzero_flags(port, iut):
    c, w = handshake(port)
    c.send_frame(sb.HEARTBEAT, sb.Heartbeat(1, 0).encode(), flags=1)
    expect_closed(c, 3, goodbye_code=sb.BYE_PROTOCOL_ERROR)


def h_oversize(port, iut):
    c, w = handshake(port)
    c.send(sb.HEADER.pack(sb.MAX_PAYLOAD + 1, ECHO_REQUEST, 0))
    expect_closed(c, 3, goodbye_code=sb.BYE_PROTOCOL_ERROR)


def h_app_before_handshake(port, iut):
    c = sb.Conn.connect(port)
    c.send_frame(ECHO_REQUEST, b"too early")
    expect_closed(c, 3, goodbye_code=sb.BYE_PROTOCOL_ERROR)


def h_no_hello(port, iut):
    c = sb.Conn.connect(port)
    t0 = time.monotonic()
    c.wait_closed(5 + SLACK_S)
    assert time.monotonic() - t0 >= 4.0, "host closed a silent connection before 5 s"


def h_graceful_shutdown(port, iut):
    c, w = handshake(port)
    iut.stop()
    expect_closed(c, 3, goodbye_code=sb.BYE_SHUTTING_DOWN, require_goodbye=True)


HOST_SCENARIOS: list[tuple[str, Callable]] = [
    ("handshake_ok", h_handshake_ok), ("echo", h_echo),
    ("unknown_app_type_ignored", h_unknown_app_type_ignored), ("heartbeats", h_heartbeats),
    ("wrong_magic_no_reply", h_wrong_magic), ("reject_app_major", h_reject_major),
    ("reject_app_protocol", h_reject_app), ("reject_bridge_version", h_reject_bridge_version),
    ("busy", h_busy), ("guest_silence_times_out", h_guest_silence_times_out),
    ("goodbye_then_reconnect", h_goodbye_then_reconnect),
    ("abrupt_close_then_reconnect", h_abrupt_close_then_reconnect),
    ("nonzero_flags_protocol_error", h_nonzero_flags), ("oversize_protocol_error", h_oversize),
    ("app_frame_before_handshake", h_app_before_handshake), ("no_hello_closed", h_no_hello),
    ("graceful_shutdown_goodbye", h_graceful_shutdown),
]


# ---- guest scenarios (IUT is the guest, this script is the host) --------------------------------
class Listener:
    def __init__(self, port: int) -> None:
        self.sock = socket.socket()
        self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.sock.bind(("127.0.0.1", port))
        self.sock.listen(4)

    def accept(self, timeout_s: float) -> sb.Conn:
        self.sock.settimeout(timeout_s)
        s, _ = self.sock.accept()
        return sb.Conn(s)

    def close(self) -> None:
        self.sock.close()


def welcome(accepted=True, code=0, reason="", sid=None) -> bytes:
    return sb.Welcome(accepted, code, reason, APP, 1, 0, "conformance-host", "0", HB_MS, TIMEOUT_MS,
                      sid if sid is not None else (random.getrandbits(63) | 1) if accepted else 0).encode()


def accept_hello(lst: Listener, timeout_s: float = 5) -> tuple[sb.Conn, sb.Hello]:
    c = lst.accept(timeout_s)
    f = c.recv_frame(5)
    assert f is not None and f[0] == sb.HELLO, f"first frame must be HELLO, got {f and hex(f[0])}"
    return c, sb.Hello.decode(f[2])


def g_hello_fields(lst, iut):
    c, h = accept_hello(lst)
    assert h.magic == sb.MAGIC and h.bridge_version == 1 and h.app_protocol == APP and h.app_major == 1
    assert h.peer_name, "peerName must be non-empty"
    c.send_frame(sb.WELCOME, welcome())
    iut.wait_event("state connected", 2)
    c.close()


def g_heartbeats(lst, iut):
    c, h = accept_hello(lst)
    c.send_frame(sb.WELCOME, welcome())
    n = 0
    deadline = time.monotonic() + 3 * HB_MS / 1000 + 0.5
    while time.monotonic() < deadline:
        c.send_frame(sb.HEARTBEAT, sb.Heartbeat(n + 1, 0).encode())
        try:
            f = c.recv_frame(0.1)
        except TimeoutError:
            continue
        assert f is not None
        n += f[0] == sb.HEARTBEAT
    assert n >= 2, f"guest must heartbeat at the host's interval; saw {n}"
    c.close()


def g_echo(lst, iut):
    c, h = accept_hello(lst)
    c.send_frame(sb.WELCOME, welcome())
    c.send_frame(ECHO_REQUEST, b"ping")
    f = c.recv_until({ECHO_REPLY}, 3)
    assert f is not None and f[2] == b"ping"
    c.close()


def g_reject_major_stops_retrying(lst, iut):
    c, h = accept_hello(lst)
    c.send_frame(sb.WELCOME, welcome(False, sb.REJECT_APP_MAJOR, "major mismatch"))
    c.wait_closed(3)
    iut.wait_event("disconnected cause=rejected", 2)
    try:
        lst.accept(2.0).close()
    except socket.timeout:
        return
    raise AssertionError("guest reconnected after an APP_MAJOR rejection")


def g_reject_busy_retries(lst, iut):
    c, h = accept_hello(lst)
    c.send_frame(sb.WELCOME, welcome(False, sb.REJECT_BUSY, "busy"))
    c.wait_closed(3)
    c2, h2 = accept_hello(lst, 3)
    assert h2.session_nonce != h.session_nonce, "sessionNonce must be fresh per attempt"
    c2.send_frame(sb.WELCOME, welcome())
    c2.close()


def g_host_goodbye_then_reconnect(lst, iut):
    c, h = accept_hello(lst)
    c.send_frame(sb.WELCOME, welcome())
    iut.wait_event("state connected", 2)
    c.send_frame(sb.GOODBYE, sb.Goodbye(sb.BYE_SHUTTING_DOWN, "city unloading").encode())
    c.wait_closed(3)
    iut.wait_event("disconnected cause=peer_goodbye", 2)
    c2, _ = accept_hello(lst, 3)
    c2.close()


def g_host_silence_times_out(lst, iut):
    c, h = accept_hello(lst)
    c.send_frame(sb.WELCOME, welcome())
    expect_closed(c, TIMEOUT_MS / 1000 + SLACK_S, goodbye_code=sb.BYE_TIMEOUT)
    iut.wait_event("disconnected cause=timeout", 2)
    c2, _ = accept_hello(lst, 3)
    c2.close()


def g_abrupt_close_reconnect(lst, iut):
    c, h = accept_hello(lst)
    c.send_frame(sb.WELCOME, welcome())
    iut.wait_event("state connected", 2)
    c.close()
    iut.wait_event("disconnected cause=connection_lost", 3)
    c2, _ = accept_hello(lst, 3)
    c2.close()


def g_no_welcome_closes(lst, iut):
    c, h = accept_hello(lst)
    t0 = time.monotonic()
    c.wait_closed(5 + SLACK_S)
    assert time.monotonic() - t0 >= 4.0, "guest gave up on WELCOME before 5 s"


def g_graceful_shutdown(lst, iut):
    c, h = accept_hello(lst)
    c.send_frame(sb.WELCOME, welcome())
    iut.wait_event("state connected", 2)
    iut.stop()
    expect_closed(c, 3, goodbye_code=sb.BYE_SHUTTING_DOWN, require_goodbye=True)


GUEST_SCENARIOS: list[tuple[str, Callable]] = [
    ("hello_fields", g_hello_fields), ("heartbeats", g_heartbeats), ("echo", g_echo),
    ("reject_app_major_stops_retrying", g_reject_major_stops_retrying),
    ("reject_busy_retries", g_reject_busy_retries),
    ("host_goodbye_then_reconnect", g_host_goodbye_then_reconnect),
    ("host_silence_times_out", g_host_silence_times_out),
    ("abrupt_close_then_reconnect", g_abrupt_close_reconnect),
    ("no_welcome_closes", g_no_welcome_closes), ("graceful_shutdown_goodbye", g_graceful_shutdown),
]


def run(role: str, cmd: list[str], only: str | None) -> list[tuple[str, float, str | None]]:
    results = []
    for name, fn in (HOST_SCENARIOS if role == "host" else GUEST_SCENARIOS):
        if only and only not in name:
            continue
        port = free_port()
        t0 = time.monotonic()
        err = None
        iut = lst = None
        try:
            if role == "host":
                iut = Iut(cmd, ["host", "--port", str(port), "--heartbeat-ms", str(HB_MS),
                                "--timeout-ms", str(TIMEOUT_MS), "--app", APP])
                fn(port, iut)
            else:
                lst = Listener(port)
                iut = Iut(cmd, ["guest", "--port", str(port), "--app", APP, "--major", "1",
                                "--retry-ms", "200"])
                fn(lst, iut)
        except Exception:
            err = traceback.format_exc()
            if iut:
                err += "\nIUT output (last 25 lines):\n" + "\n".join(iut.lines[-25:])
        finally:
            if iut and iut.proc.poll() is None:
                try:
                    iut.stop()
                except AssertionError as e:
                    err = (err or "") + f"\n{e}"
            if lst:
                lst.close()
        dt = time.monotonic() - t0
        results.append((name, dt, err))
        print(f"{'PASS' if err is None else 'FAIL'} {role}/{name} ({dt:.2f}s)", flush=True)
        if err:
            print(err, flush=True)
    return results


def write_junit(path: str, role: str, results) -> None:
    suite = ET.Element("testsuite", name=f"skbr-conformance-{role}", tests=str(len(results)),
                       failures=str(sum(1 for r in results if r[2])))
    for name, dt, err in results:
        tc = ET.SubElement(suite, "testcase", classname=f"conformance.{role}", name=name, time=f"{dt:.3f}")
        if err:
            ET.SubElement(tc, "failure", message=err.strip().splitlines()[-1][:200]).text = err
    ET.ElementTree(suite).write(path, encoding="utf-8", xml_declaration=True)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("role", choices=["host", "guest"], help="the role the IUT plays")
    ap.add_argument("--junit")
    ap.add_argument("--only")
    ap.add_argument("cmd", nargs=argparse.REMAINDER)
    a = ap.parse_args()
    cmd = a.cmd[1:] if a.cmd and a.cmd[0] == "--" else a.cmd
    if not cmd:
        ap.error("missing IUT command after --")
    results = run(a.role, cmd, a.only)
    if a.junit:
        write_junit(a.junit, a.role, results)
    failed = [r[0] for r in results if r[2]]
    print(f"{len(results) - len(failed)}/{len(results)} passed" + (f"; failed: {failed}" if failed else ""))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
