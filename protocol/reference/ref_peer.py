"""Reference SKBR peer: a complete host or guest, used to validate conformance.py and as a
stand-in for either game during development.

    python3 ref_peer.py host  --port 47615 [--app minecraft-skylines]
    python3 ref_peer.py guest --port 47615 [--app minecraft-skylines]

Follows the IUT contract documented in conformance.py (READY, EVENT lines, echo app frames,
graceful shutdown when stdin closes). With --app minecraft-skylines it also sends a status frame
after the handshake (HOST_STATUS or GUEST_STATUS) and prints the peer's status frames.
"""
from __future__ import annotations

import argparse
import os
import random
import socket
import sys
import threading
import time
import uuid

import skbridge as sb

ECHO_REQUEST, ECHO_REPLY = 0x0100, 0x0101
START = time.monotonic()


def uptime_ms() -> int:
    return int((time.monotonic() - START) * 1000)


def event(s: str) -> None:
    print("EVENT " + s, flush=True)


class Session:
    """One connected socket after (or during) the handshake. Runs reader + heartbeat threads."""

    def __init__(self, conn: sb.Conn, app: str, interval_ms: int, timeout_ms: int, on_end) -> None:
        self.c, self.app = conn, app
        self.interval_ms, self.timeout_ms = interval_ms, timeout_ms
        self.lock = threading.Lock()
        self.closed = False
        self.last_rx = time.monotonic()
        self.on_end = on_end
        self.hb_seq = 0

    def send(self, type_: int, payload: bytes) -> None:
        with self.lock:
            if not self.closed:
                try:
                    self.c.send_frame(type_, payload)
                except OSError:
                    pass

    def end(self, cause: str, code: int = -1, reason: str = "", send_bye: int | None = None) -> None:
        with self.lock:
            if self.closed:
                return
            if send_bye is not None:
                try:
                    self.c.send_frame(sb.GOODBYE, sb.Goodbye(send_bye, reason).encode())
                    self.c.sock.shutdown(socket.SHUT_WR)
                except OSError:
                    pass
            self.closed = True
            self.c.close()
        event(f"disconnected cause={cause} code={code} reason={reason!r}")
        self.on_end()

    def start(self) -> None:
        threading.Thread(target=self._read, daemon=True).start()
        threading.Thread(target=self._beat, daemon=True).start()

    def _beat(self) -> None:
        while not self.closed:
            time.sleep(self.interval_ms / 1000)
            if self.closed:
                return
            if (time.monotonic() - self.last_rx) * 1000 > self.timeout_ms:
                self.end("timeout", sb.BYE_TIMEOUT, "peer silent", send_bye=sb.BYE_TIMEOUT)
                return
            self.hb_seq += 1
            self.send(sb.HEARTBEAT, sb.Heartbeat(self.hb_seq, uptime_ms()).encode())

    def _read(self) -> None:
        while not self.closed:
            try:
                f = self.c.recv_frame(0.1)
            except TimeoutError:
                continue
            except sb.ProtocolError as e:
                self.end("protocol_error", sb.BYE_PROTOCOL_ERROR, str(e), send_bye=sb.BYE_PROTOCOL_ERROR)
                return
            except OSError:
                f = None
            if f is None:
                if not self.closed:
                    self.end("connection_lost")
                return
            self.last_rx = time.monotonic()
            type_, flags, payload = f
            try:
                if flags != 0:
                    raise sb.ProtocolError("non-zero flags")
                if type_ == sb.GOODBYE:
                    g = sb.Goodbye.decode(payload)
                    self.end("peer_goodbye", g.code, g.reason)
                    return
                if type_ == sb.HEARTBEAT:
                    sb.Heartbeat.decode(payload)
                elif type_ < sb.APP_MIN:
                    raise sb.ProtocolError(f"unexpected bridge frame 0x{type_:04x} after handshake")
                else:
                    self.on_app(type_, payload)
            except sb.ProtocolError as e:
                self.end("protocol_error", sb.BYE_PROTOCOL_ERROR, str(e), send_bye=sb.BYE_PROTOCOL_ERROR)
                return

    def on_app(self, type_: int, payload: bytes) -> None:
        if self.app == "skbr-conformance" and type_ == ECHO_REQUEST:
            self.send(ECHO_REPLY, payload)
        elif self.app == "minecraft-skylines" and type_ == sb.HOST_STATUS:
            event(f"app host_status {sb.HostStatus.decode(payload)}")
        elif self.app == "minecraft-skylines" and type_ == sb.GUEST_STATUS:
            event(f"app guest_status {sb.GuestStatus.decode(payload)}")
        else:
            event(f"app type=0x{type_:04x} bytes={len(payload)}")


