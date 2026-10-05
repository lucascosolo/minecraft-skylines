"""Reference codec for the SKBR bridge protocol v1 (protocol/bridge-v1.md).

Pure standard library. Used to generate the golden vectors, as the scriptable fake peer in
conformance.py, and as a stand-in host or guest during development. The spec wins over this file.
"""
from __future__ import annotations

import socket
import struct
import time
import uuid
from dataclasses import dataclass, field

MAGIC = 0x52424B53
BRIDGE_VERSION = 1
HEADER = struct.Struct("<IHH")
MAX_PAYLOAD = 16 * 1024 * 1024

HELLO, WELCOME, HEARTBEAT, GOODBYE = 0x0001, 0x0002, 0x0003, 0x0004
APP_MIN = 0x0100

REJECT_BRIDGE_VERSION, REJECT_APP_PROTOCOL, REJECT_APP_MAJOR, REJECT_BUSY, REJECT_NOT_READY = 1, 2, 3, 4, 5
BYE_NORMAL, BYE_SHUTTING_DOWN, BYE_PROTOCOL_ERROR, BYE_TIMEOUT, BYE_BACKPRESSURE = 0, 1, 2, 3, 4


class ProtocolError(Exception):
    pass


class Writer:
    def __init__(self) -> None:
        self.buf = bytearray()

    def u8(self, v: int) -> "Writer":
        self.buf += struct.pack("<B", v); return self

    def u16(self, v: int) -> "Writer":
        self.buf += struct.pack("<H", v); return self

    def u32(self, v: int) -> "Writer":
        self.buf += struct.pack("<I", v); return self

    def u64(self, v: int) -> "Writer":
        self.buf += struct.pack("<Q", v); return self

    def i32(self, v: int) -> "Writer":
        self.buf += struct.pack("<i", v); return self

    def f32(self, v: float) -> "Writer":
        self.buf += struct.pack("<f", v); return self

    def f64(self, v: float) -> "Writer":
        self.buf += struct.pack("<d", v); return self

    def bool(self, v: bool) -> "Writer":
        return self.u8(1 if v else 0)

    def string(self, s: str) -> "Writer":
        b = s.encode("utf-8")
        if len(b) > 0xFFFF:
            raise ValueError("string longer than 65535 bytes")
        self.u16(len(b)); self.buf += b; return self

    def uuid(self, u: uuid.UUID) -> "Writer":
        self.buf += u.bytes; return self

    def bytes(self) -> bytes:
        return bytes(self.buf)


class Reader:
    def __init__(self, data: bytes) -> None:
        self.data, self.pos = data, 0

    def _take(self, n: int) -> bytes:
        if self.pos + n > len(self.data):
            raise ProtocolError("payload truncated")
        b = self.data[self.pos:self.pos + n]; self.pos += n; return b

    def u8(self) -> int: return self._take(1)[0]
    def u16(self) -> int: return struct.unpack("<H", self._take(2))[0]
    def u32(self) -> int: return struct.unpack("<I", self._take(4))[0]
    def u64(self) -> int: return struct.unpack("<Q", self._take(8))[0]
    def i32(self) -> int: return struct.unpack("<i", self._take(4))[0]
    def f32(self) -> float: return struct.unpack("<f", self._take(4))[0]
    def f64(self) -> float: return struct.unpack("<d", self._take(8))[0]

    def bool(self) -> bool:
        v = self.u8()
        if v > 1:
            raise ProtocolError("bool out of range")
        return v == 1

    def string(self) -> str:
        n = self.u16()
        try:
            return self._take(n).decode("utf-8")
        except UnicodeDecodeError as e:
            raise ProtocolError("invalid UTF-8") from e

    def uuid(self) -> uuid.UUID:
        return uuid.UUID(bytes=self._take(16))


def frame(type_: int, payload: bytes = b"", flags: int = 0) -> bytes:
    return HEADER.pack(len(payload), type_, flags) + payload


@dataclass
class Hello:
    app_protocol: str
    app_major: int
    app_minor: int
    peer_name: str
    peer_version: str
    session_nonce: int
    magic: int = MAGIC
    bridge_version: int = BRIDGE_VERSION

    def encode(self) -> bytes:
        return (Writer().u32(self.magic).u16(self.bridge_version).string(self.app_protocol)
                .u16(self.app_major).u16(self.app_minor).string(self.peer_name)
                .string(self.peer_version).u64(self.session_nonce).bytes())

    @staticmethod
    def decode(p: bytes) -> "Hello":
        r = Reader(p)
        magic, bv = r.u32(), r.u16()
        return Hello(magic=magic, bridge_version=bv, app_protocol=r.string(), app_major=r.u16(),
                     app_minor=r.u16(), peer_name=r.string(), peer_version=r.string(),
                     session_nonce=r.u64())


