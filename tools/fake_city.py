#!/usr/bin/env python3
"""Sandbox stand-in for the Cities: Skylines host (minecraft-skylines app protocol 1.2).

Listens as the bridge host, sends status, collision geometry and ENTER_PLAYER_MODE, then drives
scripted INPUT and prints the guest's PLAYER_STATE. Summarises BLOCK_ATLAS (optionally saving the PNG with
--atlas-out), SECTION_MESH, SECTIONS_CLEAR and ATLAS_REGION, and can send one DEBUG_COMMAND (--debug-command).
--self-check decodes the 1.2 golden vectors and exits. See protocol/minecraft-skylines-v1.md.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import socket
import sys
import threading
import time
import uuid

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "protocol", "reference"))
import skbridge as sb  # noqa: E402

APP = "minecraft-skylines"
MINOR = 2
VECTORS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "protocol", "vectors", "frames.json")
START = time.monotonic()
HB_MS, TIMEOUT_MS = 1000, 5000
FLAG_NAMES = ["in_world", "on_ground", "sneaking", "sprinting", "swimming", "flying", "dead", "held"]
KEY_W, KEY_SPACE = 87, 32


def uptime_ms() -> int:
    return int((time.monotonic() - START) * 1000)


def cross(a, b, c):
    u = [b[i] - a[i] for i in range(3)]
    v = [c[i] - a[i] for i in range(3)]
    return (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])


def tri(a, b, c, flags, up=True):
    n = cross(a, b, c)
    assert (n[1] > 0) if up else (n[1] < 0), f"bad winding {a} {b} {c} normal {n}"
    return (list(a) + list(b) + list(c), flags)


def quad(x0, x1, z0, z1, y00, y01, flags, up=True):
    """Horizontal-ish quad; y00 = height at z0, y01 = height at z1. Normal up, or down if not up."""
    a, b = (x0, y00, z0), (x0, y01, z1)
    c, d = (x1, y00, z0), (x1, y01, z1)
    if up:
        return [tri(a, b, c, flags), tri(c, b, d, flags)]
    return [tri(a, c, b, flags, up=False), tri(c, d, b, flags, up=False)]


def build_regions():
    regions = {(rx, rz): [] for rx in range(-3, 4) for rz in range(-3, 4)}

    def file(t):
        v = t[0]
        regions[(math.floor(v[0] / 16), math.floor(v[2] / 16))].append(t)

    for (rx, rz) in list(regions):
        for t in quad(rx * 16, rx * 16 + 16, rz * 16, rz * 16 + 16, 64, 64, 1):
            regions[(rx, rz)].append(t)
    for t in quad(8, 12, 8, 24, 64, 68, 2):  # ramp
        file(t)
    for t in quad(-12, -4, -8, 8, 68, 68, 4):  # bridge top
        file(t)
    for t in quad(-12, -4, -8, 8, 67.5, 67.5, 4, up=False):  # bridge underside
        file(t)
    return regions


def describe(type_: int, payload: bytes, atlas_out: str | None = None) -> str | None:
    """One-line summary of a 1.2 render frame (None for other types); writes the atlas PNG to atlas_out."""
    if type_ == sb.BLOCK_ATLAS:
        a = sb.BlockAtlas.decode(payload)
        png_ok = a.data[:8] == b"\x89PNG\r\n\x1a\n"
        if atlas_out:
            with open(atlas_out, "wb") as f:
                f.write(a.data)
        return (f"BLOCK_ATLAS {a.width}x{a.height} format={a.fmt} bytes={len(a.data)} png={'ok' if png_ok else 'BAD'}"
                + (f" -> {atlas_out}" if atlas_out else ""))
    if type_ == sb.SECTION_MESH:
        m = sb.SectionMesh.decode(payload)
        n = len(m.vertices)
        cut = sum(1 for v in m.vertices if v[7] & 1)
        tr = sum(1 for v in m.vertices if v[7] & 2)
        return f"SECTION_MESH section=({m.sx},{m.sy},{m.sz}) vertices={n}" + (" (empty: drop)" if n == 0 else f" cutout={cut} translucent={tr}")
    if type_ == sb.SECTIONS_CLEAR:
        return "SECTIONS_CLEAR"
    if type_ == sb.ATLAS_REGION:
        r = sb.AtlasRegion.decode(payload)
        return f"ATLAS_REGION ({r.x},{r.y}) {r.width}x{r.height}"
    return None


def self_check() -> int:
    """Decodes the 1.2 golden vectors through describe() and the reference codecs; returns a process exit code."""
    vectors = {v["name"]: v for v in json.load(open(VECTORS))["valid"]}
    bad = 0

    def check(name, cond, what):
        nonlocal bad
        print(f"{'ok  ' if cond else 'FAIL'} {name}: {what}", flush=True)
        bad += 0 if cond else 1

    def frame_of(name):
        raw = bytes.fromhex(vectors[name]["hex"])
        length, type_, _ = sb.HEADER.unpack_from(raw)
        return type_, raw[sb.HEADER.size:sb.HEADER.size + length], vectors[name]["fields"]

    t, p, f = frame_of("block_atlas")
    line = describe(t, p)
    check("block_atlas", line == f"BLOCK_ATLAS {f['width']}x{f['height']} format={f['format']} bytes={len(f['dataHex']) // 2} png=ok", line)
    t, p, f = frame_of("atlas_region")
    r = sb.AtlasRegion.decode(p)
    check("atlas_region", (r.x, r.y, r.width, r.height, r.rgba.hex()) == (f["x"], f["y"], f["width"], f["height"], f["rgbaHex"]), describe(t, p))
    for name in ("section_mesh", "section_mesh_empty"):
        t, p, f = frame_of(name)
        m = sb.SectionMesh.decode(p)
        want = [(v["x"], v["y"], v["z"], v["u"], v["v"], v["color"], v["light"], v["flags"]) for v in f["vertices"]]
        check(name, (m.sx, m.sy, m.sz) == (f["sx"], f["sy"], f["sz"]) and [tuple(v) for v in m.vertices] == want
              and t == sb.SECTION_MESH, describe(t, p))
    t, p, _ = frame_of("sections_clear")
    check("sections_clear", describe(t, p) == "SECTIONS_CLEAR" and p == b"", describe(t, p))
    t, p, f = frame_of("debug_command")
    check("debug_command", t == sb.DEBUG_COMMAND and sb.DebugCommand(f["command"]).encode() == p
          and sb.DebugCommand.decode(p).command == f["command"], f["command"])
    print(f"self-check: {'PASS' if bad == 0 else f'{bad} FAILED'}", flush=True)
    return 1 if bad else 0


class Link:
    def __init__(self, conn: sb.Conn) -> None:
        self.c = conn
        self.lock = threading.Lock()
        self.closed = False
        self.last_rx = time.monotonic()
        self.peer_goodbye = None
        self.states: list[sb.PlayerState] = []
        self.state_cv = threading.Condition()
        self.hb_seq = 0
        self.quiet = 1
        self.last_print = 0.0
        self.count = 0
        self.atlas_out = None
        self.meshes = 0
        self.mesh_vertices = 0
        self.regions = 0

    def send(self, type_: int, payload: bytes) -> None:
        with self.lock:
            if self.closed:
                raise OSError("link closed")
            self.c.send_frame(type_, payload)

    def start(self) -> None:
        threading.Thread(target=self._read, daemon=True).start()
        threading.Thread(target=self._beat, daemon=True).start()

    def _beat(self) -> None:
        while not self.closed:
            time.sleep(HB_MS / 1000)
            if self.closed:
                return
            if (time.monotonic() - self.last_rx) * 1000 > TIMEOUT_MS:
                print("peer silent, closing", flush=True)
                self.closed = True
                return
            self.hb_seq += 1
            try:
                self.send(sb.HEARTBEAT, sb.Heartbeat(self.hb_seq, uptime_ms()).encode())
            except OSError:
                return

    def _read(self) -> None:
        while not self.closed:
            try:
                f = self.c.recv_frame(0.1)
            except TimeoutError:
                continue
            except (sb.ProtocolError, OSError):
                f = None
            if f is None:
                self.closed = True
                return
            self.last_rx = time.monotonic()
            type_, _, payload = f
            try:
                if type_ == sb.GOODBYE:
                    self.peer_goodbye = sb.Goodbye.decode(payload)
                    print(f"guest GOODBYE {self.peer_goodbye}", flush=True)
                    self.closed = True
                    return
                if type_ == sb.GUEST_STATUS:
                    print(f"GUEST_STATUS {sb.GuestStatus.decode(payload)}", flush=True)
                elif type_ == sb.PLAYER_STATE:
                    self._state(sb.PlayerState.decode(payload))
                elif type_ in (sb.BLOCK_ATLAS, sb.SECTION_MESH, sb.SECTIONS_CLEAR, sb.ATLAS_REGION):
                    self._render(type_, payload)
                # heartbeats and echoed host-direction frames are ignored
            except sb.ProtocolError as e:
                print(f"decode error on 0x{type_:04x}: {e}", flush=True)

    def _state(self, ps: sb.PlayerState) -> None:
        with self.state_cv:
            self.states.append(ps)
            self.state_cv.notify_all()
        self.count += 1
        now = time.monotonic()
        if self.count % self.quiet or now - self.last_print < 0.2:
            return
        self.last_print = now
        v = ps.values
        flags = ",".join(n for i, n in enumerate(FLAG_NAMES) if v["flags"] >> i & 1) or "-"
        print(f"STATE tick={v['tickSeq']} flags={flags} ack={v['teleportAck']} "
              f"feet=({v['x']:.3f},{v['y']:.3f},{v['z']:.3f}) eyeY={v['eyeY']:.3f} "
              f"yaw={v['yaw']:.2f} pitch={v['pitch']:.2f} fov={v['fovDeg']:.1f}", flush=True)

    def _render(self, type_: int, payload: bytes) -> None:
        line = describe(type_, payload, self.atlas_out)
        if type_ == sb.SECTION_MESH:
            self.meshes += 1
            self.mesh_vertices += len(payload) // sb.VERTEX.size
        elif type_ == sb.ATLAS_REGION:
            self.regions += 1
            if self.regions > 5 and self.regions % 500:
                return  # animated sprites arrive every tick
        print(line, flush=True)

    def wait_ack(self, seq: int, timeout_s: float) -> bool:
        deadline = time.monotonic() + timeout_s
        with self.state_cv:
            while not self.closed:
                if any(s.values["teleportAck"] == seq for s in self.states):
                    return True
                left = deadline - time.monotonic()
                if left <= 0:
                    return False
                self.state_cv.wait(min(left, 0.1))
        return False


def accept_guest(port: int) -> Link:
    ls = socket.socket()
    ls.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    ls.bind(("127.0.0.1", port))
    ls.listen(1)
    print(f"listening on 127.0.0.1:{port}", flush=True)
    while True:
        sock, _ = ls.accept()
        c = sb.Conn(sock)
        f = c.recv_frame(5)
        if f is None or f[0] != sb.HELLO:
            c.close()
            continue
        h = sb.Hello.decode(f[2])

        def reject(code: int, reason: str) -> None:
            c.send_frame(sb.WELCOME, sb.Welcome(False, code, reason, APP, 1, MINOR, "fake_city", "0",
                                                HB_MS, TIMEOUT_MS, 0).encode())
            c.close()

        if h.magic != sb.MAGIC or h.bridge_version != sb.BRIDGE_VERSION:
            reject(sb.REJECT_BRIDGE_VERSION, "bad bridge version"); continue
        if h.app_protocol != APP:
            reject(sb.REJECT_APP_PROTOCOL, "wrong app protocol"); continue
        if h.app_major != 1:
            reject(sb.REJECT_APP_MAJOR, "wrong major"); continue
        c.send_frame(sb.WELCOME, sb.Welcome(True, 0, "", APP, 1, MINOR, "fake_city", "0", HB_MS,
                                            TIMEOUT_MS, uuid.uuid4().int >> 65 | 1).encode())
        print(f"handshake complete: peer={h.peer_name!r} version={h.peer_version!r} "
              f"guest minor={h.app_minor} negotiated minor={min(h.app_minor, MINOR)}", flush=True)
        ls.close()
        link = Link(c)
        link.negotiated = min(h.app_minor, MINOR)
        return link


def finish(link: Link, code: int = 0) -> None:
    try:
        link.send(sb.GOODBYE, sb.Goodbye(sb.BYE_NORMAL, "fake_city done").encode())
        link.c.sock.shutdown(socket.SHUT_WR)
    except OSError:
        pass
    time.sleep(0.2)
    sys.stdout.flush()
    os._exit(code)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=47615)
    ap.add_argument("--seconds", type=float, default=30.0)
    ap.add_argument("--quiet-states", type=int, default=1, metavar="N", help="print only every Nth state")
    ap.add_argument("--handshake-only", action="store_true")
    ap.add_argument("--atlas-out", metavar="PATH", help="write the BLOCK_ATLAS PNG here")
    ap.add_argument("--debug-command", metavar="CMD", help="send this DEBUG_COMMAND (no leading slash) after the handshake")
    ap.add_argument("--self-check", action="store_true", help="decode the 1.2 golden vectors and exit")
    a = ap.parse_args()
    if a.self_check:
        sys.exit(self_check())
    t_end = time.monotonic() + a.seconds

    link = accept_guest(a.port)
    link.quiet = max(1, a.quiet_states)
    link.atlas_out = a.atlas_out
    link.start()
    link.send(sb.HOST_STATUS, sb.HostStatus(1 | 8, "fake city", uuid.UUID(int=0), "fake").encode())
    if link.negotiated < 1:
        print("WARNING: negotiated minor < 1, not sending player-mode messages", flush=True)
        time.sleep(1)
        finish(link)

    if a.debug_command:
        if link.negotiated >= 2:
            link.send(sb.DEBUG_COMMAND, sb.DebugCommand(a.debug_command).encode())
            print(f"sent DEBUG_COMMAND {a.debug_command!r}", flush=True)
        else:
            print("WARNING: negotiated minor < 2, not sending DEBUG_COMMAND", flush=True)

    regions = build_regions()
    link.send(sb.COLLISION_RESET, sb.CollisionReset(1).encode())
    for (rx, rz), tris in sorted(regions.items()):
        link.send(sb.COLLISION_REGION, sb.CollisionRegion(1, rx, rz, tris).encode())
    print(f"sent {len(regions)} regions, {sum(len(t) for t in regions.values())} triangles", flush=True)
    link.send(sb.ENTER_PLAYER_MODE, sb.EnterPlayerMode(1, 0.5, 64.0, 0.5, 0.0, 0.0, 1).encode())

    if a.handshake_only:
        time.sleep(1)
        finish(link)

    acked = link.wait_ack(1, 8.0)
    print(f"teleport ack {'received' if acked else 'NOT received (8 s)'}", flush=True)
    t0 = time.monotonic()
    yaw, pressed_w, space_state = 0.0, False, 0  # 0 not yet, 1 pressed, 2 released
    t_space = None
    while not link.closed and time.monotonic() < t_end:
        t = time.monotonic() - t0
        events = []
        if not pressed_w and t < 2.0:
            events.append(sb.InputEvent(sb.IN_KEY, 1, KEY_W)); pressed_w = True
        if pressed_w and t >= 2.0:
            events.append(sb.InputEvent(sb.IN_KEY, 0, KEY_W)); pressed_w = False
            events.append(sb.InputEvent(sb.IN_KEY, 1, KEY_SPACE)); space_state, t_space = 1, t
        if space_state == 1 and t - t_space >= 0.1:
            events.append(sb.InputEvent(sb.IN_KEY, 0, KEY_SPACE)); space_state = 2
        yaw = 45.0 * min(t / 2.0, 1.0)
        try:
            link.send(sb.INPUT, sb.Input(yaw, 0.0, events).encode())
        except OSError:
            break
        time.sleep(1 / 60)

    try:
        link.send(sb.EXIT_PLAYER_MODE, sb.ExitPlayerMode("fake_city done").encode())
    except OSError:
        pass

    with link.state_cv:
        st = [s.values for s in link.states]
    print(f"SUMMARY states={len(st)} section_meshes={link.meshes} mesh_vertices={link.mesh_vertices} "
          f"atlas_regions={link.regions}", flush=True)
    if st:
        f, l = st[0], st[-1]
        print(f"  first feet=({f['x']:.3f},{f['y']:.3f},{f['z']:.3f}) last feet=({l['x']:.3f},{l['y']:.3f},{l['z']:.3f})", flush=True)
        print(f"  max teleportAck={max(s['teleportAck'] for s in st)} "
              f"held seen={any(s['flags'] & 128 for s in st)}", flush=True)
    finish(link)


if __name__ == "__main__":
    main()