def run_host(a) -> None:
    lsock = socket.socket()
    lsock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    lsock.bind(("127.0.0.1", a.port))
    lsock.listen(8)
    active: list[Session | None] = [None]
    guard = threading.Lock()
    event("state listening")
    print("READY", flush=True)

    def handle(sock: socket.socket) -> None:
        c = sb.Conn(sock)
        try:
            f = c.recv_frame(5)
        except (TimeoutError, sb.ProtocolError, OSError):
            c.close(); return
        if f is None:
            c.close(); return
        type_, flags, payload = f
        if type_ != sb.HELLO or flags != 0:
            try:
                c.send_frame(sb.GOODBYE, sb.Goodbye(sb.BYE_PROTOCOL_ERROR, "expected HELLO").encode())
            except OSError:
                pass
            c.close(); return
        try:
            h = sb.Hello.decode(payload)
        except sb.ProtocolError:
            c.close(); return
        if h.magic != sb.MAGIC:
            c.close(); return

        def reject(code: int, reason: str) -> None:
            c.send_frame(sb.WELCOME, sb.Welcome(False, code, reason, a.app, 1, 0, a.name, "0",
                                                a.heartbeat_ms, a.timeout_ms, 0).encode())
            event(f"rejected code={code}")
            c.close()

        if h.bridge_version != sb.BRIDGE_VERSION:
            return reject(sb.REJECT_BRIDGE_VERSION, f"bridge version {h.bridge_version} != 1")
        if h.app_protocol != a.app:
            return reject(sb.REJECT_APP_PROTOCOL, f"app protocol {h.app_protocol!r} != {a.app!r}")
        if h.app_major != 1:
            return reject(sb.REJECT_APP_MAJOR, f"app major {h.app_major} != 1")
        with guard:
            if active[0] is not None:
                return reject(sb.REJECT_BUSY, "another guest is connected")

            def ended() -> None:
                with guard:
                    active[0] = None
                event("state listening")

            s = Session(c, a.app, a.heartbeat_ms, a.timeout_ms, ended)
            active[0] = s
        c.send_frame(sb.WELCOME, sb.Welcome(True, 0, "", a.app, 1, 0, a.name, "0", a.heartbeat_ms,
                                            a.timeout_ms, random.getrandbits(63) | 1).encode())
        event(f"state connected peer={h.peer_name!r} version={h.peer_version!r}")
        if a.app == "minecraft-skylines":
            s.send(sb.HOST_STATUS, sb.HostStatus(1, "Reference City", uuid.uuid4(), "reference").encode())
        s.start()

    def accept_loop() -> None:
        while True:
            try:
                sock, _ = lsock.accept()
            except OSError:
                return
            threading.Thread(target=handle, args=(sock,), daemon=True).start()

    threading.Thread(target=accept_loop, daemon=True).start()
    sys.stdin.read()  # until stdin closes
    with guard:
        s = active[0]
    if s:
        s.end("local_goodbye", sb.BYE_SHUTTING_DOWN, "shutting down", send_bye=sb.BYE_SHUTTING_DOWN)
    lsock.close()


def run_guest(a) -> None:
    stop = threading.Event()
    current: list[Session | None] = [None]

    def loop() -> None:
        attempts = 0
        while not stop.is_set():
            delay = a.retry_ms / 1000 if attempts < 10 else 5.0
            attempts += 1
            event("state connecting")
            try:
                sock = socket.create_connection(("127.0.0.1", a.port), timeout=1.0)
            except OSError:
                stop.wait(delay); continue
            c = sb.Conn(sock)
            c.send_frame(sb.HELLO, sb.Hello(a.app, a.major, 0, a.name, "0", random.getrandbits(64)).encode())
            event("state handshaking")
            try:
                f = c.recv_until({sb.WELCOME, sb.GOODBYE}, 5)
            except (TimeoutError, sb.ProtocolError):
                f = None
            if f is None or f[0] == sb.GOODBYE:
                event("disconnected cause=connection_lost code=-1 reason='no WELCOME'")
                c.close(); stop.wait(delay); continue
            w = sb.Welcome.decode(f[2])
            if not w.accepted:
                event(f"disconnected cause=rejected code={w.reject_code} reason={w.reject_reason!r}")
                c.close()
                if w.reject_code in (sb.REJECT_BRIDGE_VERSION, sb.REJECT_APP_PROTOCOL, sb.REJECT_APP_MAJOR):
                    return
                stop.wait(delay); continue
            attempts = 0
            done = threading.Event()
            s = Session(c, a.app, w.heartbeat_interval_ms, w.peer_timeout_ms, done.set)
            current[0] = s
            event(f"state connected session={w.session_id} peer={w.peer_name!r}")
            if a.app == "minecraft-skylines":
                s.send(sb.GUEST_STATUS, sb.GuestStatus(1, "reference-world", uuid.UUID(int=0)).encode())
            s.start()
            done.wait()
            current[0] = None
            stop.wait(delay)

    threading.Thread(target=loop, daemon=True).start()
    print("READY", flush=True)
    sys.stdin.read()
    stop.set()
    s = current[0]
    if s:
        s.end("local_goodbye", sb.BYE_SHUTTING_DOWN, "shutting down", send_bye=sb.BYE_SHUTTING_DOWN)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("role", choices=["host", "guest"])
    ap.add_argument("--port", type=int, default=47615)
    ap.add_argument("--app", default="minecraft-skylines")
    ap.add_argument("--major", type=int, default=1)
    ap.add_argument("--heartbeat-ms", type=int, default=1000)
    ap.add_argument("--timeout-ms", type=int, default=5000)
    ap.add_argument("--retry-ms", type=int, default=1000)
    ap.add_argument("--name", default="python-reference")
    a = ap.parse_args()
    (run_host if a.role == "host" else run_guest)(a)
    # Exit without interpreter finalization: daemon threads may still be blocked in socket calls,
    # and finalizing under them aborts Python 3.14 (SIGABRT, seen 2026-10-05). The GOODBYE has
    # already been sent synchronously by now.
    sys.stdout.flush()
    os._exit(0)


if __name__ == "__main__":
    main()