@dataclass
class Welcome:
    accepted: bool
    reject_code: int
    reject_reason: str
    app_protocol: str
    app_major: int
    app_minor: int
    peer_name: str
    peer_version: str
    heartbeat_interval_ms: int
    peer_timeout_ms: int
    session_id: int
    bridge_version: int = BRIDGE_VERSION

    def encode(self) -> bytes:
        return (Writer().bool(self.accepted).u16(self.reject_code).string(self.reject_reason)
                .u16(self.bridge_version).string(self.app_protocol).u16(self.app_major)
                .u16(self.app_minor).string(self.peer_name).string(self.peer_version)
                .u32(self.heartbeat_interval_ms).u32(self.peer_timeout_ms).u64(self.session_id)
                .bytes())

    @staticmethod
    def decode(p: bytes) -> "Welcome":
        r = Reader(p)
        accepted, code, reason, bv = r.bool(), r.u16(), r.string(), r.u16()
        return Welcome(accepted=accepted, reject_code=code, reject_reason=reason, bridge_version=bv,
                       app_protocol=r.string(), app_major=r.u16(), app_minor=r.u16(),
                       peer_name=r.string(), peer_version=r.string(),
                       heartbeat_interval_ms=r.u32(), peer_timeout_ms=r.u32(), session_id=r.u64())


@dataclass
class Heartbeat:
    seq: int
    sender_uptime_ms: int

    def encode(self) -> bytes:
        return Writer().u32(self.seq).u64(self.sender_uptime_ms).bytes()

    @staticmethod
    def decode(p: bytes) -> "Heartbeat":
        r = Reader(p)
        return Heartbeat(seq=r.u32(), sender_uptime_ms=r.u64())


@dataclass
class Goodbye:
    code: int
    reason: str

    def encode(self) -> bytes:
        return Writer().u16(self.code).string(self.reason).bytes()

    @staticmethod
    def decode(p: bytes) -> "Goodbye":
        r = Reader(p)
        return Goodbye(code=r.u16(), reason=r.string())


# ---- minecraft-skylines app protocol 1.0 (protocol/minecraft-skylines-v1.md) ----------------
HOST_STATUS, GUEST_STATUS = 0x0100, 0x0101


@dataclass
class HostStatus:
    flags: int
    city_name: str
    save_id: uuid.UUID
    game_version: str

    def encode(self) -> bytes:
        return (Writer().u32(self.flags).string(self.city_name).uuid(self.save_id)
                .string(self.game_version).bytes())

    @staticmethod
    def decode(p: bytes) -> "HostStatus":
        r = Reader(p)
        return HostStatus(flags=r.u32(), city_name=r.string(), save_id=r.uuid(), game_version=r.string())


@dataclass
class GuestStatus:
    flags: int
    world_name: str
    paired_save_id: uuid.UUID

    def encode(self) -> bytes:
        return Writer().u32(self.flags).string(self.world_name).uuid(self.paired_save_id).bytes()

    @staticmethod
    def decode(p: bytes) -> "GuestStatus":
        r = Reader(p)
        return GuestStatus(flags=r.u32(), world_name=r.string(), paired_save_id=r.uuid())


# ---- socket helpers ---------------------------------------------------------------------------
class Conn:
    """A blocking connection with a receive deadline, for scripted tests."""

    def __init__(self, sock: socket.socket) -> None:
        self.sock = sock
        sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        self.buf = bytearray()

    @staticmethod
    def connect(port: int, timeout_s: float = 5.0) -> "Conn":
        deadline = time.monotonic() + timeout_s
        while True:
            try:
                return Conn(socket.create_connection(("127.0.0.1", port), timeout=1.0))
            except OSError:
                if time.monotonic() > deadline:
                    raise
                time.sleep(0.05)

    def send(self, data: bytes) -> None:
        self.sock.sendall(data)

    def send_frame(self, type_: int, payload: bytes = b"", flags: int = 0) -> None:
        self.send(frame(type_, payload, flags))

    def recv_frame(self, timeout_s: float) -> tuple[int, int, bytes] | None:
        """Return (type, flags, payload), or None if the peer closed. Raises TimeoutError."""
        deadline = time.monotonic() + timeout_s
        while True:
            if len(self.buf) >= HEADER.size:
                length, type_, flags = HEADER.unpack_from(self.buf)
                if length > MAX_PAYLOAD:
                    raise ProtocolError("oversize frame")
                if len(self.buf) >= HEADER.size + length:
                    payload = bytes(self.buf[HEADER.size:HEADER.size + length])
                    del self.buf[:HEADER.size + length]
                    return type_, flags, payload
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise TimeoutError("no frame before deadline")
            self.sock.settimeout(remaining)
            try:
                chunk = self.sock.recv(65536)
            except socket.timeout as e:
                raise TimeoutError("no frame before deadline") from e
            except ConnectionResetError:
                return None
            if not chunk:
                return None
            self.buf += chunk

    def recv_until(self, wanted: set[int], timeout_s: float) -> tuple[int, int, bytes] | None:
        """Skip frames (heartbeats etc.) until one of `wanted` types arrives."""
        deadline = time.monotonic() + timeout_s
        while True:
            f = self.recv_frame(max(0.0, deadline - time.monotonic()))
            if f is None or f[0] in wanted:
                return f

    def wait_closed(self, timeout_s: float) -> list[tuple[int, int, bytes]]:
        """Read until EOF; return the frames seen. Raises TimeoutError if still open."""
        seen = []
        deadline = time.monotonic() + timeout_s
        while True:
            f = self.recv_frame(max(0.0, deadline - time.monotonic()))
            if f is None:
                return seen
            seen.append(f)

    def close(self) -> None:
        try:
            self.sock.close()
        except OSError:
            pass


@dataclass
class HostConfig:
    app_protocol: str = "skbr-conformance"
    app_major: int = 1
    app_minor: int = 0
    heartbeat_interval_ms: int = 200
    peer_timeout_ms: int = 1000
    name: str = "python-reference-host"
    version: str = "0"
    extra: dict = field(default_factory=dict)
